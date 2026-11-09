namespace CivilCad.Alignment;

/// <summary>
/// 縦断変化点 1 つの諸元。勾配は小数 (1% = 0.01)、端の点の無い側の勾配は NaN。
/// Radius = 縦断曲線半径 VCR (= L / |勾配の差|)、曲線が無ければ +∞。
/// MiddleOrdinate = 中央縦距 (変化点での勾配線と曲線の高さの差 = (g2 − g1)·L / 8)。+ は凹 (サグ)、− は凸 (クレスト)。
/// </summary>
public sealed record VerticalCurveRow(
    int Index,
    double Station, double Elevation,
    double GradeIn, double GradeOut,
    double Length, double Radius,
    double BvcStation, double BvcElevation,
    double EvcStation, double EvcElevation,
    double MiddleOrdinate)
{
    public bool HasCurve => Length > 0;
    /// <summary>凸 (クレスト) / 凹 (サグ) / 空。</summary>
    public string Kind => !HasCurve ? "" : MiddleOrdinate < 0 ? "凸" : "凹";
}

/// <summary>縦断線形の変化点ごとの諸元 (縦断曲線表)。</summary>
public static class VerticalCurveTable
{
    public static List<VerticalCurveRow> Compute(VerticalAlignment v)
    {
        var ips = v.Ips;
        var rows = new List<VerticalCurveRow>();
        for (var i = 0; i < ips.Count; i++)
        {
            var p = ips[i];
            var gIn = i > 0 ? v.Grade(i - 1) : double.NaN;
            var gOut = i < ips.Count - 1 ? v.Grade(i) : double.NaN;
            var inner = i > 0 && i < ips.Count - 1;
            var l = inner ? p.CurveLength : 0;
            var dg = inner ? gOut - gIn : 0;
            var radius = l > 0 && Math.Abs(dg) > 1e-12 ? l / Math.Abs(dg) : double.PositiveInfinity;
            var (bvc, evc) = (p.Station - l / 2, p.Station + l / 2);
            rows.Add(new VerticalCurveRow(
                i, p.Station, p.Elevation, gIn, gOut, l, radius,
                bvc, v.ElevationAt(bvc), evc, v.ElevationAt(evc),
                l > 0 ? dg * l / 8 : 0));
        }
        return rows;
    }

    /// <summary>縦断曲線半径 VCR と前後の勾配 (小数) から縦断曲線長 VCL。</summary>
    public static double LengthFromRadius(double radius, double gradeIn, double gradeOut) =>
        radius > 0 ? radius * Math.Abs(gradeOut - gradeIn) : 0;
}
