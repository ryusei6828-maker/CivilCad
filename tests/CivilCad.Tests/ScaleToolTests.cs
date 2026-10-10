using System.Runtime.ExceptionServices;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class ScaleToolTests
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

    [Fact]
    public void EveryEntityKind_ScalesAroundOrigin()
    {
        var l = new Layer("0", RgbColor.White);
        var o = new Vec2(10, 10);
        var line = new LineEntity(l, new Vec2(11, 10), new Vec2(12, 12));
        line.Scale(o, 2);
        Assert.Equal(new Vec2(12, 10), line.Start);
        Assert.Equal(new Vec2(14, 14), line.End);

        var arc = new ArcEntity(l, new Vec2(12, 10), 1, 0, 1);
        arc.Scale(o, 0.5);
        Assert.Equal(new Vec2(11, 10), arc.Center);
        Assert.Equal(0.5, arc.Radius);
        Assert.Equal(1, arc.EndAngle);

        var text = new TextEntity(l, new Vec2(10, 12), "A", 0.5) { FitWidth = 3 };
        text.Scale(o, 3);
        Assert.Equal(new Vec2(10, 16), text.Position);
        Assert.Equal(1.5, text.Height, 9);
        Assert.Equal(9, text.FitWidth!.Value, 9);

        var hatch = new HatchEntity(l, [[new Vec2(10, 10), new Vec2(14, 10), new Vec2(14, 14), new Vec2(10, 14)]],
            [new HatchPattern(Math.PI / 4, 0.5, new Vec2(10, 10))]);
        var lines = hatch.Lines.Count;
        hatch.Scale(o, 2);
        Assert.Equal(64, hatch.Area, 9);
        Assert.Equal(1, hatch.Patterns[0].Spacing, 9);
        Assert.Equal(lines, hatch.Lines.Count); // 間隔も一緒に変わるので線の本数は同じ

        var sym = new SymbolEntity(l, SymbolLibrary.NorthArrow, new Vec2(12, 10), 20, 10);
        sym.Scale(o, 1.5);
        Assert.Equal(new Vec2(13, 10), sym.Position);
        Assert.Equal(30, sym.SizeMm, 9);
        Assert.Equal(15, sym.Size, 9);

        var img = new ImageEntity(l, "a.jpg", 10, 5, new Vec2(10, 10), new Vec2(10, 0), new Vec2(0, 5));
        img.Scale(new Vec2(20, 15), 0.5);
        Assert.Equal(new Vec2(15, 12.5), img.Origin);
        Assert.Equal(5, img.Width, 9);
        Assert.Equal(2.5, img.Height, 9);
    }

    [Fact]
    public void ScaleTool_ByFactor_ByReference_UndoAndContinue() => RunSta(() =>
    {
        var doc = new CadDocument();
        var circle = new CircleEntity(doc.CurrentLayer, new Vec2(10, 0), 2);
        var poly = new PolylineEntity(doc.CurrentLayer, [new Vec2(0, 0), new Vec2(4, 0), new Vec2(4, 3)], false);
        doc.Entities.AddRange([circle, poly]);
        var canvas = new CadCanvas { Document = doc };
        canvas.SetSelection([circle, poly]);
        var tool = new ScaleTool();
        canvas.SetTool(tool);

        // 倍率: 基点 (0,0) で 2 倍
        tool.OnPoint(Vec2.Zero);
        Assert.True(tool.OnText("2"));
        var c2 = doc.Entities.OfType<CircleEntity>().Single();
        Assert.Equal(new Vec2(20, 0), c2.Center);
        Assert.Equal(4, c2.Radius, 9);
        Assert.Equal(2, canvas.Selection.Count); // 拡大した図形が選ばれたまま
        Assert.Same(tool, canvas.Tool);           // 続けて使える

        // 参照: 折れ線の 1 辺 (今は 8m) を 2m にする → 0.25 倍
        tool.OnPoint(Vec2.Zero);
        tool.OnPoint(new Vec2(0, 0));
        tool.OnPoint(new Vec2(8, 0));
        Assert.True(tool.OnText("2"));
        var p2 = doc.Entities.OfType<PolylineEntity>().Single();
        Assert.Equal(new Vec2(2, 0), p2.Points[1]);
        Assert.Equal(new Vec2(2, 1.5), p2.Points[2]);

        // 参照の新しい長さはクリックでも: 2m → 6m (3 倍)
        tool.OnPoint(Vec2.Zero);
        tool.OnPoint(new Vec2(0, 0));
        tool.OnPoint(new Vec2(2, 0));
        tool.OnPoint(new Vec2(0, 6));
        Assert.Equal(new Vec2(6, 0), doc.Entities.OfType<PolylineEntity>().Single().Points[1]);

        // 0 や負の倍率は受け付けない
        tool.OnPoint(Vec2.Zero);
        Assert.True(tool.OnText("0"));
        Assert.Equal(new Vec2(6, 0), doc.Entities.OfType<PolylineEntity>().Single().Points[1]);

        // 元に戻す (3 回) で元通り
        doc.History.Undo();
        doc.History.Undo();
        doc.History.Undo();
        Assert.Equal(new Vec2(10, 0), doc.Entities.OfType<CircleEntity>().Single().Center);
        Assert.Equal(2, doc.Entities.OfType<CircleEntity>().Single().Radius);
        Assert.Equal(new Vec2(4, 0), doc.Entities.OfType<PolylineEntity>().Single().Points[1]);
    });

    /// <summary>拡大縮小のやり方の設定 (全体で共有) を毎回もとに戻す。</summary>
    private static void RunScale(Action<CadDocument, CadCanvas, ScaleTool> action) => RunSta(() =>
    {
        try
        {
            var doc = new CadDocument();
            var rect = new PolylineEntity(doc.CurrentLayer, [new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 5), new Vec2(0, 5)], true);
            doc.Entities.Add(rect);
            var canvas = new CadCanvas { Document = doc };
            canvas.SetSelection([rect]);
            var tool = new ScaleTool();
            canvas.SetTool(tool);
            action(doc, canvas, tool);
        }
        finally
        {
            ScaleTool.Mode = ScaleMode.Factor;
            ScaleTool.FixedFactor = null;
            ScaleTool.RotateToMatch = false;
        }
    });

    private static PolylineEntity Rect(CadDocument doc) => doc.Entities.OfType<PolylineEntity>().Single();

    [Fact]
    public void Factor_EnteredFirst_ThenBaseClickApplies() => RunScale((doc, canvas, tool) =>
    {
        Assert.True(tool.SetFactor(3));          // プロパティで倍率を入れる
        Assert.Equal(10, Rect(doc).Bounds.Width); // まだ変えない
        tool.OnPoint(new Vec2(10, 5));             // 基点のクリックで拡大縮小
        Assert.Equal(new Box2(new Vec2(-20, -10), new Vec2(10, 5)), Rect(doc).Bounds);
        tool.OnPoint(Vec2.Zero);                   // 続けて同じ倍率
        Assert.Equal(90, Rect(doc).Bounds.Width, 9);  // 30 → 90
        Assert.False(tool.SetFactor(0));
    });

    [Fact]
    public void Factor_AfterBase_AppliesImmediately() => RunScale((doc, canvas, tool) =>
    {
        tool.OnPoint(Vec2.Zero);
        Assert.True(tool.HasBasePoint);
        Assert.True(tool.SetFactor(0.5));
        Assert.Equal(new Box2(Vec2.Zero, new Vec2(5, 2.5)), Rect(doc).Bounds);
        doc.History.Undo();
        Assert.Equal(10, Rect(doc).Bounds.Width);
    });

    [Fact]
    public void MatchTwoPoints_ScalesAndMoves_KeepsDirection() => RunScale((doc, canvas, tool) =>
    {
        tool.SetMode(ScaleMode.MatchTwoPoints);
        tool.OnPoint(new Vec2(0, 0));     // 元の 1 点目
        tool.OnPoint(new Vec2(10, 0));    // 元の 2 点目
        tool.OnPoint(new Vec2(99, 99));   // 間違えた点
        tool.StepBack();
        Assert.Equal(2, tool.MatchPointCount);
        tool.OnPoint(new Vec2(100, 50));  // 合わせ先の 1 点目
        tool.OnPoint(new Vec2(100, 75));  // 合わせ先の 2 点目 (真上に 25 → 長さ 25 / 10 = 2.5 倍、向きはそのまま)
        Assert.Equal(new Box2(new Vec2(100, 50), new Vec2(125, 62.5)), Rect(doc).Bounds);
        Assert.Equal(0, tool.MatchPointCount); // 続けて使える
        Assert.Same(tool, canvas.Tool);
    });

    [Fact]
    public void MatchTwoPoints_WithRotation() => RunScale((doc, canvas, tool) =>
    {
        ScaleTool.RotateToMatch = true;
        tool.SetMode(ScaleMode.MatchTwoPoints);
        tool.OnPoint(new Vec2(0, 0));
        tool.OnPoint(new Vec2(10, 0));
        tool.OnPoint(new Vec2(100, 50));
        tool.OnPoint(new Vec2(100, 70));  // 真上に 20 → 2 倍・左回り 90°
        var pts = Rect(doc).Points;
        Assert.True(pts[0].DistanceTo(new Vec2(100, 50)) < 1e-9);
        Assert.True(pts[1].DistanceTo(new Vec2(100, 70)) < 1e-9);
        Assert.True(pts[2].DistanceTo(new Vec2(90, 70)) < 1e-9);
        var (k, rad) = ScaleTool.MatchTransform(new Vec2(0, 0), new Vec2(10, 0), new Vec2(100, 50), new Vec2(100, 70), rotate: true);
        Assert.Equal(2.0, k, 12);
        Assert.Equal(Math.PI / 2, rad, 12);
    });

    [Fact]
    public void PropertiesPanel_ShowsScaleInput() => RunScale((doc, canvas, tool) =>
    {
        var panel = new PropertiesPanel();
        var win = new System.Windows.Window { Content = panel, Width = 300, Height = 600, Left = -3000, Top = -3000, ShowInTaskbar = false, WindowStyle = System.Windows.WindowStyle.None };
        win.Show();
        try
        {
            panel.Attach(canvas);
            panel.Refresh();
            Assert.NotNull(panel.ScaleFactorBox);          // 倍率を指定: 倍率の欄
            tool.SetMode(ScaleMode.MatchTwoPoints);
            panel.Refresh();
            Assert.Null(panel.ScaleFactorBox);             // 2 点を合わせる: 倍率の欄は無い
        }
        finally
        {
            win.Close();
        }
    });
}
