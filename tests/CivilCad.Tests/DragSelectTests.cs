using System.Runtime.ExceptionServices;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class DragSelectTests
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

    /// <summary>線 a: (0,0)-(10,0)、線 b: (20,0)-(30,0)。表示倍率 1 (1m = 1px) のキャンバス。</summary>
    private static (CadDocument doc, CadCanvas canvas, LineEntity a, LineEntity b) Setup()
    {
        var doc = new CadDocument();
        var a = new LineEntity(doc.CurrentLayer, new Vec2(0, 0), new Vec2(10, 0));
        var b = new LineEntity(doc.CurrentLayer, new Vec2(20, 0), new Vec2(30, 0));
        doc.Entities.Add(a);
        doc.Entities.Add(b);
        var canvas = new CadCanvas { Document = doc };
        canvas.SetView(Vec2.Zero, 1);
        return (doc, canvas, a, b);
    }

    [Fact]
    public void Select_DragWindowAndCrossing_ClickEmptyClears() => RunSta(() =>
    {
        var (_, canvas, a, b) = Setup();
        var tool = new SelectTool();
        canvas.SetTool(tool);

        // 左→右の窓: a だけ完全に入る
        tool.OnPress(new Vec2(-1, -5));
        Assert.True(tool.IsDragging);
        tool.OnMouseMove(new Vec2(15, 5));
        tool.OnRelease(new Vec2(15, 5));
        Assert.False(tool.IsDragging);
        Assert.Same(a, Assert.Single(canvas.Selection));

        // 空き部分をクリック (動かさない) → 解除
        tool.OnPress(new Vec2(50, 50));
        tool.OnRelease(new Vec2(50.5, 50));
        Assert.Empty(canvas.Selection);

        // 右→左の交差: b にかかれば選ばれる
        tool.OnPress(new Vec2(25, 5));
        tool.OnRelease(new Vec2(15, -5));
        Assert.Same(b, Assert.Single(canvas.Selection));

        // 図形のクリックで追加
        tool.OnPress(new Vec2(5, 0.2));
        tool.OnRelease(new Vec2(5, 0.2));
        Assert.Equal(2, canvas.Selection.Count);
    });

    [Fact]
    public void Erase_DragRemovesInsideBox_ClickRemovesHit() => RunSta(() =>
    {
        var (doc, canvas, a, b) = Setup();
        var tool = new EraseTool();
        canvas.SetTool(tool);

        tool.OnPress(new Vec2(50, 50));       // 空きクリックは何もしない
        tool.OnRelease(new Vec2(50, 50));
        Assert.Equal(2, doc.Entities.Count);

        tool.OnPress(new Vec2(-1, -5));        // 窓で a を削除
        tool.OnRelease(new Vec2(15, 5));
        Assert.Same(b, Assert.Single(doc.Entities));

        tool.OnPress(new Vec2(25, 0));         // クリックで b を削除
        tool.OnRelease(new Vec2(25, 0));
        Assert.Empty(doc.Entities);
    });
}
