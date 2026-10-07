using System.Text.Json.Serialization;
using CivilCad.Core.Geometry;

namespace CivilCad.Core.Model;

/// <summary>
/// 記号を作る図形 (記号の大きさ 1 の座標。原点が配置点、+y が記号の上 = 北)。
/// Color が null なら記号 (図形) の色、あれば その色で描く (登録した図形の色を残すため)。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SymbolPoly), "poly")]
[JsonDerivedType(typeof(SymbolCircle), "circle")]
[JsonDerivedType(typeof(SymbolArc), "arc")]
[JsonDerivedType(typeof(SymbolText), "text")]
[JsonDerivedType(typeof(SymbolHatch), "hatch")]
public abstract record SymbolShape
{
    public RgbColor? Color { get; init; }
}

/// <summary>折れ線・多角形。Filled なら塗りつぶす。</summary>
public sealed record SymbolPoly(IReadOnlyList<Vec2> Points, bool Closed, bool Filled = false) : SymbolShape;

public sealed record SymbolCircle(Vec2 Center, double Radius, bool Filled = false) : SymbolShape;

/// <summary>円弧 (角度はラジアン、反時計回り)。</summary>
public sealed record SymbolArc(Vec2 Center, double Radius, double StartAngle, double EndAngle) : SymbolShape;

/// <summary>文字 (既定は中心が Position)。Rotation は記号に対する向き (ラジアン)。</summary>
public sealed record SymbolText(Vec2 Position, string Text, double Height, double Rotation = 0,
    TextHAlign HAlign = TextHAlign.Center, TextVAlign VAlign = TextVAlign.Middle, double WidthFactor = 1) : SymbolShape;

/// <summary>ハッチング・塗りつぶし (境界と線のパターンは記号の座標)。</summary>
public sealed record SymbolHatch(IReadOnlyList<IReadOnlyList<Vec2>> Loops, IReadOnlyList<HatchPattern> Patterns, bool Solid,
    double TransparencyPercent = 0) : SymbolShape;

/// <summary>プリセット (記号) の定義。IsUser = 図形を選んで登録したもの。</summary>
public sealed record SymbolDefinition(string Kind, string Name, IReadOnlyList<SymbolShape> Shapes, double DefaultSizeMm, bool IsUser = false);

/// <summary>プリセットの一覧 (最初から入っているもの + 登録したもの)。</summary>
public static class SymbolLibrary
{
    public const string NorthArrow = "NorthArrow";
    public const string NorthArrowSimple = "NorthArrowSimple";
    public const string NorthArrowCross = "NorthArrowCross";

    public static IReadOnlyList<SymbolDefinition> BuiltIn { get; } =
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

    private static readonly List<SymbolDefinition> _user = [];

    /// <summary>削除した登録プリセット (図面に置いてあるものは、削除後もそのまま表示できるように残す)。</summary>
    private static readonly Dictionary<string, SymbolDefinition> _removed = [];

    /// <summary>登録したプリセット。</summary>
    public static IReadOnlyList<SymbolDefinition> User
    {
        get { lock (_user) return [.. _user]; }
    }

    public static IReadOnlyList<SymbolDefinition> All => [.. BuiltIn, .. User];

    /// <summary>登録プリセットの一覧が変わった。</summary>
    public static event EventHandler? UserChanged;

    public static SymbolDefinition Get(string kind)
    {
        if (BuiltIn.FirstOrDefault(d => d.Kind == kind) is { } b) return b;
        lock (_user)
        {
            return _user.FirstOrDefault(d => d.Kind == kind) ?? _removed.GetValueOrDefault(kind) ?? BuiltIn[0];
        }
    }

