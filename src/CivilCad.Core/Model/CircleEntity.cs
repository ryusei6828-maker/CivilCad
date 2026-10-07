using CivilCad.Core.Geometry;

namespace CivilCad.Core.Model;

public sealed class CircleEntity(Layer layer, Vec2 center, double radius) : Entity(layer)
{
    public Vec2 Center { get; set; } = center;
    public double Radius { get; set; } = radius;

    public override Box2 Bounds => new(Center - new Vec2(Radius, Radius), Center + new Vec2(Radius, Radius));
    public override double DistanceTo(Vec2 p) => Math.Abs(p.DistanceTo(Center) - Radius);

    public override bool IntersectsBox(Box2 box)
    {
        if (!Bounds.Intersects(box)) return false;
        // 円周が矩形にかかる: 中心から矩形への最近距離 ≤ R ≤ 最遠距離
        var nearest = new Vec2(Math.Clamp(Center.X, box.Min.X, box.Max.X), Math.Clamp(Center.Y, box.Min.Y, box.Max.Y));
        var fx = Math.Max(Math.Abs(Center.X - box.Min.X), Math.Abs(Center.X - box.Max.X));
        var fy = Math.Max(Math.Abs(Center.Y - box.Min.Y), Math.Abs(Center.Y - box.Max.Y));
        return Center.DistanceTo(nearest) <= Radius && Math.Sqrt(fx * fx + fy * fy) >= Radius;
    }

    public override void Translate(Vec2 delta) => Center += delta;
    public override void Rotate(Vec2 origin, double rad) => Center = Center.RotateAround(origin, rad);

    public override void Scale(Vec2 origin, double k)
    {
        Center = ScalePoint(Center, origin, k);
        Radius *= k;
    }

    public override IEnumerable<Curve> Curves => [new CircleCurve(Center, Radius)];

    public override Entity Clone() => CopyPropertiesTo(new CircleEntity(Layer, Center, Radius));

    public override IEnumerable<SnapPoint> SnapPoints =>
    [
        new(Center, SnapKind.Center),
        new(Center + new Vec2(Radius, 0), SnapKind.Quadrant),
        new(Center + new Vec2(0, Radius), SnapKind.Quadrant),
        new(Center + new Vec2(-Radius, 0), SnapKind.Quadrant),
        new(Center + new Vec2(0, -Radius), SnapKind.Quadrant),
    ];
}
