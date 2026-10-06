using System.Runtime.ExceptionServices;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>コマンドは 1 つ描いても終わらず、続けて描ける (Esc で終了)。</summary>
public class ContinuousCommandTests
{
    private static void RunSta(Action<CadDocument, CadCanvas> action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                DrawInputMemory.Radius = null;
                DrawInputMemory.ArcRadius = null;
                DrawInputMemory.Length = null;
                DrawInputMemory.AngleDeg = null;
                var doc = new CadDocument();
                action(doc, new CadCanvas { Document = doc });
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void Circle_Arc_Text_Continue() => RunSta((doc, canvas) =>
    {
        var circle = new CircleTool();
        canvas.SetTool(circle);
        for (var i = 0; i < 3; i++)
        {
            circle.OnPoint(new Vec2(i * 10, 0));
            circle.OnPoint(new Vec2(i * 10 + 2, 0));
        }
        Assert.Equal(3, doc.Entities.OfType<CircleEntity>().Count());
        Assert.Same(circle, canvas.Tool);

        var arc = new ArcTool();
        canvas.SetTool(arc);
        for (var i = 0; i < 2; i++)
        {
            arc.OnPoint(new Vec2(i * 10 - 3, 20));
            arc.OnPoint(new Vec2(i * 10, 23));
            arc.OnPoint(new Vec2(i * 10 + 3, 20));
        }
        Assert.Equal(2, doc.Entities.OfType<ArcEntity>().Count());
        Assert.Same(arc, canvas.Tool);

        var text = new TextTool();
        canvas.SetTool(text);
        text.OnPoint(new Vec2(0, 40));
        Assert.True(text.OnText("一つ目"));
        text.OnPoint(new Vec2(0, 50));
        Assert.True(text.OnText("二つ目"));
        Assert.Equal(["一つ目", "二つ目"], doc.Entities.OfType<TextEntity>().Select(t => t.Text));
        Assert.Same(text, canvas.Tool);

        text.OnCancel(); // Esc で終了
        Assert.IsType<SelectTool>(canvas.Tool);
    });

    [Fact]
    public void Line_And_Polyline_EnterStartsNext() => RunSta((doc, canvas) =>
    {
        var line = new LineTool();
        canvas.SetTool(line);
        line.OnPoint(new Vec2(0, 0));
        line.OnPoint(new Vec2(10, 0));
        line.OnEnter();                       // この線を終える → まだ線コマンド
        Assert.Same(line, canvas.Tool);
        Assert.Null(line.BasePoint);
        line.OnPoint(new Vec2(0, 5));         // 新しい線の始点 (前の線とつながらない)
        line.OnPoint(new Vec2(10, 5));
        Assert.Equal(new Vec2(0, 5), doc.Entities.OfType<LineEntity>().Last().Start);
        line.OnEnter();
        line.OnEnter();                       // 何も引いていない Enter でコマンド終了
        Assert.IsType<SelectTool>(canvas.Tool);

        var pl = new PolylineTool();
        canvas.SetTool(pl);
        pl.OnPoint(new Vec2(0, 10));
        pl.OnPoint(new Vec2(5, 10));
        pl.OnEnter();
        pl.OnPoint(new Vec2(0, 20));
        pl.OnPoint(new Vec2(5, 20));
        pl.OnPoint(new Vec2(5, 25));
        Assert.True(pl.OnText("C"));          // 閉じて確定
        Assert.Equal(2, doc.Entities.OfType<PolylineEntity>().Count());
        Assert.Same(pl, canvas.Tool);
    });

    [Fact]
    public void Move_Continues_WithSameTargets() => RunSta((doc, canvas) =>
    {
        var c = new CircleEntity(doc.CurrentLayer, Vec2.Zero, 1);
        doc.Entities.Add(c);
        canvas.SetSelection([c]);
        var move = new MoveCopyTool(copy: false);
        canvas.SetTool(move);
        move.OnPoint(Vec2.Zero);
        move.OnPoint(new Vec2(5, 0));
        move.OnPoint(Vec2.Zero);
        move.OnPoint(new Vec2(0, 3));
        Assert.Equal(new Vec2(5, 3), c.Center);
        Assert.Same(move, canvas.Tool);
    });
}
