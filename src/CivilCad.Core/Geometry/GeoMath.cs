namespace CivilCad.Core.Geometry;

public static class GeoMath
{
    public const double TwoPi = Math.PI * 2;

    /// <summary>角度を [0, 2π) に正規化。</summary>
    public static double NormalizeAngle(double a)
    {
        a %= TwoPi;
        if (a < 0) a += TwoPi;
        return a;
    }

    /// <summary>角度を (-π, π] に正規化。</summary>
    public static double NormalizeSignedAngle(double a)
    {
        a = NormalizeAngle(a);
        return a > Math.PI ? a - TwoPi : a;
    }

    /// <summary>start から反時計回りに end までの掃引角 (0 &lt; sweep ≤ 2π)。</summary>
    public static double SweepAngle(double start, double end)
    {
        var s = NormalizeAngle(end - start);
        return s == 0 ? TwoPi : s;
    }

    /// <summary>angle が start から反時計回りに end までの範囲にあるか。</summary>
    public static bool IsAngleInSweep(double angle, double start, double end) =>
        NormalizeAngle(angle - start) <= SweepAngle(start, end);

    public static double ClosestParamOnSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared;
        if (len2 == 0) return 0;
        return Math.Clamp((p - a).Dot(ab) / len2, 0, 1);
    }

    public static double DistanceToSegment(Vec2 p, Vec2 a, Vec2 b) =>
        p.DistanceTo(a + (b - a) * ClosestParamOnSegment(p, a, b));

    /// <summary>線分が矩形と交差(一部でも内部に入る)するか。</summary>
    public static bool SegmentIntersectsBox(Vec2 a, Vec2 b, Box2 box)
    {
        if (box.Contains(a) || box.Contains(b)) return true;
        if (!Box2.FromPoints(a, b).Intersects(box)) return false;
        var c0 = box.Min;
        var c1 = new Vec2(box.Max.X, box.Min.Y);
        var c2 = box.Max;
        var c3 = new Vec2(box.Min.X, box.Max.Y);
        return SegmentsIntersect(a, b, c0, c1) || SegmentsIntersect(a, b, c1, c2) ||
               SegmentsIntersect(a, b, c2, c3) || SegmentsIntersect(a, b, c3, c0);
    }

    public static bool SegmentsIntersect(Vec2 p1, Vec2 p2, Vec2 q1, Vec2 q2)
    {
        var d1 = (p2 - p1).Cross(q1 - p1);
        var d2 = (p2 - p1).Cross(q2 - p1);
        var d3 = (q2 - q1).Cross(p1 - q1);
        var d4 = (q2 - q1).Cross(p2 - q1);
        return d1 * d2 <= 0 && d3 * d4 <= 0;
    }

    /// <summary>3点を通る円。3点が一直線上なら null。</summary>
    public static (Vec2 Center, double Radius)? CircleFrom3Points(Vec2 a, Vec2 b, Vec2 c)
    {
        var d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
        if (Math.Abs(d) < 1e-12) return null;
        var a2 = a.LengthSquared;
        var b2 = b.LengthSquared;
        var c2 = c.LengthSquared;
        var center = new Vec2(
            (a2 * (b.Y - c.Y) + b2 * (c.Y - a.Y) + c2 * (a.Y - b.Y)) / d,
            (a2 * (c.X - b.X) + b2 * (a.X - c.X) + c2 * (b.X - a.X)) / d);
        return (center, center.DistanceTo(a));
    }
}
