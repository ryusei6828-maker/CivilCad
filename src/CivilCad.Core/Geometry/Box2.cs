namespace CivilCad.Core.Geometry;

/// <summary>軸平行な矩形 (バウンディングボックス)。</summary>
public readonly record struct Box2(Vec2 Min, Vec2 Max)
{
    public static readonly Box2 Empty = new(
        new(double.PositiveInfinity, double.PositiveInfinity),
        new(double.NegativeInfinity, double.NegativeInfinity));

    public bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y;
    public double Width => Max.X - Min.X;
    public double Height => Max.Y - Min.Y;
    public Vec2 Center => (Min + Max) / 2;

    public static Box2 FromPoints(Vec2 a, Vec2 b) => new(
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)),
        new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));

    public static Box2 FromPoints(IEnumerable<Vec2> points)
    {
        var box = Empty;
        foreach (var p in points) box = box.Include(p);
        return box;
    }

    public Box2 Include(Vec2 p) => Union(new Box2(p, p));

    public Box2 Union(Box2 o)
    {
        if (IsEmpty) return o;
        if (o.IsEmpty) return this;
        return new(
            new(Math.Min(Min.X, o.Min.X), Math.Min(Min.Y, o.Min.Y)),
            new(Math.Max(Max.X, o.Max.X), Math.Max(Max.Y, o.Max.Y)));
    }

    public Box2 Inflate(double d) => IsEmpty ? this : new(Min - new Vec2(d, d), Max + new Vec2(d, d));

    public bool Contains(Vec2 p) => p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y;

    public bool Contains(Box2 o) =>
        !o.IsEmpty && o.Min.X >= Min.X && o.Min.Y >= Min.Y && o.Max.X <= Max.X && o.Max.Y <= Max.Y;

    public bool Intersects(Box2 o) =>
        !IsEmpty && !o.IsEmpty &&
        !(o.Min.X > Max.X || o.Max.X < Min.X || o.Min.Y > Max.Y || o.Max.Y < Min.Y);
}
