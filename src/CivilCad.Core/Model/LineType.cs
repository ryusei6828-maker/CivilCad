namespace CivilCad.Core.Model;

/// <summary>線種。</summary>
public enum LineType { Continuous, Dashed, Dotted, DashDot, DashDotDot }

/// <summary>
/// 線種の名前と模様。模様は用紙上の mm (正 = 線を引く長さ、負 = 空ける長さ、0 = 点) で、
/// 図面の縮尺によらず印刷したときに同じ見た目になる (JIS の製図の線の目安に合わせた長さ)。
/// </summary>
public static class LineTypes
{
    public static IReadOnlyList<LineType> All { get; } = Enum.GetValues<LineType>();

    public static string Name(LineType t) => t switch
    {
        LineType.Dashed => "破線",
        LineType.Dotted => "点線",
        LineType.DashDot => "一点鎖線",
        LineType.DashDotDot => "二点鎖線",
        _ => "実線",
    };

    public static bool TryParse(string s, out LineType type)
    {
        foreach (var t in All)
            if (Name(t) == s.Trim())
            {
                type = t;
                return true;
            }
        type = LineType.Continuous;
        return false;
    }

    /// <summary>模様 (用紙上の mm)。実線は空。</summary>
    public static IReadOnlyList<double> PatternMm(LineType t) => t switch
    {
        LineType.Dashed => [3, -1],
        LineType.Dotted => [0, -1.5],
        LineType.DashDot => [10, -1.5, 0, -1.5],
        LineType.DashDotDot => [10, -1.5, 0, -1.5, 0, -1.5],
        _ => [],
    };

    /// <summary>模様 1 回分の長さ (用紙上の mm)。</summary>
    public static double PeriodMm(LineType t) => PatternMm(t).Sum(Math.Abs);
}
