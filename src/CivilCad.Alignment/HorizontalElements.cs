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
