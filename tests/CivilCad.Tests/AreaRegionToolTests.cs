using System.Runtime.ExceptionServices;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

[Collection("SharedToolSettings")] // 共有の設定を書き換える
public class AreaRegionToolTests
{
    private static void RunSta(Action<CadDocument, CadCanvas> action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var doc = new CadDocument { ScaleDenominator = 1 };
                MeasureSettings.AreaByRegion = true;
                action(doc, new CadCanvas { Document = doc });
            }
            catch (Exception ex) { error = ex; }
            finally
            {
                MeasureSettings.AreaByRegion = false;
                MeasureSettings.PlaceText = true;
                MeasureSettings.Detail = false;
                MeasureSettings.Decimals = 3;
                MeasureSettings.Unit = LengthUnit.M;
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    /// <summary>10 x 5 の長方形を作る 4 本の線 (互いにはみ出して交わる) と、遠くの 1 本。</summary>
    private static List<LineEntity> Lines(CadDocument doc)
    {
        var l = doc.CurrentLayer;
        var lines = new List<LineEntity>
        {
            new(l, new Vec2(-2, 0), new Vec2(12, 0)),
            new(l, new Vec2(-2, 5), new Vec2(12, 5)),
            new(l, new Vec2(0, -3), new Vec2(0, 8)),
            new(l, new Vec2(10, -3), new Vec2(10, 8)),
        };
        doc.Entities.AddRange(lines);
        return lines;
    }

    [Fact]
    public void ClickedBoundary_ThenInside_PlacesArea() => RunSta((doc, canvas) =>
    {
        var lines = Lines(doc);
        var far = new LineEntity(doc.CurrentLayer, new Vec2(100, 100), new Vec2(110, 100)); // 境界に選ばない線
        doc.Entities.Add(far);
        string? message = null;
        canvas.Message += (_, m) => message = m;
        var tool = new AreaTool();
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(5, 0));          // 下の線
        tool.OnPoint(new Vec2(5, 5));          // 上の線
        tool.OnPoint(new Vec2(0, 2));          // 左
        tool.OnPoint(new Vec2(10, 2));         // 右
        tool.OnPoint(new Vec2(105, 100));      // 選んでから外す
        tool.OnPoint(new Vec2(105, 100));
        tool.OnEnter();                        // 境界を確定
        tool.OnMouseMove(new Vec2(5, 2));
        tool.OnPoint(new Vec2(5, 2));          // 内側 → 文字がカーソルに付く
        Assert.Equal(5, doc.Entities.Count);   // まだ置いていない
        tool.OnPoint(new Vec2(5, 2));          // 置く
        var text = Assert.Single(doc.Entities.OfType<TextEntity>());
        Assert.Equal("A=50.000m²", text.Text);

        // 続けて外側 (囲まれていない所) をクリック → 見つからず、文字は出ない
        tool.OnPoint(new Vec2(50, 50));
        Assert.Single(doc.Entities.OfType<TextEntity>());
        Assert.Contains("見つかりません", message);

        // 一つ戻す: 境界の指定へ (選んだ 4 本は残る)。もう一度 Enter で内側へ
        tool.StepBack();
        tool.OnEnter();
        MeasureSettings.Detail = true;
        tool.OnPoint(new Vec2(5, 2));
        tool.OnPoint(new Vec2(3, 3));
        Assert.Equal("A=50.000m²  (周長 30.000m)", doc.Entities.OfType<TextEntity>().Last().Text);
        Assert.Equal(lines.Count + 1 + 2, doc.Entities.Count);
    });

    [Fact]
    public void EnterWithNothing_UsesAllVisibleEntities() => RunSta((doc, canvas) =>
    {
        Lines(doc);
        var tool = new AreaTool();
        canvas.SetTool(tool);
        tool.OnEnter();                        // 何も選ばずに Enter = 表示中のすべての図形
        tool.OnPoint(new Vec2(5, 2));
        tool.OnPoint(new Vec2(5, 2));
        Assert.Equal("A=50.000m²", Assert.Single(doc.Entities.OfType<TextEntity>()).Text);

        // 外側の細長い部分は囲まれていない。Enter で境界の指定に戻る
        tool.OnPoint(new Vec2(-1, 2));
        Assert.Single(doc.Entities.OfType<TextEntity>());
    });

    [Fact]
    public void Selection_IsUsedAsBoundary() => RunSta((doc, canvas) =>
    {
        var lines = Lines(doc);
        canvas.SetSelection(lines);
        var tool = new AreaTool();
        canvas.SetTool(tool);                  // 選択中の図形は境界に入っているが、境界の指定は続く
        tool.OnEnter();                        // 境界を確定
        tool.OnPoint(new Vec2(5, 2));
        tool.OnPoint(new Vec2(5, 2));
        Assert.Equal("A=50.000m²", Assert.Single(doc.Entities.OfType<TextEntity>()).Text);
    });

    [Fact]
    public void OneSelectedLine_StillPicking_CanAddMoreBoundary() => RunSta((doc, canvas) =>
    {
        var lines = Lines(doc);
        canvas.SetSelection([lines[0]]);
        string? message = null;
        canvas.Message += (_, m) => message = m;
        var tool = new AreaTool();
        canvas.SetTool(tool);                  // 1 本だけ選択中でも、境界の指定中のまま
        tool.OnPoint(new Vec2(5, 5));          // 上の線を足す
        tool.OnPoint(new Vec2(0, 2));          // 左
        tool.OnPoint(new Vec2(10, 2));         // 右
        tool.OnEnter();
        tool.OnPoint(new Vec2(5, 2));
        tool.OnPoint(new Vec2(5, 2));
        Assert.Equal("A=50.000m²", Assert.Single(doc.Entities.OfType<TextEntity>()).Text);

        // 閉じていない境界のメッセージには境界の数が出る
        tool.StepBack();                       // 境界の指定へ
        tool.StepBack();                       // 右の線を外す
        tool.OnEnter();
        tool.OnPoint(new Vec2(5, 2));
        Assert.Contains("境界 3 個", message);
    });

    [Fact]
    public void PointMode_StillWorks() => RunSta((doc, canvas) =>
    {
        MeasureSettings.AreaByRegion = false;
        var tool = new AreaTool();
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(0, 0));
        tool.OnPoint(new Vec2(4, 0));
        tool.OnPoint(new Vec2(4, 3));
        tool.OnEnter();
        tool.OnPoint(new Vec2(1, 1));
        Assert.Equal("A=6.000m²", Assert.Single(doc.Entities.OfType<TextEntity>()).Text);
    });
}
