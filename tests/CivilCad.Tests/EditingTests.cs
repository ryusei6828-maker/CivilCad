using CivilCad.Core.Editing;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class EditingTests
{
    private static readonly Layer L = new("0", RgbColor.White);

    private static LineEntity Line(double x1, double y1, double x2, double y2) => new(L, new Vec2(x1, y1), new Vec2(x2, y2));

    private static void Near(Vec2 expected, Vec2 actual, double tol = 1e-9) =>
        Assert.True(expected.DistanceTo(actual) <= tol, $"expected {expected}, actual {actual}");

    // ---- 交点 ----

    [Fact]
    public void Intersect_SegSeg_And_SegCircle()
    {
        var pts = CurveIntersect.Points(new SegmentCurve(new Vec2(0, 0), new Vec2(10, 0)), new SegmentCurve(new Vec2(5, -5), new Vec2(5, 5)));
        Near(new Vec2(5, 0), Assert.Single(pts));
        Assert.Empty(CurveIntersect.Points(new SegmentCurve(new Vec2(0, 0), new Vec2(4, 0)), new SegmentCurve(new Vec2(5, -5), new Vec2(5, 5))));
        Assert.Single(CurveIntersect.Points(new SegmentCurve(new Vec2(0, 0), new Vec2(4, 0)), new SegmentCurve(new Vec2(5, -5), new Vec2(5, 5)), extendA: true));
        var circle = CurveIntersect.Points(new SegmentCurve(new Vec2(-10, 0), new Vec2(10, 0)), new CircleCurve(new Vec2(0, 0), 3));
        Assert.Equal(2, circle.Count);
        // 上半分の円弧とは 2 点 (端点) で交わる
        var half = CurveIntersect.Points(new SegmentCurve(new Vec2(-10, 1), new Vec2(10, 1)), new CircleCurve(new Vec2(0, 0), 3, 0, Math.PI, IsFull: false));
        Assert.Equal(2, half.Count);
        Assert.Empty(CurveIntersect.Points(new SegmentCurve(new Vec2(-10, -1), new Vec2(10, -1)), new CircleCurve(new Vec2(0, 0), 3, 0, Math.PI, IsFull: false)));
    }

    [Fact]
    public void Intersect_CircleCircle()
    {
        var pts = CurveIntersect.CircleCircle(new Vec2(0, 0), 5, new Vec2(8, 0), 5);
        Assert.Equal(2, pts.Count);
        Assert.All(pts, p => Assert.Equal(4.0, p.X, 9));
        Assert.Single(CurveIntersect.CircleCircle(new Vec2(0, 0), 5, new Vec2(10, 0), 5));
        Assert.Empty(CurveIntersect.CircleCircle(new Vec2(0, 0), 5, new Vec2(11, 0), 5));
    }

    // ---- トリム ----

    [Fact]
    public void Trim_LineMiddle_BetweenTwoEdges()
    {
        var target = Line(0, 0, 10, 0);
        var r = TrimExtend.Trim(target, new Vec2(5, 0.1), [Line(3, -1, 3, 1), Line(7, -1, 7, 1)]);
        Assert.NotNull(r);
        Assert.Equal(2, r!.Pieces.Count);
        var a = (LineEntity)r.Pieces[0];
        var b = (LineEntity)r.Pieces[1];
        Near(new Vec2(0, 0), a.Start); Near(new Vec2(3, 0), a.End);
        Near(new Vec2(7, 0), b.Start); Near(new Vec2(10, 0), b.End);
        var removed = (LineEntity)r.Removed;
        Near(new Vec2(3, 0), removed.Start); Near(new Vec2(7, 0), removed.End);
    }

    [Fact]
    public void Trim_LineEnd_KeepsOtherSide()
    {
        var target = Line(0, 0, 10, 0);
        target.Color = new RgbColor(1, 2, 3);
        target.LineWeight = 0.5;
        var r = TrimExtend.Trim(target, new Vec2(9, 0), [Line(3, -1, 3, 1), Line(7, -1, 7, 1)])!;
        var piece = (LineEntity)Assert.Single(r.Pieces);
        Near(new Vec2(0, 0), piece.Start); Near(new Vec2(7, 0), piece.End);
        Assert.Equal(target.Color, piece.Color);
        Assert.Equal(0.5, piece.LineWeight);
    }

    [Fact]
    public void Trim_NoIntersection_ReturnsNull()
    {
        Assert.Null(TrimExtend.Trim(Line(0, 0, 10, 0), new Vec2(5, 0), [Line(20, -1, 20, 1)]));
        // 端点に触れているだけの線は区切りにならない
        Assert.Null(TrimExtend.Trim(Line(0, 0, 10, 0), new Vec2(5, 0), [Line(10, -1, 10, 1)]));
    }

    [Fact]
    public void Trim_Circle_LeavesArc()
    {
        var circle = new CircleEntity(L, new Vec2(0, 0), 5);
        // 縦線 x=0 で左右に分かれる。右側をクリックして消すと左半分が残る
        var r = TrimExtend.Trim(circle, new Vec2(5, 0), [Line(0, -10, 0, 10)])!;
        var arc = (ArcEntity)Assert.Single(r.Pieces);
        Near(new Vec2(0, 5), arc.StartPoint, 1e-9);
        Near(new Vec2(0, -5), arc.EndPoint, 1e-9);
        Assert.Equal(Math.PI, arc.Sweep, 9);
        Near(new Vec2(-5, 0), arc.MidPoint, 1e-9);
    }

    [Fact]
    public void Trim_Arc()
    {
        var arc = new ArcEntity(L, new Vec2(0, 0), 5, 0, Math.PI);
        var r = TrimExtend.Trim(arc, new Vec2(4, 3), [Line(0, -10, 0, 10)])!;
        var piece = (ArcEntity)Assert.Single(r.Pieces);
        Assert.Equal(Math.PI / 2, piece.StartAngle, 9);
        Assert.Equal(Math.PI, piece.EndAngle, 9);
    }

    [Fact]
    public void Trim_OpenPolyline_SplitsIntoTwo()
    {
        var pl = new PolylineEntity(L, [new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10)]);
        var r = TrimExtend.Trim(pl, new Vec2(10, 1), [Line(8, -1, 12, 3), Line(9, 5, 11, 5)])!;
        Assert.Equal(2, r.Pieces.Count);
        var first = (PolylineEntity)r.Pieces[0];
        Assert.Equal([new Vec2(0, 0), new Vec2(9, 0)], first.Points);
        var second = (PolylineEntity)r.Pieces[1];
        Near(new Vec2(10, 5), second.Points[0]);
        Near(new Vec2(10, 10), second.Points[^1]);
        var removed = (PolylineEntity)r.Removed;
        Assert.Equal(3, removed.Points.Count); // (9,0)-(10,0)-(10,5)
    }

    [Fact]
    public void Trim_ClosedPolyline_BecomesOpen()
    {
        var square = new PolylineEntity(L, [new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10), new Vec2(0, 10)], isClosed: true);
        var r = TrimExtend.Trim(square, new Vec2(5, 0), [Line(2, -5, 2, 15), Line(8, -5, 8, 15)])!;
        var kept = (PolylineEntity)Assert.Single(r.Pieces);
        Assert.False(kept.IsClosed);
        Near(new Vec2(8, 0), kept.Points[0]);
        Near(new Vec2(2, 0), kept.Points[^1]);
        Assert.Equal(6, kept.Points.Count); // (8,0)(10,0)(10,10)(0,10)(0,0)(2,0)
        Assert.Equal(40.0 - 6.0, kept.Length, 9);
    }

    // ---- 延長 ----

    [Fact]
    public void Extend_LineToBoundary()
    {
        var target = Line(0, 0, 5, 0);
        var ext = (LineEntity)TrimExtend.Extend(target, new Vec2(4.5, 0), [Line(10, -5, 10, 5), Line(20, -5, 20, 5)])!;
        Near(new Vec2(0, 0), ext.Start);
        Near(new Vec2(10, 0), ext.End);
        // 始点側をクリックすると始点側に伸ばす
        var back = (LineEntity)TrimExtend.Extend(target, new Vec2(0.5, 0), [Line(-3, -5, -3, 5)])!;
        Near(new Vec2(-3, 0), back.Start);
        Near(new Vec2(5, 0), back.End);
    }

    [Fact]
    public void Extend_NothingAhead_ReturnsNull()
    {
        Assert.Null(TrimExtend.Extend(Line(0, 0, 5, 0), new Vec2(5, 0), [Line(10, 1, 10, 5)]));
        Assert.Null(TrimExtend.Extend(Line(0, 0, 5, 0), new Vec2(5, 0), [Line(3, -5, 3, 5)]));
    }

    [Fact]
    public void Extend_ArcToLine()
    {
        var arc = new ArcEntity(L, new Vec2(0, 0), 5, 0, Math.PI / 4);
        var ext = (ArcEntity)TrimExtend.Extend(arc, arc.EndPoint, [Line(0, 0, 0, 10)])!;
        Assert.Equal(0.0, ext.StartAngle, 9);
        Assert.Equal(Math.PI / 2, ext.EndAngle, 9);
    }

    [Fact]
    public void Extend_PolylineLastSegment()
    {
        var pl = new PolylineEntity(L, [new Vec2(0, 0), new Vec2(5, 0), new Vec2(5, 3)]);
        var ext = (PolylineEntity)TrimExtend.Extend(pl, new Vec2(5, 3), [Line(0, 8, 10, 8)])!;
        Near(new Vec2(5, 8), ext.Points[^1]);
        Assert.Equal(3, ext.Points.Count);
    }

    // ---- オフセット ----

    [Fact]
    public void Offset_LineToClickedSide()
    {
        var line = Line(0, 0, 10, 0);
        var up = (LineEntity)Offset.Create(line, 2, new Vec2(5, 1))!;
        Near(new Vec2(0, 2), up.Start); Near(new Vec2(10, 2), up.End);
        var down = (LineEntity)Offset.Create(line, 2, new Vec2(5, -1))!;
        Near(new Vec2(0, -2), down.Start);
    }

    [Fact]
    public void Offset_CircleAndArc()
    {
        var c = new CircleEntity(L, new Vec2(0, 0), 5);
        Assert.Equal(7.0, ((CircleEntity)Offset.Create(c, 2, new Vec2(10, 0))!).Radius, 9);
        Assert.Equal(3.0, ((CircleEntity)Offset.Create(c, 2, new Vec2(1, 0))!).Radius, 9);
        Assert.Null(Offset.Create(c, 6, new Vec2(1, 0)));
        var arc = new ArcEntity(L, new Vec2(0, 0), 5, 0, Math.PI);
        var o = (ArcEntity)Offset.Create(arc, 1, new Vec2(0, 10))!;
        Assert.Equal(6.0, o.Radius, 9);
        Assert.Equal(arc.StartAngle, o.StartAngle);
    }

    [Fact]
    public void Offset_ClosedPolyline_Inward()
    {
        var square = new PolylineEntity(L, [new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10), new Vec2(0, 10)], isClosed: true);
        var inner = (PolylineEntity)Offset.Create(square, 1, new Vec2(5, 5))!;
        Assert.True(inner.IsClosed);
        Assert.Equal(4, inner.Points.Count);
        Near(new Vec2(1, 1), inner.Points[0]);
        Near(new Vec2(9, 1), inner.Points[1]);
        Near(new Vec2(9, 9), inner.Points[2]);
        Near(new Vec2(1, 9), inner.Points[3]);
        var outer = (PolylineEntity)Offset.Create(square, 1, new Vec2(5, -3))!;
        Near(new Vec2(-1, -1), outer.Points[0]);
    }

    [Fact]
    public void Offset_OpenPolyline_MiterCorner()
    {
        var pl = new PolylineEntity(L, [new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10)]);
        var o = (PolylineEntity)Offset.Create(pl, 1, new Vec2(5, 1))!;
        Near(new Vec2(0, 1), o.Points[0]);
        Near(new Vec2(9, 1), o.Points[1]);
        Near(new Vec2(9, 10), o.Points[2]);
    }

    // ---- 連結 ----

    [Fact]
    public void Join_CollinearPiecesBecomeOneLine()
    {
        // トリムで中抜きした線も、つなぎ直すと元の 1 本に戻る
        var a = Line(0, 0, 3, 0);
        var b = Line(7, 0, 10, 0);
        var c = Line(3, 0, 5, 0);
        var r = Join.Run([a, b, c]);
        Assert.Equal(3, r.Removed.Count);
        var merged = (LineEntity)Assert.Single(r.Added);
        Near(new Vec2(0, 0), merged.Start);
        Near(new Vec2(10, 0), merged.End);
    }

    [Fact]
    public void Join_ConnectedLinesBecomePolyline()
    {
        var r = Join.Run([Line(0, 0, 10, 0), Line(10, 10, 10, 0), Line(10, 10, 0, 10)]);
        var pl = (PolylineEntity)Assert.Single(r.Added);
        Assert.Equal(3, r.Removed.Count);
        Assert.False(pl.IsClosed);
        Assert.Equal([new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10), new Vec2(0, 10)], pl.Points);
    }

    [Fact]
    public void Join_LoopBecomesClosedPolyline()
    {
        var r = Join.Run([Line(0, 0, 10, 0), Line(10, 0, 10, 10), Line(10, 10, 0, 10), Line(0, 10, 0, 0)]);
        var pl = (PolylineEntity)Assert.Single(r.Added);
        Assert.True(pl.IsClosed);
        Assert.Equal(4, pl.Points.Count);
        Assert.Equal(100.0, pl.Area, 9);
    }

    [Fact]
    public void Join_CollinearThenChained()
    {
        // 分割された辺 (0,0)-(5,0)-(10,0) と縦の辺 → 1 本のポリライン (頂点は 3 つ)
        var r = Join.Run([Line(0, 0, 5, 0), Line(5, 0, 10, 0), Line(10, 0, 10, 10)]);
        var pl = (PolylineEntity)Assert.Single(r.Added);
        Assert.Equal(3, r.Removed.Count);
        Assert.Equal([new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10)], pl.Points);
    }

    [Fact]
    public void Join_PolylineAndLine()
    {
        var pl = new PolylineEntity(L, [new Vec2(0, 0), new Vec2(5, 0), new Vec2(5, 5)]);
        var r = Join.Run([pl, Line(5, 5, 0, 5)]);
        var joined = (PolylineEntity)Assert.Single(r.Added);
        Assert.Equal(4, joined.Points.Count);
        Assert.Contains(pl, r.Removed);
    }

    [Fact]
    public void Join_ArcsOnSameCircle()
    {
        var a = new ArcEntity(L, new Vec2(0, 0), 5, 0, Math.PI / 2);
        var b = new ArcEntity(L, new Vec2(0, 0), 5, Math.PI, 3 * Math.PI / 2);
        var arc = (ArcEntity)Assert.Single(Join.Run([a, b]).Added);
        // 一番大きい隙間 (どちらも 90°) のうち先に見つかった側以外を埋める → 270° の円弧
        Assert.Equal(1.5 * Math.PI, arc.Sweep, 9);
        var full = Join.Run([a, b, new ArcEntity(L, new Vec2(0, 0), 5, Math.PI / 2, Math.PI), new ArcEntity(L, new Vec2(0, 0), 5, 1.5 * Math.PI, 2 * Math.PI)]);
        Assert.IsType<CircleEntity>(Assert.Single(full.Added));
    }

    [Fact]
    public void Join_UnrelatedLinesUnchanged()
    {
        var r = Join.Run([Line(0, 0, 1, 0), Line(5, 5, 6, 7)]);
        Assert.Empty(r.Removed);
        Assert.Empty(r.Added);
    }
}