    /// <summary>登録プリセットを入れ替える (起動時に保存ファイルから読むとき)。</summary>
    public static void SetUser(IEnumerable<SymbolDefinition> defs)
    {
        lock (_user)
        {
            _user.Clear();
            _user.AddRange(defs.Where(d => d.Shapes.Count > 0).Select(d => d with { IsUser = true }));
        }
        UserChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void AddUser(SymbolDefinition def)
    {
        lock (_user) _user.Add(def with { IsUser = true });
        UserChanged?.Invoke(null, EventArgs.Empty);
    }

    public static bool RemoveUser(string kind)
    {
        lock (_user)
        {
            var def = _user.FirstOrDefault(d => d.Kind == kind);
            if (def == null) return false;
            _user.Remove(def);
            _removed[kind] = def;
        }
        UserChanged?.Invoke(null, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// 図形からプリセットを作る。basePoint が配置点 (記号の原点) になる。
    /// 大きさ (DefaultSizeMm) は図形全体の幅・高さの大きい方の用紙上の mm で、
    /// その大きさで置くと登録した図形と同じ大きさになる。画像など記号にできない図形は skipped に数える。
    /// </summary>
    public static SymbolDefinition FromEntities(string name, IReadOnlyList<Entity> entities, Vec2 basePoint, double metersPerPaperMm, out int skipped)
    {
        var box = entities.Where(e => e is not ImageEntity).Aggregate(Box2.Empty, (acc, e) => acc.Union(e.Bounds));
        var unit = box.IsEmpty ? 1 : Math.Max(box.Width, box.Height);
        if (unit <= 0) unit = 1;
        Vec2 L(Vec2 p) => (p - basePoint) / unit;

        var shapes = new List<SymbolShape>();
        skipped = 0;
        foreach (var e in entities)
        {
            var color = e.Color;
            var converted = Convert(e).ToList();
            if (converted.Count == 0) skipped++;
            shapes.AddRange(converted.Select(s => s.Color == null && color != null ? s with { Color = color } : s));
        }
        return new SymbolDefinition($"User:{Guid.NewGuid():N}", name, shapes, unit / metersPerPaperMm, IsUser: true);

        IEnumerable<SymbolShape> Convert(Entity e) => e switch
        {
            LineEntity l => [new SymbolPoly([L(l.Start), L(l.End)], false)],
            PolylineEntity pl when pl.Points.Count > 1 => [new SymbolPoly([.. pl.Points.Select(L)], pl.IsClosed, pl.Filled)],
            CircleEntity c => [new SymbolCircle(L(c.Center), c.Radius / unit)],
            ArcEntity a => [new SymbolArc(L(a.Center), a.Radius / unit, a.StartAngle, a.EndAngle)],
            TextEntity t => [new SymbolText(L(t.Position), t.Text, t.Height / unit, t.Rotation, t.HAlign, t.VAlign,
                t.FitWidth is double fit && t.ApproxWidth > 0 && t.WidthFactor > 0 ? t.WidthFactor * fit / (t.ApproxWidth) : t.WidthFactor)],
            HatchEntity h when h.Loops.Count > 0 => [new SymbolHatch(
                [.. h.Loops.Select(loop => (IReadOnlyList<Vec2>)[.. loop.Select(L)])],
                [.. h.Patterns.Select(p => p with { Spacing = p.Spacing / unit, Origin = L(p.Origin) })],
                h.Solid, h.TransparencyPercent)],
            // 記号の中の色は、記号ごとの色を引き継ぐ
            SymbolEntity s => s.Explode().SelectMany(x => Convert(x).Select(c => c.Color == null && x.Color != null ? c with { Color = x.Color } : c)),
            _ => [],
        };
    }
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

    /// <summary>図面上の図形として展開する (線・塗り・円・文字)。表示・保存 (DXF / SFC)・分解に使う。</summary>
    public IEnumerable<Entity> Explode()
    {
        foreach (var s in Definition.Shapes)
        {
            Entity e = s switch
            {
                SymbolPoly { Filled: true } p => new HatchEntity(Layer, [p.Points.Select(Map)], [], solid: true),
                SymbolPoly p => new PolylineEntity(Layer, p.Points.Select(Map), p.Closed),
                SymbolCircle c => new CircleEntity(Layer, Map(c.Center), c.Radius * Size),
                SymbolArc a => new ArcEntity(Layer, Map(a.Center), a.Radius * Size, a.StartAngle + Rotation, a.EndAngle + Rotation),
                SymbolText t => new TextEntity(Layer, Map(t.Position), t.Text, t.Height * Size, t.Rotation + Rotation)
                {
                    HAlign = t.HAlign,
                    VAlign = t.VAlign,
                    WidthFactor = t.WidthFactor,
                },
                SymbolHatch h => new HatchEntity(Layer, h.Loops.Select(l => l.Select(Map)),
                    h.Patterns.Select(p => new HatchPattern(p.Angle + Rotation, p.Spacing * Size, Map(p.Origin))), h.Solid)
                {
                    TransparencyPercent = h.TransparencyPercent,
                },
                _ => throw new NotSupportedException(),
            };
            CopyPropertiesTo(e);
            if (s.Color is RgbColor sc) e.Color = sc;
            yield return e;
        }
    }

    private IEnumerable<Vec2> OutlinePoints() => Definition.Shapes.SelectMany(s => s switch
    {
        SymbolPoly p => p.Points.Select(Map),
        SymbolCircle c => Enumerable.Range(0, 4).Select(k => Map(c.Center + Vec2.FromAngle(k * Math.PI / 2) * c.Radius)),
        SymbolArc a => Enumerable.Range(0, 4).Select(k => Map(a.Center + Vec2.FromAngle(k * Math.PI / 2) * a.Radius)),
        SymbolText t => TextCorners(t).Select(Map),
        SymbolHatch h => h.Loops.SelectMany(l => l).Select(Map),
        _ => [],
    });

    /// <summary>文字の範囲 (記号の座標、おおよそ)。</summary>
    private static IEnumerable<Vec2> TextCorners(SymbolText t)
    {
        var w = t.Text.Sum(c => c < 0x80 || c is >= '｡' and <= 'ﾟ' ? 0.5 : 1.0) * t.Height * t.WidthFactor;
        var x0 = t.HAlign switch { TextHAlign.Center => -w / 2, TextHAlign.Right => -w, _ => 0 };
        var y0 = t.VAlign switch { TextVAlign.Middle => -t.Height / 2, TextVAlign.Top => -t.Height, _ => 0 };
        if (t.HAlign == TextHAlign.Center && t.VAlign == TextVAlign.Middle) { x0 = Math.Min(x0, -t.Height / 2); w = Math.Max(w, t.Height); }
        return new[] { new Vec2(x0, y0), new Vec2(x0 + w, y0), new Vec2(x0 + w, y0 + t.Height), new Vec2(x0, y0 + t.Height) }
            .Select(d => t.Position + d.Rotate(t.Rotation));
    }

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
