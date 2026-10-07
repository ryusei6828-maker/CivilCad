using CivilCad.Core.Geometry;

namespace CivilCad.Core.Model;

/// <summary>記号を作る図形 (記号の大きさ 1 の座標。原点が配置点、+y が記号の上 = 北)。</summary>
public abstract record SymbolShape;

/// <summary>折れ線・多角形。Filled なら塗りつぶす。</summary>
public sealed record SymbolPoly(IReadOnlyList<Vec2> Points, bool Closed, bool Filled = false) : SymbolShape;

public sealed record SymbolCircle(Vec2 Center, double Radius, bool Filled = false) : SymbolShape;

/// <summary>文字 (中心が Position)。</summary>
public sealed record SymbolText(Vec2 Position, string Text, double Height) : SymbolShape;

/// <summary>プリセット (記号) の定義。</summary>
public sealed record SymbolDefinition(string Kind, string Name, IReadOnlyList<SymbolShape> Shapes, double DefaultSizeMm);

/// <summary>プリセットの一覧。</summary>
public static class SymbolLibrary
{
    public const string NorthArrow = "NorthArrow";
    public const string NorthArrowSimple = "NorthArrowSimple";
    public const string NorthArrowCross = "NorthArrowCross";

    public static IReadOnlyList<SymbolDefinition> All { get; } =
    [
        // 円 + 半分塗りの矢 + N
        new(NorthArrow, "方位記号 (矢印・円)",
        [
            new SymbolCircle(Vec2.Zero, 0.3),
            new SymbolPoly([new(0, 0.42), new(-0.13, -0.32), new(0, -0.18)], true, Filled: true),
            new SymbolPoly([new(0, 0.42), new(0.13, -0.32), new(0, -0.18)], true),
            new SymbolText(new Vec2(0, 0.6), "N", 0.2),
        ], 20),

        // 線 + 塗りの矢じり + N
        new(NorthArrowSimple, "方位記号 (シンプル)",
        [
            new SymbolPoly([new(0, -0.45), new(0, 0.3)], false),
            new SymbolPoly([new(0, 0.45), new(-0.09, 0.22), new(0.09, 0.22)], true, Filled: true),
            new SymbolText(new Vec2(0, 0.62), "N", 0.2),
        ], 15),

        // 十字 + 北側を塗った菱形 + N
        new(NorthArrowCross, "方位記号 (十字)",
        [
            new SymbolPoly([new(-0.4, 0), new(0.4, 0)], false),
            new SymbolPoly([new(0, -0.4), new(0, 0.4)], false),
            new SymbolPoly([new(0, 0.42), new(-0.08, 0), new(0.08, 0)], true, Filled: true),
            new SymbolPoly([new(0, -0.42), new(-0.08, 0), new(0.08, 0)], true),
            new SymbolText(new Vec2(0, 0.6), "N", 0.2),
        ], 20),
    ];

    public static SymbolDefinition Get(string kind) => All.FirstOrDefault(d => d.Kind == kind) ?? All[0];
}

/// <summary>
/// プリセット (方位記号などの記号)。1 つの図形として選択・移動・回転・削除できる。
/// 大きさは用紙上の mm (SizeMm) で決まり、図面の縮尺が変わっても用紙上の大きさは変わらない
/// (Size = 図面上の大きさ m は、縮尺を変えたときに CadDocument 側で合わせ直す)。
/// Rotation は記号の上 (北) の向き: 0 = 図面の上 (+y)。
/// </summary>
public sealed class SymbolEntity(Layer layer, string kind, Vec2 position, double sizeMm, double size, double rotation = 0) : Entity(layer)
{
    public string Kind { get; } = kind;
    public Vec2 Position { get; set; } = position;

    /// <summary>用紙上の大きさ (mm)。</summary>
    public double SizeMm { get; set; } = sizeMm;

    /// <summary>図面上の大きさ (m) = SizeMm を図面の縮尺で換算したもの。</summary>
    public double Size { get; set; } = size;

    public double Rotation { get; set; } = rotation;

    public SymbolDefinition Definition => SymbolLibrary.Get(Kind);

    /// <summary>記号の座標 → 図面の座標。</summary>
    public Vec2 Map(Vec2 local) => Position + (local * Size).Rotate(Rotation);

    /// <summary>図面上の図形として展開する (線・塗り・円・文字)。保存 (DXF / SFC) や分解に使う。</summary>
    public IEnumerable<Entity> Explode()
    {
        foreach (var s in Definition.Shapes)
        {
            Entity e = s switch
            {
                SymbolPoly { Filled: true } p => new HatchEntity(Layer, [p.Points.Select(Map)], [], solid: true),
                SymbolPoly p => new PolylineEntity(Layer, p.Points.Select(Map), p.Closed),
                SymbolCircle c => new CircleEntity(Layer, Map(c.Center), c.Radius * Size),
                SymbolText t => new TextEntity(Layer, Map(t.Position), t.Text, t.Height * Size, Rotation) { HAlign = TextHAlign.Center, VAlign = TextVAlign.Middle },
                _ => throw new NotSupportedException(),
            };
            yield return CopyPropertiesTo(e);
        }
    }

    private IEnumerable<Vec2> OutlinePoints() => Definition.Shapes.SelectMany(s => s switch
    {
        SymbolPoly p => p.Points.Select(Map),
        SymbolCircle c => Enumerable.Range(0, 4).Select(k => Map(c.Center + Vec2.FromAngle(k * Math.PI / 2) * c.Radius)),
        SymbolText t => new[] { new Vec2(-0.5, -0.5), new Vec2(0.5, -0.5), new Vec2(0.5, 0.5), new Vec2(-0.5, 0.5) }
            .Select(d => Map(t.Position + d * t.Height)),
        _ => [],
    });

    public override Box2 Bounds => Box2.FromPoints(OutlinePoints());

    /// <summary>記号の範囲 (の中) をクリックすれば選べるよう、範囲内は距離 0。</summary>
    public override double DistanceTo(Vec2 p)
    {
        var b = Bounds;
        var dx = Math.Max(0, Math.Max(b.Min.X - p.X, p.X - b.Max.X));
        var dy = Math.Max(0, Math.Max(b.Min.Y - p.Y, p.Y - b.Max.Y));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public override void Translate(Vec2 delta) => Position += delta;

    public override void Rotate(Vec2 origin, double rad)
    {
        Position = Position.RotateAround(origin, rad);
        Rotation += rad;
    }

    public override Entity Clone() => CopyPropertiesTo(new SymbolEntity(Layer, Kind, Position, SizeMm, Size, Rotation));

    public override IEnumerable<SnapPoint> SnapPoints => [new(Position, SnapKind.Node)];
}
