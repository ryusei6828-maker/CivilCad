using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Core.Editing;

public static class Offset
{
    /// <summary>
    /// target を distance だけ sidePoint の側へ平行にずらした図形を作る。線・円・円弧・ポリラインが対象。
    /// 作れないとき (内側に縮めて半径が 0 以下になるなど) は null。ポリラインの角は隣り合う辺の延長の交点でつなぐ (留め継ぎ)。
    /// </summary>
    public static Entity? Create(Entity target, double distance, Vec2 sidePoint)
    {
        if (!(distance > 0)) return null;
        switch (target)
        {
            case LineEntity line:
            {
                var dir = line.End - line.Start;
                if (dir.LengthSquared == 0) return null;
                var n = dir.Normalized().LeftNormal * (Side(dir, sidePoint - line.Start) * distance);
                return EditUtil.CopyStyle(line, new LineEntity(line.Layer, line.Start + n, line.End + n));
            }
            case CircleEntity circle:
            {
                var r = RadiusFor(circle.Center, circle.Radius, distance, sidePoint);
                return r > 0 ? EditUtil.CopyStyle(circle, new CircleEntity(circle.Layer, circle.Center, r)) : null;
            }
            case ArcEntity arc:
            {
                var r = RadiusFor(arc.Center, arc.Radius, distance, sidePoint);
                return r > 0 ? EditUtil.CopyStyle(arc, new ArcEntity(arc.Layer, arc.Center, r, arc.StartAngle, arc.EndAngle)) : null;
            }
            case PolylineEntity pl:
                return OffsetPolyline(pl, distance, sidePoint);
            default:
                return null;
        }
    }

    /// <summary>指定点が円の内側なら縮め、外側なら広げる。</summary>
    private static double RadiusFor(Vec2 center, double radius, double distance, Vec2 sidePoint)
    {
        var r = sidePoint.DistanceTo(center) < radius ? radius - distance : radius + distance;
        return r > EditUtil.PointTolerance ? r : 0;
    }

    /// <summary>toPoint が進行方向の左なら +1、右なら -1。</summary>
    private static double Side(Vec2 dir, Vec2 toPoint) => dir.Cross(toPoint) >= 0 ? 1 : -1;

    private static PolylineEntity? OffsetPolyline(PolylineEntity pl, double distance, Vec2 sidePoint)
    {
        var pts = new List<Vec2>();
        foreach (var p in pl.Points)
            if (pts.Count == 0 || pts[^1].DistanceTo(p) > EditUtil.PointTolerance) pts.Add(p);
        var closed = pl.IsClosed && pts.Count > 2;
        if (closed && pts[0].DistanceTo(pts[^1]) <= EditUtil.PointTolerance) pts.RemoveAt(pts.Count - 1);
        if (pts.Count < 2) return null;

        var segCount = closed ? pts.Count : pts.Count - 1;
        Vec2 SegEnd(int i) => pts[(i + 1) % pts.Count];
        // どちら側にずらすかは、指定点に一番近い辺で決める
        var nearest = Enumerable.Range(0, segCount).MinBy(i => GeoMath.DistanceToSegment(sidePoint, pts[i], SegEnd(i)));
        var k = Side(SegEnd(nearest) - pts[nearest], sidePoint - pts[nearest]) * distance;
        var segs = Enumerable.Range(0, segCount).Select(i =>
        {
            var n = (SegEnd(i) - pts[i]).Normalized().LeftNormal * k;
            return (A: pts[i] + n, B: SegEnd(i) + n);
        }).ToList();

        var result = new List<Vec2>();
        for (var i = 0; i < pts.Count; i++)
        {
            var hasPrev = closed || i > 0;
            var hasNext = closed || i < segCount;
            if (hasPrev && hasNext)
            {
                var prev = segs[(i - 1 + segCount) % segCount];
                var next = segs[i % segCount];
                // 平行な辺 (一直線) は交点が無いので、ずらした点をそのまま使う
                result.Add(CurveIntersect.LineLine(prev.A, prev.B, next.A, next.B) is var (t, _) ? prev.A + (prev.B - prev.A) * t : next.A);
            }
            else
            {
                result.Add(hasNext ? segs[i].A : segs[i - 1].B);
            }
        }
        return EditUtil.CopyStyle(pl, new PolylineEntity(pl.Layer, result, closed) { Filled = pl.Filled });
    }
}
