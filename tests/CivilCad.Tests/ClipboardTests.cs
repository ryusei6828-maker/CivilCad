using System.Runtime.ExceptionServices;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>図形のコピー・貼り付け (別のシートへ)。コピーの中身はアプリ全体で 1 つなので、テストは 1 つのクラスにまとめる。</summary>
public class ClipboardTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally { EntityClipboard.Clear(); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static void Near(Vec2 expected, Vec2 actual)
    {
        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
    }

    [Fact]
    public void CopyToOtherSheet_SamePosition_LayerByName() => RunSta(() =>
    {
        var a = new CadDocument();
        var road = new Layer("道路", new RgbColor(255, 0, 0));
        a.Layers.Add(road);
        var line = new LineEntity(road, new Vec2(10, 20), new Vec2(30, 20));
        a.Entities.Add(line);
        EntityClipboard.Copy(a, [line]);
        line.Translate(new Vec2(100, 0)); // コピーした後で元を動かしても、コピーした時点の形

        var b = new CadDocument();
        var pasted = Assert.IsType<LineEntity>(Assert.Single(EntityClipboard.CreateFor(b)));
        Assert.NotSame(line, pasted);
        Near(new Vec2(10, 20), pasted.Start);
        Assert.Same(b.FindLayer("道路"), pasted.Layer);   // 貼り付け先に同じ名前・色のレイヤを作る
        Assert.Equal(new RgbColor(255, 0, 0), pasted.Layer.Color);
        Assert.DoesNotContain(road, b.Layers);

        // 2 回目は既にあるレイヤを使う
        var again = Assert.Single(EntityClipboard.CreateFor(b));
        Assert.Same(pasted.Layer, again.Layer);
        Assert.Single(b.Layers, l => l.Name == "道路");
    });

    [Fact]
    public void Paste_KeepsSurveyCoordinates_WhenFramesDiffer() => RunSta(() =>
    {
        var a = new CadDocument();
        a.SetSurveyFrame(Math.PI / 2, new Vec2(0, 0));
        var line = new LineEntity(a.CurrentLayer, new Vec2(10, 0), new Vec2(20, 0));
        a.Entities.Add(line);
        EntityClipboard.Copy(a, [line]);

        var b = new CadDocument(); // 座標設定の無いシート
        var pasted = (LineEntity)Assert.Single(EntityClipboard.CreateFor(b));
        Assert.Equal(a.ToSurvey(line.Start).X, b.ToSurvey(pasted.Start).X, 6);
        Assert.Equal(a.ToSurvey(line.Start).Y, b.ToSurvey(pasted.Start).Y, 6);
        Assert.Equal(a.ToSurvey(line.End).X, b.ToSurvey(pasted.End).X, 6);
    });

    [Fact]
    public void PasteTool_PlacesAtClick_AndSelects() => RunSta(() =>
    {
        var a = new CadDocument();
        var rect = new PolylineEntity(a.CurrentLayer, [new Vec2(5, 5), new Vec2(15, 5), new Vec2(15, 10), new Vec2(5, 10)], true);
        a.Entities.Add(rect);
        EntityClipboard.Copy(a, [rect]);

        var b = new CadDocument();
        var canvas = new CadCanvas { Document = b };
        var tool = new PasteTool();
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(100, 200)); // 左下をここに
        tool.OnPoint(new Vec2(300, 200)); // 続けてもう 1 つ
        tool.OnEnter();
        Assert.IsType<SelectTool>(canvas.Tool);
        var placed = b.Entities.OfType<PolylineEntity>().ToList();
        Assert.Equal(2, placed.Count);
        Assert.Equal(new Box2(new Vec2(100, 200), new Vec2(110, 205)), placed[0].Bounds);
        Assert.Equal(new Box2(new Vec2(300, 200), new Vec2(310, 205)), placed[1].Bounds);
        Assert.Equal([placed[1]], canvas.Selection);
        b.History.Undo();
        Assert.Single(b.Entities);
        Assert.Single(a.Entities); // コピー元はそのまま
    });
}
