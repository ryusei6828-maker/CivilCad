using CivilCad.Core.Geometry;

namespace CivilCad.Core.Model;

/// <summary>円弧。StartAngle から EndAngle へ反時計回り (ラジアン)。</summary>
public sealed class ArcEntity(Layer layer, Vec2 center, double radius, double startAngle, double endAngle) : Entity(layer)
{
    public Vec2 Center { get; set; } = center;
    public double Radius { get; set; } = radius;
    public double StartAngle { get; set; } = startAngle;
    public double EndAngle { get; set; } = endAngle;

    public double Sweep => GeoMath.SweepAngle(StartAngle, EndAngle);
    public double Length => Radius * Sweep;
    public Vec2 StartPoint => Center + Vec2.FromAngle(StartAngle) * Radius;
    public Vec2 EndPoint => Center + Vec2.FromAngle(EndAngle) * Radius;
    public Vec2 MidPoint => Center + Vec2.FromAngle(StartAngle + Sweep / 2) * Radius;

    /// <summary>3点 (始点・通過点・終点) から円弧を作る。一直線上なら null。</summary>
    public static ArcEntity? From3Points(Layer layer, Vec2 p1, Vec2 p2, Vec2 p3)
    {
        if (GeoMath.CircleFrom3Points(p1, p2, p3) is not var (c, r)) return null;
        var a1 = (p1 - c).Angle;
        var a2 = (p2 - c).Angle;
        var a3 = (p3 - c).Angle;
        return GeoMath.IsAngleInSweep(a2, a1, a3)
            ? new ArcEntity(layer, c, r, a1, a3)
            : new ArcEntity(layer, c, r, a3, a1);
    }

    /// <summary>
    /// 始点・終点・半径から円弧を作る。候補は 4 通り (中心が左右 × 短い側・長い側) なので、
    /// pick に一番近いものを選ぶ。2 点間が直径より長ければ null。
    /// </summary>
    public static ArcEntity? FromChordAndRadius(Layer layer, Vec2 start, Vec2 end, double radius, Vec2 pick)
    {
        var chord = end - start;
        var d = chord.Length;
        if (d < 1e-9 || radius <= 0 || d > 2 * radius + 1e-9) return null;
        var mid = (start + end) / 2;
        var h = Math.Sqrt(Math.Max(0, radius * radius - d * d / 4));
        var n = chord.Normalized().LeftNormal;

        ArcEntity? best = null;
        var bestD = double.PositiveInfinity;
        foreach (var c in new[] { mid + n * h, mid - n * h })
        {
            var a1 = (start - c).Angle;
            var a2 = (end - c).Angle;
            foreach (var arc in new[] { new ArcEntity(layer, c, radius, a1, a2), new ArcEntity(layer, c, radius, a2, a1) })
            {
                var dist = arc.DistanceTo(pick);
                if (dist < bestD) (best, bestD) = (arc, dist);
            }
        }
        return best;
    }

    public override Box2 Bounds
    {
        get
        {
            var box = Box2.FromPoints(StartPoint, EndPoint);
            for (var q = 0; q < 4; q++)
            {
                var a = q * Math.PI / 2;
                if (GeoMath.IsAngleInSweep(a, StartAngle, EndAngle))
                    box = box.Include(Center + Vec2.FromAngle(a) * Radius);
            }
            return box;
        }
    }

    public override double DistanceTo(Vec2 p)
    {
        if (GeoMath.IsAngleInSweep((p - Center).Angle, StartAngle, EndAngle))
            return Math.Abs(p.DistanceTo(Center) - Radius);
        return Math.Min(p.DistanceTo(StartPoint), p.DistanceTo(EndPoint));
    }

    public override void Translate(Vec2 delta) => Center += delta;
    public override void Rotate(Vec2 origin, double rad)
    {
        Center = Center.RotateAround(origin, rad);
        StartAngle += rad;
        EndAngle += rad;
    }

    public override IEnumerable<Curve> Curves => [new CircleCurve(Center, Radius, StartAngle, EndAngle, IsFull: false)];

    public override Entity Clone() => CopyPropertiesTo(new ArcEntity(Layer, Center, Radius, StartAngle, EndAngle));

    public override IEnumerable<SnapPoint> SnapPoints =>
    [
        new(Center, SnapKind.Center),
        new(StartPoint, SnapKind.Endpoint),
        new(EndPoint, SnapKind.Endpoint),
        new(MidPoint, SnapKind.Midpoint),
    ];
}
