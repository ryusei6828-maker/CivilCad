using CivilCad.Core.Geometry;

namespace CivilCad.Core.Model;

public sealed class PolylineEntity(Layer layer, IEnumerable<Vec2> points, bool isClosed = false) : Entity(layer)
{
    public List<Vec2> Points { get; } = [.. points];
    public bool IsClosed { get; set; } = isClosed;

    /// <summary>内部を色で塗りつぶす (SXF の塗りつぶし領域など)。</summary>
    public bool Filled { get; set; }

    public IEnumerable<(Vec2 A, Vec2 B)> Segments
    {
        get
        {
            for (var i = 0; i + 1 < Points.Count; i++) yield return (Points[i], Points[i + 1]);
            if (IsClosed && Points.Count > 2) yield return (Points[^1], Points[0]);
        }
    }

    public double Length => Segments.Sum(s => s.A.DistanceTo(s.B));

    /// <summary>閉じた図形の面積 (座標法)。</summary>
    public double Area
    {
        get
        {
            if (Points.Count < 3) return 0;
            double sum = 0;
            for (var i = 0; i < Points.Count; i++)
                sum += Points[i].Cross(Points[(i + 1) % Points.Count]);
            return Math.Abs(sum) / 2;
        }
    }

    public override Box2 Bounds => Box2.FromPoints(Points);

    public override double DistanceTo(Vec2 p)
    {
        if (Points.Count == 1) return p.DistanceTo(Points[0]);
        var min = double.PositiveInfinity;
        foreach (var (a, b) in Segments) min = Math.Min(min, GeoMath.DistanceToSegment(p, a, b));
        return min;
    }

    public override bool IntersectsBox(Box2 box) =>
        Segments.Any(s => GeoMath.SegmentIntersectsBox(s.A, s.B, box));

    public override void Translate(Vec2 delta)
    {
        for (var i = 0; i < Points.Count; i++) Points[i] += delta;
    }

    public override void Rotate(Vec2 origin, double rad)
    {
        for (var i = 0; i < Points.Count; i++) Points[i] = Points[i].RotateAround(origin, rad);
    }

    public override IEnumerable<Curve> Curves => Segments.Select(s => (Curve)new SegmentCurve(s.A, s.B));

    public override Entity Clone() => CopyPropertiesTo(new PolylineEntity(Layer, Points, IsClosed) { Filled = Filled });

    public override IEnumerable<SnapPoint> SnapPoints =>
        Points.Select(p => new SnapPoint(p, SnapKind.Endpoint))
            .Concat(Segments.Select(s => new SnapPoint((s.A + s.B) / 2, SnapKind.Midpoint)));
}
