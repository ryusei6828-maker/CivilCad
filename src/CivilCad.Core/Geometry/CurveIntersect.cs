namespace CivilCad.Core.Geometry;

/// <summary>
/// トリム・延長用の交点計算。<see cref="SnapGeometry.Intersections"/> と違い、
/// 一方の曲線を延長 (線分 → 無限直線、円弧 → 全円) して交点を求められる。
/// </summary>
public static class CurveIntersect
{
    private const double ParamEps = 1e-9;

    /// <summary>無限直線 p1-p2 と q1-q2 の交点のパラメータ (p1 + (p2-p1)T = q1 + (q2-q1)U)。平行なら null。</summary>
    public static (double T, double U)? LineLine(Vec2 p1, Vec2 p2, Vec2 q1, Vec2 q2)
    {
        var r = p2 - p1;
        var s = q2 - q1;
        var denom = r.Cross(s);
        if (Math.Abs(denom) < 1e-12 * Math.Max(1, r.Length * s.Length)) return null;
        var qp = q1 - p1;
        return (qp.Cross(s) / denom, qp.Cross(r) / denom);
    }

    /// <summary>無限直線 p1-p2 と円の交点のパラメータ t (p1 + (p2-p1)t)。接する場合は 1 つ。</summary>
    public static List<double> LineCircle(Vec2 p1, Vec2 p2, Vec2 center, double radius)
    {
        var d = p2 - p1;
        var a = d.LengthSquared;
        if (a == 0) return [];
        var f = p1 - center;
        var b = 2 * f.Dot(d);
        var c = f.LengthSquared - radius * radius;
        var disc = b * b - 4 * a * c;
        var tangentEps = 1e-12 * Math.Max(1, b * b);
        if (disc < -tangentEps) return [];
        if (disc <= tangentEps) return [-b / (2 * a)];
        var sq = Math.Sqrt(disc);
        return [(-b - sq) / (2 * a), (-b + sq) / (2 * a)];
    }

    public static List<Vec2> CircleCircle(Vec2 c1, double r1, Vec2 c2, double r2)
    {
        var d = c1.DistanceTo(c2);
        var eps = 1e-9 * Math.Max(1, r1 + r2);
        if (d < 1e-12 || d > r1 + r2 + eps || d < Math.Abs(r1 - r2) - eps) return [];
        var a = (r1 * r1 - r2 * r2 + d * d) / (2 * d);
        var h2 = r1 * r1 - a * a;
        var dir = (c2 - c1) / d;
        var mid = c1 + dir * a;
        if (h2 <= eps * eps) return [mid];
        var h = Math.Sqrt(h2);
        return [mid + dir.LeftNormal * h, mid - dir.LeftNormal * h];
    }

    /// <summary>a と b の交点。extendA なら a を無限直線 / 全円として扱う (b は常に実際の範囲)。</summary>
    public static List<Vec2> Points(Curve a, Curve b, bool extendA = false)
    {
        var result = new List<Vec2>();
        switch (a, b)
        {
            case (SegmentCurve sa, SegmentCurve sb):
                if (LineLine(sa.A, sa.B, sb.A, sb.B) is var (ta, ub) && InUnit(ub) && (extendA || InUnit(ta)))
                    result.Add(sa.A + (sa.B - sa.A) * ta);
                break;
            case (SegmentCurve sa, CircleCurve cb):
                foreach (var t in LineCircle(sa.A, sa.B, cb.Center, cb.Radius))
                {
                    var p = sa.A + (sa.B - sa.A) * t;
                    if ((extendA || InUnit(t)) && OnArc(cb, p)) result.Add(p);
                }
                break;
            case (CircleCurve ca, SegmentCurve sb):
                foreach (var u in LineCircle(sb.A, sb.B, ca.Center, ca.Radius))
                {
                    var p = sb.A + (sb.B - sb.A) * u;
                    if (InUnit(u) && (extendA || OnArc(ca, p))) result.Add(p);
                }
                break;
            case (CircleCurve ca, CircleCurve cb):
                foreach (var p in CircleCircle(ca.Center, ca.Radius, cb.Center, cb.Radius))
                    if (OnArc(cb, p) && (extendA || OnArc(ca, p))) result.Add(p);
                break;
        }
        return result;
    }

    /// <summary>円弧の上にあるか (端点の角度は丸め誤差を許す)。</summary>
    public static bool OnArc(CircleCurve c, Vec2 p)
    {
        if (c.IsFull) return true;
        var eps = 1e-9 / Math.Max(c.Radius, 1e-9);
        var offset = GeoMath.NormalizeAngle((p - c.Center).Angle - c.Start);
        return offset <= GeoMath.SweepAngle(c.Start, c.End) + eps || offset >= GeoMath.TwoPi - eps;
    }

    private static bool InUnit(double t) => t is >= -ParamEps and <= 1 + ParamEps;
}
