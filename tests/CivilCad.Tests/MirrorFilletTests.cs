using System.Runtime.ExceptionServices;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Editing;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class MirrorFilletTests
{
    private static readonly Layer L = new("0", RgbColor.White);

    private static LineEntity Line(double x1, double y1, double x2, double y2) => new(L, new Vec2(x1, y1), new Vec2(x2, y2));

    private static void Near(Vec2 expected, Vec2 actual, double tol = 1e-9) =>
        Assert.True(expected.DistanceTo(actual) <= tol, $"expected {expected}, actual {actual}");

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

    // ---- ミラー ----

    [Fact]
    public void ReflectAcross_VerticalAndDiagonalAxis()
    {
        Near(new Vec2(8, 3), new Vec2(2, 3).ReflectAcross(new Vec2(5, 0), new Vec2(5, 10)));
        Near(new Vec2(3, 1), new Vec2(1, 3).ReflectAcross(Vec2.Zero, new Vec2(1, 1)));
        Near(new Vec2(1, 3), new Vec2(1, 3).ReflectAcross(Vec2.Zero, Vec2.Zero)); // 軸が決まらなければそのまま
    }

    [Fact]
    public void Mirror_Arc_StaysCounterClockwiseOnMirroredSide()
    {
        // 中心 (0,0) R=2 の右上 1/4 円 (0°→90°) を y 軸で裏返すと左上 1/4 円 (90°→180°)
        var arc = new ArcEntity(L, Vec2.Zero, 2, 0, Math.PI / 2);
        arc.Mirror(Vec2.Zero, new Vec2(0, 1));
        Near(new Vec2(0, 2), arc.StartPoint);
        Near(new Vec2(-2, 0), arc.EndPoint);
        Assert.Equal(Math.PI / 2, arc.Sweep, 9);
    }

    [Fact]
    public void Mirror_Text_KeepsAngleAndMovesBoxCenter()
    {
        var text = new TextEntity(L, new Vec2(1, 0), "ABCD", 1); // 幅 2 (半角 0.5H × 4)、枠の中心 (2, 0.5)
        text.Mirror(new Vec2(5, 0), new Vec2(5, 1));
        Assert.Equal(0, text.Rotation);
        Near(new Vec2(8, 0.5), text.Bounds.Center);
    }

    [Fact]
    public void Mirror_Dimension_StaysReadable()
    {
        var dim = new DimensionEntity(L, new Vec2(0, 0), new Vec2(4, 0), new Vec2(2, 1), 0, 0.5, 0.3);
        dim.Mirror(new Vec2(10, 0), new Vec2(10, 1));
        Near(new Vec2(16, 0), dim.P1); // P1・P2 を入れ替えて左から右のまま
        Near(new Vec2(20, 0), dim.P2);
        Near(new Vec2(18, 1), dim.LinePoint);
        Assert.Equal(0, dim.Angle!.Value, 9);
        Assert.Equal(4, dim.Measured, 9);
    }

    [Fact]
    public void MirrorTool_KeepsOriginal_AndUndo() => RunSta(() =>
    {
        var doc = new CadDocument();
        var line = new LineEntity(doc.CurrentLayer, new Vec2(0, 0), new Vec2(2, 3));
        doc.Entities.Add(line);
        var canvas = new CadCanvas { Document = doc };
        canvas.SetSelection([line]);
        var tool = new MirrorTool();
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(5, 0));
        tool.OnPoint(new Vec2(5, 10));

        Assert.Equal(2, doc.Entities.Count);
        var copy = (LineEntity)doc.Entities[1];
        Near(new Vec2(10, 0), copy.Start);
        Near(new Vec2(8, 3), copy.End);
        Near(new Vec2(2, 3), line.End); // 元はそのまま
        Assert.Same(copy, Assert.Single(canvas.Selection));
        doc.History.Undo();
        Assert.Single(doc.Entities);
    });

    // ---- フィレット ----

    [Fact]
    public void Fillet_RightAngle_TrimsToTangentPoints()
    {
        // 交差する 2 本 (十字)。右側と上側をクリックして R=1 でつなぐ
        var h = Line(-5, 0, 5, 0);
        var v = Line(0, -5, 0, 5);
        var r = Fillet.Create(h, new Vec2(3, 0), v, new Vec2(0, 3), 1, out var error);
        Assert.Equal(Fillet.Error.None, error);
        Assert.NotNull(r);
        Near(new Vec2(1, 0), r.Line1!.Start); // 向きは元のまま (始点が接点側)
        Near(new Vec2(5, 0), r.Line1.End);
        Near(new Vec2(0, 1), r.Line2!.Start);
        Near(new Vec2(0, 5), r.Line2.End);
        var arc = r.Arc!;
        Near(new Vec2(1, 1), arc.Center);
        Assert.Equal(1, arc.Radius, 9);
        Assert.Equal(Math.PI / 2, arc.Sweep, 9);
        // 円弧の両端が接点
        var ends = new[] { arc.StartPoint, arc.EndPoint };
        Assert.Contains(ends, p => p.DistanceTo(new Vec2(1, 0)) < 1e-9);
        Assert.Contains(ends, p => p.DistanceTo(new Vec2(0, 1)) < 1e-9);
    }

    [Fact]
    public void Fillet_ZeroRadius_ExtendsLinesToCorner()
    {
        // 離れた 2 本 (L 字の手前で止まっている) を角まで伸ばす
        var h = Line(2, 0, 6, 0);
        var v = Line(0, 2, 0, 6);
        var r = Fillet.Create(h, new Vec2(4, 0), v, new Vec2(0, 4), 0, out _)!;
        Assert.Null(r.Arc);
        Near(Vec2.Zero, r.Line1!.Start);
        Near(new Vec2(6, 0), r.Line1.End);
        Near(Vec2.Zero, r.Line2!.Start);
    }

    [Fact]
    public void Fillet_ObtuseAngle_ArcIsTangent()
    {
        // 120° に開いた 2 本
        var a = Line(0, 0, 10, 0);
        var b = new LineEntity(L, Vec2.Zero, Vec2.FromAngle(2 * Math.PI / 3) * 10);
        var r = Fillet.Create(a, new Vec2(5, 0), b, Vec2.FromAngle(2 * Math.PI / 3) * 5, 2, out _)!;
        var arc = r.Arc!;
        // 中心から両方の線までの距離が R
        Assert.Equal(2, arc.Center.Y, 9);
        var n = Vec2.FromAngle(2 * Math.PI / 3).LeftNormal;
        Assert.Equal(2, Math.Abs(arc.Center.Dot(n)), 9);
        Assert.Equal(Math.PI / 3, arc.Sweep, 9); // π − 120°
    }

    [Fact]
    public void Fillet_Errors()
    {
        var h = Line(0, 0, 5, 0);
        Assert.Null(Fillet.Create(h, new Vec2(1, 0), Line(0, 1, 5, 1), new Vec2(1, 1), 1, out var e1));
        Assert.Equal(Fillet.Error.Parallel, e1);
        Assert.Null(Fillet.Create(h, new Vec2(1, 0), Line(0, 0, 0, 1), new Vec2(0, 0.5), 3, out var e2));
        Assert.Equal(Fillet.Error.TooLarge, e2);
        Assert.Null(Fillet.Create(h, new Vec2(1, 0), h, new Vec2(2, 0), 1, out var e3));
        Assert.Equal(Fillet.Error.SameLine, e3);
    }

    [Fact]
    public void FilletTool_ReplacesLinesAndAddsArc_OneUndo() => RunSta(() =>
    {
        var doc = new CadDocument();
        var h = new LineEntity(doc.CurrentLayer, new Vec2(0, 0), new Vec2(10, 0));
        var v = new LineEntity(doc.CurrentLayer, new Vec2(0, 0), new Vec2(0, 10));
        doc.Entities.AddRange([h, v]);
        var canvas = new CadCanvas { Document = doc };
        var tool = new FilletTool();
        canvas.SetTool(tool);
        tool.OnText("2");
        tool.OnPoint(new Vec2(6, 0));
        tool.OnPoint(new Vec2(0, 6));

        Assert.Equal(3, doc.Entities.Count);
        Assert.Single(doc.Entities.OfType<ArcEntity>());
        Assert.Contains(doc.Entities.OfType<LineEntity>(), l => l.Start.DistanceTo(new Vec2(2, 0)) < 1e-9);
        Assert.Same(tool, canvas.Tool); // 続けて別の角にも使える
        doc.History.Undo();
        Assert.Equal(2, doc.Entities.Count);
        Assert.Contains(h, doc.Entities);
        Assert.Contains(v, doc.Entities);
        tool.OnText("0"); // 半径は次回も覚えている (テスト同士で残らないよう 0 に戻す)
    });
}
