using System.Runtime.ExceptionServices;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class MeasureTextTests
{
    private static void RunSta(Action<CadDocument, CadCanvas> action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var doc = new CadDocument { ScaleDenominator = 500 };
                action(doc, new CadCanvas { Document = doc });
            }
            catch (Exception ex) { error = ex; }
            finally
            {
                MeasureSettings.PlaceText = true;
                MeasureSettings.Detail = false;
                MeasureSettings.Decimals = 3;
                MeasureSettings.TextHeightMm = 2.5;
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
}
