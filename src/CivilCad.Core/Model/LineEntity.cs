using CivilCad.Core.Geometry;

namespace CivilCad.Core.Model;

public sealed class LineEntity(Layer layer, Vec2 start, Vec2 end) : Entity(layer)
{
    public Vec2 Start { get; set; } = start;
    public Vec2 End { get; set; } = end;

    public double Length => Start.DistanceTo(End);
    public override Box2 Bounds => Box2.FromPoints(Start, End);
    public override double DistanceTo(Vec2 p) => GeoMath.DistanceToSegment(p, Start, End);
    public override bool IntersectsBox(Box2 box) => GeoMath.SegmentIntersectsBox(Start, End, box);

    public override void Translate(Vec2 delta)
    {
        Start += delta;
        End += delta;
    }

    public override void Rotate(Vec2 origin, double rad)
    {
        Start = Start.RotateAround(origin, rad);
        End = End.RotateAround(origin, rad);
    }

    public override void Scale(Vec2 origin, double k)
    {
        Start = ScalePoint(Start, origin, k);
        End = ScalePoint(End, origin, k);
    }

    public override IEnumerable<Curve> Curves => [new SegmentCurve(Start, End)];

    public override Entity Clone() => CopyPropertiesTo(new LineEntity(Layer, Start, End));

    public override IEnumerable<SnapPoint> SnapPoints =>
    [
        new(Start, SnapKind.Endpoint),
        new(End, SnapKind.Endpoint),
        new((Start + End) / 2, SnapKind.Midpoint),
    ];
}
