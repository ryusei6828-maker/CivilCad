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

    /// <summary>線の向きの刻み (度、90 / 45 / 15)。null は刻まない (自由な角度)。角度を固定しているときは使わない。</summary>
    public static double? AngleStepDeg { get; set; }

    public static double? Radius { get; set; }

    /// <summary>円弧の半径 (円とは別に覚える)。</summary>
    public static double? ArcRadius { get; set; }

    /// <summary>作図入力バーの長さ・半径の入力単位 (値そのものは常に m で持つ)。</summary>
    public static LengthUnit Unit { get; set; } = LengthUnit.M;
}

public enum LengthUnit { Mm, Cm, M }

public static class LengthUnits
{
    /// <summary>1 単位が何 m か。</summary>
    public static double Meters(this LengthUnit u) => u switch { LengthUnit.Mm => 0.001, LengthUnit.Cm => 0.01, _ => 1 };

    public static string Label(this LengthUnit u) => u switch { LengthUnit.Mm => "mm", LengthUnit.Cm => "cm", _ => "m" };

    /// <summary>mm → cm → m → mm の順に切り替える。</summary>
    public static LengthUnit Next(this LengthUnit u) => u switch { LengthUnit.Mm => LengthUnit.Cm, LengthUnit.Cm => LengthUnit.M, _ => LengthUnit.Mm };
}

public static class LengthAngle
{
    /// <summary>
    /// 基準点からカーソル方向へ、長さ・角度の固定を当てはめた点。
    /// stepDeg があれば (角度を固定していないとき) 向きをその刻み (東 = 0° から 90° / 45° / 15° ごと) の一番近い向きにする。
    /// </summary>
    public static Vec2 Apply(Vec2 basePoint, Vec2 cursor, double? length, double? angleDeg, double? stepDeg = null)
    {
        var d = cursor - basePoint;
        double? fixedDeg = angleDeg;
        if (fixedDeg == null && stepDeg is double step && step > 0 && d.Length > 0)
            fixedDeg = Math.Round(d.Angle * 180 / Math.PI / step) * step;
        var angle = fixedDeg is double a ? a * Math.PI / 180 : d.Angle;
        double len;
        if (length is double l) len = l;
        else if (fixedDeg != null) len = d.Dot(Vec2.FromAngle(angle)); // 角度だけ決まる: カーソルを方向線に投影
        else return cursor;
        return basePoint + Vec2.FromAngle(angle) * len;
    }

    /// <summary>刻みの選択肢 (null = なし)。</summary>
    public static IReadOnlyList<double?> Steps { get; } = [null, 90, 45, 15];

    public static string StepName(double? step) => step is double s ? $"{s:0}°刻み" : "刻みなし";

    public static (double Length, double AngleDeg) Measure(Vec2 basePoint, Vec2 p)
    {
        var d = p - basePoint;
        return (d.Length, GeoMath.NormalizeAngle(d.Angle) * 180 / Math.PI);
    }
}
