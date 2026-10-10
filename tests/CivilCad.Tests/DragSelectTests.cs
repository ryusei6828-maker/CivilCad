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
    public void Select_DragEitherDirection_FullyInside_ClickEmptyClears() => RunSta(() =>
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

        // 右→左でも同じ: かかるだけの図形は選ばれない
        tool.OnPress(new Vec2(25, 5));
        tool.OnRelease(new Vec2(15, -5));
        Assert.Empty(canvas.Selection);

        // 右→左で完全に囲めば選ばれる
        tool.OnPress(new Vec2(31, 5));
        tool.OnRelease(new Vec2(19, -5));
        Assert.Same(b, Assert.Single(canvas.Selection));

        // 図形のクリックはその図形だけを選ぶ (追加は Shift+クリック)
        tool.OnPress(new Vec2(5, 0.2));
        tool.OnRelease(new Vec2(5, 0.2));
        Assert.Same(a, Assert.Single(canvas.Selection));
    });

    [Fact]
    public void Select_CrossingMode_PicksTouchingEntities() => RunSta(() =>
    {
        var (_, canvas, a, b) = Setup();
        var tool = new SelectTool();
        canvas.SetTool(tool);
        BoxDrag.Mode = BoxSelectMode.Crossing;
        try
        {
            // a の右端と b の左端にかかる範囲 (どちらも完全には入っていない)
            tool.OnPress(new Vec2(5, -5));
            tool.OnRelease(new Vec2(25, 5));
            Assert.Equal(2, canvas.Selection.Count);
        }
        finally
        {
            BoxDrag.Mode = BoxSelectMode.Inside;
        }
    });

    [Fact]
    public void Erase_ClickAndDragOnlyMark_DoubleClickDeletes() => RunSta(() =>
    {
        var (doc, canvas, a, b) = Setup();
        var tool = new EraseTool();
        canvas.SetTool(tool);

        tool.OnPress(new Vec2(50, 50));       // 空きクリックは何もしない
        tool.OnRelease(new Vec2(50, 50));
        Assert.Empty(tool.Marked);

        tool.OnPress(new Vec2(-1, -5));        // 窓で a を選ぶ (まだ消えない)
        tool.OnRelease(new Vec2(15, 5));
        Assert.Equal([a], tool.Marked);
        Assert.Equal(2, doc.Entities.Count);

        tool.OnPress(new Vec2(25, 0));         // クリックで b も選ぶ (まだ消えない)
        tool.OnRelease(new Vec2(25, 0));
        Assert.Equal(2, doc.Entities.Count);
        tool.OnPress(new Vec2(25, 0));         // もう一度クリックで b を解除
        tool.OnRelease(new Vec2(25, 0));
        Assert.Equal([a], tool.Marked);

        tool.OnDoubleClick(new Vec2(50, 50));  // 空きでダブルクリック → 選んだ a だけ削除
        tool.OnRelease(new Vec2(50, 50));
        Assert.Same(b, Assert.Single(doc.Entities));
        Assert.Same(tool, canvas.Tool);        // 削除モードは続く

        // ダブルクリック = 1 回目のクリック (選ぶ) + 2 回目 (確定) で b を削除
        tool.OnPress(new Vec2(25, 0));
        tool.OnRelease(new Vec2(25, 0));
        tool.OnDoubleClick(new Vec2(25, 0));
        tool.OnRelease(new Vec2(25, 0));
        Assert.Empty(doc.Entities);

        // 元に戻すで 1 回分ずつ戻る
        doc.History.Undo();
        Assert.Same(b, Assert.Single(doc.Entities));
    });

    [Fact]
    public void Erase_EnterConfirms_EscClearsThenEnds() => RunSta(() =>
    {
        var (doc, canvas, a, b) = Setup();
        var tool = new EraseTool();
        canvas.SetTool(tool);
        tool.OnPress(new Vec2(25, 0));
        tool.OnRelease(new Vec2(25, 0));
        tool.OnCancel();                       // Esc: 選んだものを解除 (消さない)
        Assert.Empty(tool.Marked);
        Assert.Equal(2, doc.Entities.Count);
        Assert.Same(tool, canvas.Tool);
        tool.OnPress(new Vec2(25, 0));
        tool.OnRelease(new Vec2(25, 0));
        tool.OnEnter();                        // Enter でも確定
        Assert.Same(a, Assert.Single(doc.Entities));
        tool.OnCancel();                       // 何も選んでいなければ終了
        Assert.IsNotType<EraseTool>(canvas.Tool);
    });
}
