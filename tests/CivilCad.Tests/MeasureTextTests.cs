using System.Runtime.ExceptionServices;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

[Collection("SharedToolSettings")] // 共有の設定を書き換える
public class MeasureTextTests
{
    private static void RunSta(Action<CadDocument, CadCanvas> action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var doc = new CadDocument { ScaleDenominator = 1 };
                action(doc, new CadCanvas { Document = doc });
            }
            catch (Exception ex) { error = ex; }
            finally
            {
                MeasureSettings.PlaceText = true;
                MeasureSettings.Detail = false;
                MeasureSettings.Decimals = 3;
                MeasureSettings.TextHeightMm = 2.5;
                MeasureSettings.Unit = LengthUnit.M;
                MeasureSettings.StationOffset = true;
                MeasureSettings.StationMark = false;
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void Distance_PlacesTextAlongLine() => RunSta((doc, canvas) =>
    {
        var tool = new DistanceTool();
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(10, 10));
        tool.OnPoint(new Vec2(4, 2));          // 右から左へ (逆さまにならない角度で書く)
        Assert.Empty(doc.Entities);            // まだ置いていない
        tool.OnPoint(new Vec2(7, 7));          // 置く位置
        var t = Assert.Single(doc.Entities.OfType<TextEntity>());
        Assert.Equal("L=10.000m", t.Text);
        Assert.Equal(new Vec2(7, 7), t.Position);
        Assert.Equal(Math.Atan2(8, 6), t.Rotation, 9);   // (4,2)→(10,10) の向き
        Assert.Equal(doc.PaperMmToMeters(2.5), t.Height, 9);

        // Enter で置かずに次へ
        tool.OnPoint(new Vec2(0, 0));
        tool.OnPoint(new Vec2(3, 4));
        tool.OnEnter();
        Assert.Single(doc.Entities.OfType<TextEntity>());

        // 置く前に「一つ戻す」で 2 点目の指定に戻り、別の点で測り直せる
        MeasureSettings.Detail = true;
        MeasureSettings.Decimals = 1;
        tool.OnPoint(new Vec2(0, 0));
        tool.OnPoint(new Vec2(99, 99));
        tool.StepBack();
        Assert.Equal(new Vec2(0, 0), tool.BasePoint);
        tool.OnPoint(new Vec2(0, 5));          // 北へ 5m
        tool.OnPoint(new Vec2(1, 1));
        Assert.Equal("L=5.0m  θ=0°00'00.0\"",doc.Entities.OfType<TextEntity>().Last().Text);
    });

    [Fact]
    public void Scale_IsNotApplied_InChosenUnit() => RunSta((doc, canvas) =>
    {
        // 図面は実寸 (m) で描いているので、縮尺 1:100 でも 1m の線は 1m = 100cm
        doc.ScaleDenominator = 100;
        MeasureSettings.Unit = LengthUnit.Cm;
        MeasureSettings.Decimals = 1;
        var tool = new DistanceTool();
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(0, 0));
        tool.OnPoint(new Vec2(1, 0));
        tool.OnPoint(new Vec2(1, 1));
        Assert.Equal("L=100.0cm", doc.Entities.OfType<TextEntity>().Last().Text);

        // 面積: 2m × 3m = 6m²
        MeasureSettings.Unit = LengthUnit.M;
        MeasureSettings.Detail = true;
        var area = new AreaTool();
        canvas.SetTool(area);
        foreach (var p in new Vec2[] { new(0, 0), new(2, 0), new(2, 3), new(0, 3) }) area.OnPoint(p);
        area.OnEnter();
        area.OnPoint(new Vec2(1, 1));
        Assert.Equal("A=6.0m²  (周長 10.0m)", doc.Entities.OfType<TextEntity>().Last().Text);

        Assert.Equal(60000, MeasureSettings.Area(6) * 10000, 6); // m² → cm² の確認 (×10⁴)
        MeasureSettings.Unit = LengthUnit.Mm;
        Assert.Equal(1000, MeasureSettings.Length(1), 9);        // 1m → 1000mm

        // A3・1:100 の図枠は端から端まで 38m
        MeasureSettings.Unit = LengthUnit.M;
        MeasureSettings.Detail = false;
        var frame = CivilCad.App.Drawing.DrawingFrame.Build(doc, new CivilCad.App.Drawing.DrawingFrameOptions { Paper = "A3", ScaleDenominator = 100, Margin = 20 }, Vec2.Zero);
        var outer = frame.OfType<PolylineEntity>().First().Bounds;
        canvas.SetTool(tool);
        tool.OnPoint(outer.Min);
        tool.OnPoint(new Vec2(outer.Max.X, outer.Min.Y));
        tool.OnPoint(new Vec2(5, 5));
        Assert.Equal("L=38.0m", doc.Entities.OfType<TextEntity>().Last().Text);
    });

    [Fact]
    public void Area_PlacesText_OrSkipsWhenOff() => RunSta((doc, canvas) =>
    {
        var tool = new AreaTool();
        canvas.SetTool(tool);
        foreach (var p in new Vec2[] { new(0, 0), new(10, 0), new(10, 10), new(0, 10) }) tool.OnPoint(p);
        tool.OnEnter();                        // 計算 → 文字を置く手順へ
        tool.OnPoint(new Vec2(5, 5));
        var t = Assert.Single(doc.Entities.OfType<TextEntity>());
        Assert.Equal("A=100.000m²", t.Text);
        Assert.Equal(0, t.Rotation);

        MeasureSettings.PlaceText = false;     // 記入しない設定なら、計算だけで次へ
        foreach (var p in new Vec2[] { new(0, 0), new(4, 0), new(0, 3) }) tool.OnPoint(p);
        tool.OnEnter();
        Assert.Single(doc.Entities.OfType<TextEntity>());
        Assert.Null(tool.BasePoint);
    });

    [Fact]
    public void StationQuery_PlacesStationText() => RunSta((doc, canvas) =>
    {
        // 直線の路線 (0,0)→(100,0)。(32.5, -5) は 南側 = 進行方向の右
        var al = CivilCad.Alignment.HorizontalAlignment.FromIps(
        [
            new("BP", Vec2.Zero),
            new("EP", new Vec2(100, 0)),
        ]);
        var tool = new StationQueryTool(al, 20);
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(32.5, -5));
        Assert.Empty(doc.Entities);
        tool.OnPoint(new Vec2(40, -8));        // 置く位置
        var t = Assert.Single(doc.Entities.OfType<TextEntity>());
        Assert.Single(doc.Entities);           // 印はオフ
        Assert.Equal("No.1+12.500  右 5.000m", t.Text);
        Assert.Equal(new Vec2(40, -8), t.Position);
        Assert.Equal(0, t.Rotation, 9);

        // 幅なし・印あり・左側
        MeasureSettings.StationOffset = false;
        MeasureSettings.StationMark = true;
        tool.OnPoint(new Vec2(50, 4));
        tool.OnPoint(new Vec2(55, 6));
        Assert.Equal("No.2+10.000", doc.Entities.OfType<TextEntity>().Last().Text);
        var line = Assert.Single(doc.Entities.OfType<LineEntity>());
        Assert.True(line.Start.DistanceTo(new Vec2(50, 0)) < 1e-5);
        Assert.True(line.End.DistanceTo(new Vec2(50, 4)) < 1e-9);

        MeasureSettings.StationOffset = true;
        tool.OnPoint(new Vec2(60, 3));
        tool.OnPoint(new Vec2(61, 5));
        Assert.Contains("左 3.000m", doc.Entities.OfType<TextEntity>().Last().Text);
    });
}
