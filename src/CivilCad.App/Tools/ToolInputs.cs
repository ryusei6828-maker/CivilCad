using CivilCad.Core.Geometry;

namespace CivilCad.App.Tools;

/// <summary>作図入力バーに線幅・色の欄を出す作図ツール。</summary>
public interface IStyledDrawTool;

/// <summary>次の点までの長さ・角度を固定できるツール (線・ポリライン)。null は固定しない。</summary>
public interface ILengthAngleInput
{
    double? FixedLength { get; set; }

    /// <summary>角度 (度)。東 = 0°、反時計回りが正 (極座標入力 @距離&lt;角度 と同じ)。</summary>
    double? FixedAngleDeg { get; set; }

    /// <summary>基準点 (直前の点) からカーソルまでの今の長さと角度 (度)。基準点が無ければ null。</summary>
    (double Length, double AngleDeg)? Live { get; }
}

/// <summary>半径を固定できるツール (円)。</summary>
public interface IRadiusInput
{
    double? FixedRadius { get; set; }

    /// <summary>中心からカーソルまでの今の半径。中心が未指定なら null。</summary>
    double? LiveRadius { get; }
}

/// <summary>
/// 作図入力バーで入れた長さ・角度・半径。空欄にするまで次のコマンドでも使い続ける
/// (同じ長さの線や同じ半径の円を続けて描けるように)。
/// </summary>
public static class DrawInputMemory
{
    public static double? Length { get; set; }
    public static double? AngleDeg { get; set; }
    public static double? Radius { get; set; }

    /// <summary>円弧の半径 (円とは別に覚える)。</summary>
    public static double? ArcRadius { get; set; }
}

public static class LengthAngle
{
    /// <summary>基準点からカーソル方向へ、長さ・角度の固定を当てはめた点。</summary>
    public static Vec2 Apply(Vec2 basePoint, Vec2 cursor, double? length, double? angleDeg)
    {
        var d = cursor - basePoint;
        var angle = angleDeg is double a ? a * Math.PI / 180 : d.Angle;
        double len;
        if (length is double l) len = l;
        else if (angleDeg != null) len = d.Dot(Vec2.FromAngle(angle)); // 角度だけ固定: カーソルを方向線に投影
        else return cursor;
        return basePoint + Vec2.FromAngle(angle) * len;
    }

    public static (double Length, double AngleDeg) Measure(Vec2 basePoint, Vec2 p)
    {
        var d = p - basePoint;
        return (d.Length, GeoMath.NormalizeAngle(d.Angle) * 180 / Math.PI);
    }
}
