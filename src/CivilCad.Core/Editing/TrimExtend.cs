using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Core.Editing;

/// <param name="Pieces">残る部分 (0〜2 個)。元の図形と置き換える。</param>
/// <param name="Removed">切り取られる部分 (プレビュー用)。</param>
public sealed record TrimResult(IReadOnlyList<Entity> Pieces, Entity Removed);

public static class TrimExtend
{
    private const double Eps = 1e-7;

    /// <summary>
    /// target のうち pick に近い部分を、edges との交点で区切って切り取る。
    /// 線・円弧・円・ポリラインが対象。交点が無い (閉じた形状は 2 つ未満) ときは null。
    /// </summary>
    public static TrimResult? Trim(Entity target, Vec2 pick, IEnumerable<Entity> edges)
    {
        if (CurvePath.Of(target) is not { } path) return null;
        var length = path.Length;
        var eps = Eps * Math.Max(1, length);
        var area = target.Bounds.Inflate(EditUtil.PointTolerance);
        var cuts = new List<double>();
        foreach (var edge in edges)
        {
            if (edge == target || !edge.Bounds.Inflate(EditUtil.PointTolerance).Intersects(area)) continue;
            foreach (var e in edge.Curves)
                foreach (var t in path.Curves)
                    foreach (var p in CurveIntersect.Points(t, e))
                        cuts.Add(path.ParamOf(p));
        }
        // 開いた形状の端点にくっついているだけの交点は区切りにしない
        if (!path.Closed) cuts.RemoveAll(s => s <= eps || s >= length - eps);
        cuts = Dedupe(cuts, eps);
        var sc = path.ParamOf(pick);

        if (path.Closed)
        {
            if (cuts.Count < 2) return null;
            var lo = cuts.LastOrDefault(s => s < sc, cuts[^1] - length);
            var hi = cuts.FirstOrDefault(s => s > sc, cuts[0] + length);
            if (lo < 0) (lo, hi) = (lo + length, hi + length);
            return new TrimResult([path.Sub(hi, lo + length)], path.Sub(lo, hi));
        }

        if (cuts.Count == 0) return null;
        var a = cuts.LastOrDefault(s => s < sc, 0);
        var b = cuts.FirstOrDefault(s => s > sc, length);
        var pieces = new List<Entity>();
        if (a > 0) pieces.Add(path.Sub(0, a));
        if (b < length) pieces.Add(path.Sub(b, length));
        return new TrimResult(pieces, path.Sub(a, b));
    }

    /// <summary>
    /// target の pick に近い側の端を、延長線上で最初に当たる edges まで伸ばす。
    /// 線・円弧・開いたポリラインが対象。当たるものが無ければ null。
    /// </summary>
    public static Entity? Extend(Entity target, Vec2 pick, IEnumerable<Entity> edges)
    {
        var edgeCurves = edges.Where(e => e != target).SelectMany(e => e.Curves).ToList();
        switch (target)
        {
            case LineEntity line:
            {
                var atEnd = pick.DistanceTo(line.End) <= pick.DistanceTo(line.Start);
                if (ExtendSegment(line.Start, line.End, atEnd, edgeCurves) is not Vec2 p) return null;
                return EditUtil.CopyStyle(line, atEnd ? new LineEntity(line.Layer, line.Start, p) : new LineEntity(line.Layer, p, line.End));
            }
            case ArcEntity arc:
                return ExtendArc(arc, pick, edgeCurves);
            case PolylineEntity pl when !pl.IsClosed && pl.Points.Count >= 2:
            {
                var pts = pl.Points;
                var atEnd = pick.DistanceTo(pts[^1]) <= pick.DistanceTo(pts[0]);
                var p = atEnd ? ExtendSegment(pts[^2], pts[^1], true, edgeCurves) : ExtendSegment(pts[0], pts[1], false, edgeCurves);
                if (p is not Vec2 np) return null;
                var newPts = pts.ToList();
                newPts[atEnd ? newPts.Count - 1 : 0] = np;
                return EditUtil.CopyStyle(pl, new PolylineEntity(pl.Layer, newPts) { Filled = pl.Filled });
            }
            default:
                return null;
        }
    }

    /// <summary>線分 a-b を伸ばす向きの先 (終点側は t &gt; 1、始点側は t &lt; 0) で一番近い交点。</summary>
    private static Vec2? ExtendSegment(Vec2 a, Vec2 b, bool atEnd, List<Curve> edges)
    {
        var seg = new SegmentCurve(a, b);
        var eps = Eps * Math.Max(1, a.DistanceTo(b));
        var best = double.MaxValue;
        Vec2? result = null;
        foreach (var edge in edges)
            foreach (var p in CurveIntersect.Points(seg, edge, extendA: true))
            {
                var t = (b - a).Dot(p - a) / (b - a).LengthSquared;
                var beyond = atEnd ? t - 1 : -t;
                if (beyond > eps && beyond < best) (best, result) = (beyond, p);
            }
        return result;
    }

    private static ArcEntity? ExtendArc(ArcEntity arc, Vec2 pick, List<Curve> edges)
    {
        var atEnd = pick.DistanceTo(arc.EndPoint) <= pick.DistanceTo(arc.StartPoint);
        var circle = new CircleCurve(arc.Center, arc.Radius);
        var room = GeoMath.TwoPi - arc.Sweep; // 伸ばせるのは一周の残りまで
        var eps = Eps / Math.Max(arc.Radius, 1e-9);
        var best = double.MaxValue;
        foreach (var edge in edges)
            foreach (var p in CurveIntersect.Points(circle, edge, extendA: true))
            {
                var angle = (p - arc.Center).Angle;
                var delta = atEnd ? GeoMath.NormalizeAngle(angle - arc.EndAngle) : GeoMath.NormalizeAngle(arc.StartAngle - angle);
                if (delta > eps && delta < room - eps && delta < best) best = delta;
            }
        if (best == double.MaxValue) return null;
        var result = atEnd
            ? new ArcEntity(arc.Layer, arc.Center, arc.Radius, arc.StartAngle, arc.EndAngle + best)
            : new ArcEntity(arc.Layer, arc.Center, arc.Radius, arc.StartAngle - best, arc.EndAngle);
        return EditUtil.CopyStyle(arc, result);
    }

    private static List<double> Dedupe(List<double> values, double eps)
    {
        values.Sort();
        var result = new List<double>();
        foreach (var v in values)
            if (result.Count == 0 || v - result[^1] > eps) result.Add(v);
        return result;
    }
}
