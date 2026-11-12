using CivilCad.Core.Geometry;

namespace CivilCad.Alignment;

public enum ElementKind { Line, Arc, Clothoid }

/// <summary>
/// 平面線形の要素。方向角は CAD 座標系の数学角 (東=0, 反時計回り正, rad)。
/// Turn は +1 = 左カーブ, -1 = 右カーブ。
/// </summary>
public abstract class HorizontalElement(double length)
{
    public double Length { get; } = length;
    public double StartStation { get; internal set; }
    public double EndStation => StartStation + Length;
    public abstract ElementKind Kind { get; }

    /// <summary>要素始点から距離 s の点と接線方向。</summary>
    public abstract (Vec2 Point, double Direction) Evaluate(double s);

    public Vec2 StartPoint => Evaluate(0).Point;
    public Vec2 EndPoint => Evaluate(Length).Point;
    public double StartDirection => Evaluate(0).Direction;
    public double EndDirection => Evaluate(Length).Direction;
}

public sealed class LineElement(Vec2 start, double direction, double length) : HorizontalElement(length)
{
    public override ElementKind Kind => ElementKind.Line;
    public override (Vec2 Point, double Direction) Evaluate(double s) => (start + Vec2.FromAngle(direction) * s, direction);
}

public sealed class ArcElement(Vec2 start, double direction, double length, double radius, int turn) : HorizontalElement(length)
{
    public double Radius { get; } = radius;
    public int Turn { get; } = turn;
    public Vec2 Center { get; } = start + Vec2.FromAngle(direction).LeftNormal * (turn * radius);
    public override ElementKind Kind => ElementKind.Arc;

    public override (Vec2 Point, double Direction) Evaluate(double s)
    {
        var phi = Turn * s / Radius;
        return (Center + (start - Center).Rotate(phi), direction + phi);
    }
}

/// <summary>
/// クロソイド要素。Entering = true なら始点が曲率0 (KA→KE)、false なら終点が曲率0 (KE→KA)。
/// anchor は曲率0側の点と、その点での進行方向。
/// </summary>
public sealed class ClothoidElement(double a, double length, int turn, bool entering, Vec2 anchor, double anchorDirection)
    : HorizontalElement(length)
{
    public double A { get; } = a;
    public int Turn { get; } = turn;
    public bool Entering { get; } = entering;
    public double Radius => A * A / Length;
    public override ElementKind Kind => ElementKind.Clothoid;

    public override (Vec2 Point, double Direction) Evaluate(double s)
    {
        var t = Vec2.FromAngle(anchorDirection);
        var n = t.LeftNormal * Turn;
        if (Entering)
        {
            var p = Clothoid.LocalPoint(A, s);
            return (anchor + t * p.X + n * p.Y, anchorDirection + Turn * Clothoid.TangentAngle(A, s));
        }
        // 終点 (曲率0) から逆向きにたどる
        var u = Length - s;
        var q = Clothoid.LocalPoint(A, u);
        return (anchor - t * q.X + n * q.Y, anchorDirection - Turn * Clothoid.TangentAngle(A, u));
    }
}

/// <summary>
/// 一般のらせん (緩和曲線) 要素。曲率が始点 k1 から終点 k2 まで長さに比例して変わる (符号付き, 左 +, 右 −)。
/// 卵形クロソイド (R1 → R2) や、片方の曲率が 0 の普通のクロソイドを表せる。
/// 位置は接線角を積分して求める (ガウス・ルジャンドル求積を区間分割して使う。300m で 1e-9 m 程度)。
/// </summary>
public sealed class SpiralElement(Vec2 start, double direction, double length, double k1, double k2) : HorizontalElement(length)
{
    private static readonly double[] GlX = [0.0, -0.5384693101056831, 0.5384693101056831, -0.9061798459386640, 0.9061798459386640];
    private static readonly double[] GlW = [0.5688888888888889, 0.4786286704993665, 0.4786286704993665, 0.2369268850561891, 0.2369268850561891];

