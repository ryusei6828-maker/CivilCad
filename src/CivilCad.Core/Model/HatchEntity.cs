using CivilCad.Core.Geometry;

namespace CivilCad.Core.Model;

/// <summary>
/// ハッチングの線の並び。Angle の向きの平行線を、Origin を通る線から Spacing おきに引く。
/// </summary>
/// <param name="Angle">線の向き (rad, 東 = 0, 反時計回り)。</param>
/// <param name="Spacing">線の間隔 (m)。</param>
/// <param name="Origin">基準の線が通る点。</param>
public sealed record HatchPattern(double Angle, double Spacing, Vec2 Origin);

/// <summary>
/// ハッチング。境界 (閉じた多角形の組) の内側に平行線を引く。境界は偶奇規則で、
/// 内側にある境界は中抜き (島) になる。ハッチ線は 1 つの図形としてまとめて選択・移動・削除できる。
/// Solid なら線ではなく塗りつぶし。
/// </summary>
public sealed class HatchEntity : Entity
{
    /// <summary>これ以上の本数になる場合はハッチ線を作らない (間隔が極端に細かいデータ対策)。</summary>
    public const int MaxLines = 50000;

    private List<(Vec2 A, Vec2 B)>? _lines;

    public HatchEntity(Layer layer, IEnumerable<IEnumerable<Vec2>> loops, IEnumerable<HatchPattern> patterns, bool solid = false) : base(layer)
    {
        Loops = [.. loops.Select(l => l.ToList()).Where(l => l.Count >= 3)];
        Patterns = [.. patterns];
        _solid = solid;
    }

    /// <summary>境界。各ループは閉じた多角形 (最後の点から最初の点へ戻る)。</summary>
    public List<List<Vec2>> Loops { get; }

    public IReadOnlyList<HatchPattern> Patterns { get; private set; }

    public bool Solid
    {
        get => _solid;
        set
        {
            _solid = value;
            _lines = null;
        }
    }

    private bool _solid;

    /// <summary>不透明度 (1 = 不透明、0 = 見えない)。下の図形を透かして見せるのに使う。</summary>
    public double Opacity
    {
        get => _opacity;
        set => _opacity = Math.Clamp(double.IsNaN(value) ? 1 : value, 0, 1);
    }

    private double _opacity = 1;

    /// <summary>透明度 (%)。0 = 不透明、100 = 透明。</summary>
    public double TransparencyPercent
    {
        get => Math.Round((1 - Opacity) * 100, 6);
        set => Opacity = 1 - value / 100;
    }

    /// <summary>元のパターン名 (DXF・SFC から読んだもの)。表示用。</summary>
    public string? PatternName { get; set; }

    public void SetPatterns(IEnumerable<HatchPattern> patterns)
    {
        Patterns = [.. patterns];
        _lines = null;
    }

    /// <summary>ハッチ線 (境界で切った線分)。境界・パターンが変わったら作り直す。</summary>
    public IReadOnlyList<(Vec2 A, Vec2 B)> Lines => _lines ??= Solid ? [] : ComputeLines(Loops, Patterns);

    public IEnumerable<(Vec2 A, Vec2 B)> BoundarySegments =>
        Loops.SelectMany(l => l.Select((p, i) => (p, l[(i + 1) % l.Count])));

    /// <summary>面積 (中抜きを除く)。</summary>
    public double Area
    {
        get
        {
            // 偶奇規則: 他のループの内側に奇数回入っているループは中抜き
            double sum = 0;
            for (var i = 0; i < Loops.Count; i++)
            {
                var depth = Loops.Where((_, j) => j != i).Count(o => IsInside(o, Loops[i][0]));
                sum += (depth % 2 == 0 ? 1 : -1) * Math.Abs(SignedArea(Loops[i]));
            }
            return Math.Max(sum, 0);
        }
    }

    public override Box2 Bounds => Box2.FromPoints(Loops.SelectMany(l => l));

    /// <summary>点が塗られる側 (境界の内側、中抜きの外側) にあるか。</summary>
    public bool Contains(Vec2 p) => Loops.Count(l => IsInside(l, p)) % 2 == 1;

    public override double DistanceTo(Vec2 p)
    {
        if (Solid && Contains(p)) return 0;
        var min = double.PositiveInfinity;
        foreach (var (a, b) in Lines) min = Math.Min(min, GeoMath.DistanceToSegment(p, a, b));
        foreach (var (a, b) in BoundarySegments) min = Math.Min(min, GeoMath.DistanceToSegment(p, a, b));
        return min;
    }

