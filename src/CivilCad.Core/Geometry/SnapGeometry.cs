namespace CivilCad.Core.Geometry;

/// <summary>スナップ計算用の曲線 (線分か円・円弧)。</summary>
public abstract record Curve;

public sealed record SegmentCurve(Vec2 A, Vec2 B) : Curve;

/// <summary>円 (IsFull) または反時計回りの円弧 (Start → End, rad)。</summary>
public sealed record CircleCurve(Vec2 Center, double Radius, double Start = 0, double End = 0, bool IsFull = true) : Curve
{
    public bool Contains(double angle) => IsFull || GeoMath.IsAngleInSweep(angle, Start, End);
}

/// <summary>交点・垂線の足・接点・近接点の計算。</summary>
public static class SnapGeometry
{
    private const double Eps = 1e-9;

    public static IEnumerable<Vec2> Intersections(Curve a, Curve b) => (a, b) switch
    {
        (SegmentCurve s, SegmentCurve t) => SegmentSegment(s, t),
        (SegmentCurve s, CircleCurve c) => SegmentCircle(s, c),
        (CircleCurve c, SegmentCurve s) => SegmentCircle(s, c),
        (CircleCurve c, CircleCurve d) => CircleCircle(c, d),
        _ => [],
    };

    /// <summary>曲線上で p に最も近い点。</summary>
    public static Vec2 Nearest(Curve curve, Vec2 p)
    {
        switch (curve)
        {
            case SegmentCurve s:
                return s.A + (s.B - s.A) * GeoMath.ClosestParamOnSegment(p, s.A, s.B);
            case CircleCurve c:
                var v = p - c.Center;
                var angle = v.LengthSquared < Eps ? c.Start : v.Angle;
                if (c.Contains(angle)) return c.Center + Vec2.FromAngle(angle) * c.Radius;
                // 円弧の外側なら近い方の端点
                var s0 = c.Center + Vec2.FromAngle(c.Start) * c.Radius;
                var s1 = c.Center + Vec2.FromAngle(c.End) * c.Radius;
                return p.DistanceTo(s0) <= p.DistanceTo(s1) ? s0 : s1;
            default:
                return p;
        }
    }

    /// <summary>base から曲線に下ろした垂線の足 (曲線上にあるものだけ)。</summary>
    public static IEnumerable<Vec2> PerpendicularFeet(Curve curve, Vec2 basePoint)
    {
        switch (curve)
        {
            case SegmentCurve s:
                var ab = s.B - s.A;
                if (ab.LengthSquared < Eps) yield break;
                var t = (basePoint - s.A).Dot(ab) / ab.LengthSquared;
                if (t is >= -Eps and <= 1 + Eps) yield return s.A + ab * t;
                break;
            case CircleCurve c:
                // 円への垂線 = 中心を通る直線と円の交点 (近い側・遠い側)
                var v = basePoint - c.Center;
                if (v.LengthSquared < Eps) yield break;
                foreach (var a in new[] { v.Angle, v.Angle + Math.PI })
                    if (c.Contains(a)) yield return c.Center + Vec2.FromAngle(a) * c.Radius;
                break;
        }
    }

    /// <summary>base から円・円弧に引いた接線の接点。</summary>
    public static IEnumerable<Vec2> TangentPoints(Curve curve, Vec2 basePoint)
    {
        if (curve is not CircleCurve c) yield break;
        var v = basePoint - c.Center;
        var d = v.Length;
        if (d <= c.Radius + Eps) yield break; // 円の内側・円周上からは引けない
        var alpha = Math.Acos(c.Radius / d);
        foreach (var a in new[] { v.Angle + alpha, v.Angle - alpha })
            if (c.Contains(a)) yield return c.Center + Vec2.FromAngle(a) * c.Radius;
    }

    private static IEnumerable<Vec2> SegmentSegment(SegmentCurve s, SegmentCurve t)
    {
        var r = s.B - s.A;
        var q = t.B - t.A;
        var denom = r.Cross(q);
        if (Math.Abs(denom) < Eps) yield break; // 平行
        var w = t.A - s.A;
        var u = w.Cross(q) / denom;
        var v = w.Cross(r) / denom;
        if (u is >= -Eps and <= 1 + Eps && v is >= -Eps and <= 1 + Eps) yield return s.A + r * u;
    }

    private static IEnumerable<Vec2> SegmentCircle(SegmentCurve s, CircleCurve c)
    {
        var d = s.B - s.A;
        var f = s.A - c.Center;
        var a = d.LengthSquared;
        if (a < Eps) yield break;
        var b = 2 * f.Dot(d);
        var cc = f.LengthSquared - c.Radius * c.Radius;
        var disc = b * b - 4 * a * cc;
        if (disc < 0) yield break;
        var sq = Math.Sqrt(disc);
        var roots = disc < Eps ? new[] { -b / (2 * a) } : new[] { (-b - sq) / (2 * a), (-b + sq) / (2 * a) };
        foreach (var t in roots)
        {
            if (t is < -Eps or > 1 + Eps) continue;
            var p = s.A + d * t;
            if (c.Contains((p - c.Center).Angle)) yield return p;
        }
    }

    private static IEnumerable<Vec2> CircleCircle(CircleCurve c1, CircleCurve c2)
    {
        var dv = c2.Center - c1.Center;
        var d = dv.Length;
        if (d < Eps || d > c1.Radius + c2.Radius + Eps || d < Math.Abs(c1.Radius - c2.Radius) - Eps) yield break;
        var a = (c1.Radius * c1.Radius - c2.Radius * c2.Radius + d * d) / (2 * d);
        var h = Math.Sqrt(Math.Max(0, c1.Radius * c1.Radius - a * a));
        var mid = c1.Center + dv / d * a;
        var n = dv.LeftNormal / d * h;
        var pts = h < Eps ? new[] { mid } : new[] { mid + n, mid - n };
        foreach (var p in pts)
            if (c1.Contains((p - c1.Center).Angle) && c2.Contains((p - c2.Center).Angle)) yield return p;
    }
}