    public double K1 { get; } = k1;
    public double K2 { get; } = k2;
    public override ElementKind Kind => ElementKind.Clothoid;

    /// <summary>+1 = 左カーブ, -1 = 右カーブ (曲率 0 でない側の符号)。</summary>
    public int Turn => K1 != 0 ? Math.Sign(K1) : Math.Sign(K2);

    /// <summary>始点・終点の半径 (曲率 0 なら ∞)。</summary>
    public double RadiusStart => K1 == 0 ? double.PositiveInfinity : 1 / Math.Abs(K1);
    public double RadiusEnd => K2 == 0 ? double.PositiveInfinity : 1 / Math.Abs(K2);

    /// <summary>クロソイドパラメータ A (A² = L / |1/R2 − 1/R1|)。曲率が変わらなければ 0。</summary>
    public double A => K1 == K2 ? 0 : Math.Sqrt(Length / Math.Abs(K2 - K1));

    private double Theta(double t) => direction + K1 * t + (K2 - K1) * t * t / (2 * Length);

    public override (Vec2 Point, double Direction) Evaluate(double s)
    {
        if (s <= 0 || Length <= 0) return (start, direction);
        var dTheta = Math.Abs(Theta(s) - direction);
        var n = Math.Max(1, (int)Math.Ceiling(Math.Max(s / 15, dTheta / 0.2)));
        double x = 0, y = 0;
        var h = s / n;
        for (var i = 0; i < n; i++)
        {
            var mid = h * (i + 0.5);
            for (var j = 0; j < GlX.Length; j++)
            {
                var th = Theta(mid + GlX[j] * h / 2);
                x += GlW[j] * Math.Cos(th);
                y += GlW[j] * Math.Sin(th);
            }
        }
        return (start + new Vec2(x, y) * (h / 2), Theta(s));
    }
}

/// <summary>要素法の 1 要素の定義。半径は直線なら ∞、円曲線は始点 = 終点、クロソイドは自由 (∞ 可、両方有限なら卵形)。Turn は +1 左 / −1 右。</summary>
public sealed record ElementDef(ElementKind Kind, double Length, double RadiusStart, double RadiusEnd, int Turn, string? EndName = null)
{
    /// <summary>クロソイドパラメータ A (A² = L / |1/R2 − 1/R1|)。クロソイド以外は 0。</summary>
    public double A
    {
        get
        {
            if (Kind != ElementKind.Clothoid) return 0;
            var d = Math.Abs(Curv(RadiusEnd) - Curv(RadiusStart));
            return d > 0 ? Math.Sqrt(Length / d) : 0;
        }
    }

    private static double Curv(double r) => double.IsInfinity(r) || r <= 0 ? 0 : 1 / r;

    public static ElementDef Line(double length, string? endName = null) =>
        new(ElementKind.Line, length, double.PositiveInfinity, double.PositiveInfinity, 0, endName);

    public static ElementDef Arc(double length, double radius, int turn, string? endName = null) =>
        new(ElementKind.Arc, length, radius, radius, turn, endName);

    public static ElementDef Spiral(double length, double radiusStart, double radiusEnd, int turn, string? endName = null) =>
        new(ElementKind.Clothoid, length, radiusStart, radiusEnd, turn, endName);

    /// <summary>計算済みの要素から定義を作る (IP 法の線形を要素法の表にするとき)。</summary>
    public static ElementDef FromElement(HorizontalElement e, string? endName = null) => e switch
    {
        ArcElement a => Arc(a.Length, a.Radius, a.Turn, endName),
        ClothoidElement c => c.Entering
            ? Spiral(c.Length, double.PositiveInfinity, c.Radius, c.Turn, endName)
            : Spiral(c.Length, c.Radius, double.PositiveInfinity, c.Turn, endName),
        SpiralElement s => Spiral(s.Length, s.RadiusStart, s.RadiusEnd, s.Turn, endName),
        _ => Line(e.Length, endName),
    };
}
