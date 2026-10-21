using System.Globalization;
using CivilCad.Core.Geometry;

namespace CivilCad.Core.Model;

/// <summary>寸法線の端末記号。</summary>
public enum DimTerminator { Arrow, Dot, Tick }

/// <summary>寸法値の単位。</summary>
public enum DimUnit { M, Mm }

/// <summary>
/// 長さの寸法 (平行・水平・垂直)。1 つの図形として選択・移動・回転・削除でき、寸法値は計った長さから自動で書く。
/// P1・P2 が計る 2 点、LinePoint は寸法線が通る点。
/// Angle が null なら平行寸法 (P1→P2 の向き)、値があればその向き (0 = 水平、π/2 = 垂直) で計る。
/// 大きさ (TextHeight・TerminatorSize) は図面上の m。
/// </summary>
public sealed class DimensionEntity(Layer layer, Vec2 p1, Vec2 p2, Vec2 linePoint, double? angle, double textHeight, double terminatorSize)
    : Entity(layer)
{
    public Vec2 P1 { get; set; } = p1;
    public Vec2 P2 { get; set; } = p2;
    public Vec2 LinePoint { get; set; } = linePoint;
    public double? Angle { get; set; } = angle;
    public double TextHeight { get; set; } = textHeight;
    public double TerminatorSize { get; set; } = terminatorSize;
    public DimTerminator Terminator { get; set; } = DimTerminator.Arrow;
    public DimUnit Unit { get; set; } = DimUnit.M;
    public int Decimals { get; set; } = 3;

    /// <summary>寸法値を書き換えた文字。null なら計った長さ。「&lt;&gt;」は計った長さに置き換える (例: L=&lt;&gt;)。</summary>
    public string? TextOverride { get; set; }

    /// <summary>計る向き (単位ベクトル)。</summary>
    public Vec2 Direction
    {
        get
        {
            if (Angle is double a) return Vec2.FromAngle(a);
            var d = (P2 - P1).Normalized();
            return d.Length > 0 ? d : new Vec2(1, 0);
        }
    }

    /// <summary>
    /// 縦の長さの倍率 (シートの縦の縮尺 / 横の縮尺。縦横で縮尺が違う縦断図などで 1 以外)。
    /// 寸法値は図面上の縦の長さにこれを掛けた実際の長さで計る。
    /// </summary>
    public double VerticalFactor { get; set; } = 1;

    /// <summary>
    /// 計った長さ (m)。縦横で縮尺が違えば、実際の大きさ (縦を VerticalFactor 倍) に直してから計る向きの長さを求める
    /// (水平の寸法は横の長さ、垂直の寸法は縦の長さ、平行の寸法は実際の斜めの長さ)。
    /// </summary>
    public double Measured
    {
        get
        {
            var d = P2 - P1;
            var u = Direction;
            var f = VerticalFactor;
            if (Math.Abs(f - 1) < 1e-12) return Math.Abs(d.Dot(u));
            // 実際の差 r = (dx, dy·f)、実際の向き u' = (ux, uy·f) / |…| → r·u'
            var norm = Math.Sqrt(u.X * u.X + u.Y * u.Y * f * f);
            return norm > 0 ? Math.Abs(d.X * u.X + d.Y * u.Y * f * f) / norm : 0;
        }
    }

    public string MeasuredText
    {
        get
        {
            var v = Unit == DimUnit.Mm ? Measured * 1000 : Measured;
            return v.ToString("F" + Math.Clamp(Decimals, 0, 6), CultureInfo.InvariantCulture);
        }
    }

    public string DisplayText => TextOverride is { } o ? o.Replace("<>", MeasuredText) : MeasuredText;

    /// <summary>寸法線の両端 (P1・P2 から寸法線へ下ろした点)。</summary>
    public (Vec2 A, Vec2 B) DimLine
    {
        get
        {
            var u = Direction;
            var n = u.LeftNormal;
            var offset = (LinePoint - P1).Dot(n);
            var a = P1 + n * offset;
            var b = a + u * (P2 - P1).Dot(u);
            return (a, b);
        }
    }

    /// <summary>補助線・寸法線・端末・寸法値に展開する (表示と保存に使う)。</summary>
    public IEnumerable<Entity> Explode()
    {
        var (a, b) = DimLine;
        var s = TerminatorSize;
        // 補助線: 計る点から少し離して始め、寸法線より少し先まで
        foreach (var (p, q) in new[] { (P1, a), (P2, b) })
        {
            var d = q - p;
            if (d.Length < 1e-9) continue;
            var dir = d.Normalized();
            var gap = Math.Min(s * 0.3, d.Length * 0.5);
            yield return CopyPropertiesTo(new LineEntity(Layer, p + dir * gap, q + dir * s * 0.6));
        }
        if ((b - a).Length < 1e-9) yield break;
        yield return CopyPropertiesTo(new LineEntity(Layer, a, b));

        // 端末: 両端から内側へ
        var u = (b - a).Normalized();
        foreach (var (tip, inward) in new[] { (a, u), (b, -u) })
        {
            switch (Terminator)
            {
                case DimTerminator.Arrow:
                    var back = tip + inward * s;
                    var side = inward.LeftNormal * (s * 0.18);
                    yield return CopyPropertiesTo(new HatchEntity(Layer, [[tip, back + side, back - side]], [], solid: true));
                    break;
                case DimTerminator.Dot:
                    var r = s * 0.25;
                    yield return CopyPropertiesTo(new HatchEntity(Layer,
                        [Enumerable.Range(0, 24).Select(i => tip + Vec2.FromAngle(i * Math.PI / 12) * r)], [], solid: true));
                    break;
                case DimTerminator.Tick:
                    var t = Vec2.FromAngle(u.Angle + Math.PI / 4) * (s * 0.4);
                    yield return CopyPropertiesTo(new LineEntity(Layer, tip - t, tip + t));
                    break;
            }
        }

        // 寸法値: 寸法線の中央の上側に、読める向き (下から・右から読む) で
        var angle = u.Angle;
        if (angle > Math.PI / 2 + 1e-9) angle -= Math.PI;
        else if (angle <= -Math.PI / 2 + 1e-9) angle += Math.PI;
        var up = Vec2.FromAngle(angle).LeftNormal;
        var mid = (a + b) / 2 + up * (TextHeight * 0.3);
        yield return CopyPropertiesTo(new TextEntity(Layer, mid, DisplayText, TextHeight, angle)
        {
            HAlign = TextHAlign.Center,
            VAlign = TextVAlign.Bottom,
        });
    }

    public override IEnumerable<Entity> ExplodeForOutput() => Explode();

    public override Box2 Bounds => Explode().Aggregate(Box2.Empty, (acc, e) => acc.Union(e.Bounds)).Union(Box2.FromPoints(P1, P2));

    public override double DistanceTo(Vec2 p) => Explode().Min(e => e.DistanceTo(p));

    public override bool IntersectsBox(Box2 box) => Explode().Any(e => e.IntersectsBox(box));

    public override void Translate(Vec2 delta)
    {
        P1 += delta;
        P2 += delta;
        LinePoint += delta;
    }

    public override void Rotate(Vec2 origin, double rad)
    {
        P1 = P1.RotateAround(origin, rad);
        P2 = P2.RotateAround(origin, rad);
        LinePoint = LinePoint.RotateAround(origin, rad);
        if (Angle is double a) Angle = a + rad;
    }

    public override void Scale(Vec2 origin, double k)
    {
        P1 = ScalePoint(P1, origin, k);
        P2 = ScalePoint(P2, origin, k);
        LinePoint = ScalePoint(LinePoint, origin, k);
        TextHeight *= k;
        TerminatorSize *= k;
    }

    public override IEnumerable<Curve> Curves => Explode().OfType<LineEntity>().SelectMany(l => l.Curves);

    public override Entity Clone() => CopyPropertiesTo(new DimensionEntity(Layer, P1, P2, LinePoint, Angle, TextHeight, TerminatorSize)
    {
        Terminator = Terminator,
        Unit = Unit,
        Decimals = Decimals,
        TextOverride = TextOverride,
        VerticalFactor = VerticalFactor,
    });

    public override IEnumerable<SnapPoint> SnapPoints
    {
        get
        {
            var (a, b) = DimLine;
            return [new(P1, SnapKind.Node), new(P2, SnapKind.Node), new(a, SnapKind.Endpoint), new(b, SnapKind.Endpoint), new((a + b) / 2, SnapKind.Midpoint)];
        }
    }
}
