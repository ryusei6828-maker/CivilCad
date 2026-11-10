using System.Runtime.ExceptionServices;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>複写で、基点の後にシートを切り替えて別のシートへ複写する。</summary>
[Collection("SharedToolSettings")]
public class CrossSheetCopyTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
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

    private static (CadDocument a, CadDocument b, CadCanvas canvas, LineEntity line) Setup()
    {
        var a = new CadDocument();
        var road = new Layer("道路", new RgbColor(255, 0, 0));
        a.Layers.Add(road);
        var line = new LineEntity(road, new Vec2(10, 20), new Vec2(30, 20));
        a.Entities.Add(line);
        var canvas = new CadCanvas { Document = a };
        canvas.SetSelection([line]);
        return (a, new CadDocument(), canvas, line);
    }

    [Fact]
    public void CopyToOtherSheet_PlacesOnNamedLayer_UndoPerSheet() => RunSta(() =>
    {
        var (a, b, canvas, line) = Setup();
        var tool = new MoveCopyTool(copy: true);
        canvas.SetTool(tool);
        Assert.False(tool.KeepsAcrossSheets);
        tool.OnPoint(new Vec2(10, 20));
        Assert.True(tool.KeepsAcrossSheets);

        canvas.Document = b; // シートを切り替えた (ツールは続く)
        Assert.Same(tool, canvas.Tool);
        Assert.Contains("別のシート", tool.Prompt);
        tool.OnPoint(new Vec2(100, 200));
        tool.OnPoint(new Vec2(0, 0)); // 続けて置ける

        Assert.Equal(2, b.Entities.Count);
        var copy = Assert.IsType<LineEntity>(b.Entities[0]);
        Near(new Vec2(100, 200), copy.Start);
        Near(new Vec2(120, 200), copy.End);
        Assert.Equal("道路", copy.Layer.Name);
        Assert.Same(b.FindLayer("道路"), copy.Layer);
        Assert.NotSame(line.Layer, copy.Layer);
        Assert.Equal(new RgbColor(255, 0, 0), copy.Layer.Color);
        Assert.Single(a.Entities); // 元のシートは変わらない
        Near(new Vec2(10, 20), line.Start);

        b.History.Undo();
        Assert.Single(b.Entities);
        Assert.Single(a.Entities);
    });

    [Fact]
    public void BackToSourceSheet_BehavesAsNormalCopy() => RunSta(() =>
    {
        var (a, b, canvas, _) = Setup();
        var tool = new MoveCopyTool(copy: true);
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(0, 0));
        canvas.Document = b;
        canvas.Document = a;
        tool.OnPoint(new Vec2(5, 5));
        Assert.Equal(2, a.Entities.Count);
        Near(new Vec2(15, 25), ((LineEntity)a.Entities[1]).Start);
        Assert.Empty(b.Entities);
    });

    [Fact]
    public void Group_GetsNewGroupId() => RunSta(() =>
    {
        var (a, b, canvas, line) = Setup();
        var l2 = new LineEntity(line.Layer, new Vec2(0, 0), new Vec2(1, 1));
        a.Entities.Add(l2);
        a.History.Execute(new SetGroupCommand([line, l2], Guid.NewGuid(), "グループ化"));
        canvas.SetSelection([line, l2]);
        var tool = new MoveCopyTool(copy: true);
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(0, 0));
        canvas.Document = b;
        tool.OnPoint(new Vec2(1, 1));
        Assert.Equal(2, b.Entities.Count);
        Assert.NotNull(b.Entities[0].GroupId);
        Assert.Equal(b.Entities[0].GroupId, b.Entities[1].GroupId);
        Assert.NotEqual(line.GroupId, b.Entities[0].GroupId);
    });

    [Fact]
    public void DifferentSurveyRotation_KeepsSurveyCoordinates() => RunSta(() =>
    {
        var (a, b, canvas, line) = Setup();
        b.SetSurveyFrame(Math.PI / 2, new Vec2(0, 0));
        var tool = new MoveCopyTool(copy: true);
        canvas.SetTool(tool);
        var basePt = new Vec2(10, 20);
        tool.OnPoint(basePt);
        canvas.Document = b;
        // 基点と同じ測量座標の位置をクリックすれば、図形は測量座標が変わらない
        var click = b.FromSurveyCad(a.ToSurveyCad(basePt));
        tool.OnPoint(click);
        var copy = (LineEntity)b.Entities[0];
        Near(a.ToSurveyCad(line.Start), b.ToSurveyCad(copy.Start));
        Near(a.ToSurveyCad(line.End), b.ToSurveyCad(copy.End));
    });
}