    public override bool IntersectsBox(Box2 box) =>
        Bounds.Intersects(box) &&
        (BoundarySegments.Any(s => GeoMath.SegmentIntersectsBox(s.A, s.B, box)) || Contains(box.Center) || box.Contains(Bounds));

    public override void Translate(Vec2 delta)
    {
        foreach (var l in Loops)
            for (var i = 0; i < l.Count; i++) l[i] += delta;
        Patterns = [.. Patterns.Select(p => p with { Origin = p.Origin + delta })];
        _lines = null;
    }

    public override void Rotate(Vec2 origin, double rad)
    {
        foreach (var l in Loops)
            for (var i = 0; i < l.Count; i++) l[i] = l[i].RotateAround(origin, rad);
        Patterns = [.. Patterns.Select(p => p with { Angle = p.Angle + rad, Origin = p.Origin.RotateAround(origin, rad) })];
        _lines = null;
    }

    public override Entity Clone() =>
        CopyPropertiesTo(new HatchEntity(Layer, Loops, Patterns, Solid) { PatternName = PatternName, Opacity = Opacity });

    public override IEnumerable<SnapPoint> SnapPoints => Loops.SelectMany(l => l).Select(p => new SnapPoint(p, SnapKind.Endpoint));

    public override IEnumerable<Curve> Curves => BoundarySegments.Select(s => (Curve)new SegmentCurve(s.A, s.B));

    // ---- 計算 ----

    /// <summary>
    /// 各パターンの平行線を境界で切る。線の向き d と法線 n について、n·p = c の線と各辺の交点を
    /// d 方向に並べ、偶奇で内側の区間を取り出す。
    /// </summary>
    public static List<(Vec2 A, Vec2 B)> ComputeLines(IReadOnlyList<IReadOnlyList<Vec2>> loops, IEnumerable<HatchPattern> patterns)
    {
        var result = new List<(Vec2, Vec2)>();
        var edges = loops.SelectMany(l => l.Select((p, i) => (A: p, B: l[(i + 1) % l.Count]))).ToList();
        if (edges.Count == 0) return result;

        foreach (var pat in patterns)
        {
            if (!(pat.Spacing > 0) || double.IsInfinity(pat.Spacing)) continue;
            var d = Vec2.FromAngle(pat.Angle);
            var n = d.LeftNormal;
            var o = n.Dot(pat.Origin);
            var min = double.PositiveInfinity;
            var max = double.NegativeInfinity;
            foreach (var (a, _) in edges)
            {
                var c = n.Dot(a);
                min = Math.Min(min, c);
                max = Math.Max(max, c);
            }
            var k0 = Math.Ceiling((min - o) / pat.Spacing);
            var k1 = Math.Floor((max - o) / pat.Spacing);
            if (k1 - k0 + 1 + result.Count > MaxLines) continue;

            var hits = new List<double>();
            for (var k = k0; k <= k1; k++)
            {
                var c = o + k * pat.Spacing;
                hits.Clear();
                foreach (var (a, b) in edges)
                {
                    var ca = n.Dot(a) - c;
                    var cb = n.Dot(b) - c;
                    // 半開区間で数える (頂点をちょうど通るときの二重数え防止)
                    if (ca > 0 == cb > 0) continue;
                    var t = ca / (ca - cb);
                    hits.Add(d.Dot(a + (b - a) * t));
                }
                if (hits.Count < 2) continue;
                hits.Sort();
                for (var i = 0; i + 1 < hits.Count; i += 2)
                {
                    if (hits[i + 1] - hits[i] < 1e-12) continue;
                    result.Add((d * hits[i] + n * c, d * hits[i + 1] + n * c));
                }
            }
        }
        return result;
    }

    public static double SignedArea(IReadOnlyList<Vec2> pts)
    {
        double s = 0;
        for (var i = 0; i < pts.Count; i++) s += pts[i].Cross(pts[(i + 1) % pts.Count]);
        return s / 2;
    }

    /// <summary>点が多角形の内側か (偶奇判定)。</summary>
    public static bool IsInside(IReadOnlyList<Vec2> poly, Vec2 p)
    {
        var inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var (a, b) = (poly[i], poly[j]);
            if (a.Y > p.Y != b.Y > p.Y && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }
}
