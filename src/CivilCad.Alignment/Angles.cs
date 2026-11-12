using CivilCad.Core.Geometry;

namespace CivilCad.Alignment;

public static class Angles
{
    /// <summary>CAD の数学角 (東=0, 反時計回り) → 測量の方向角 (北=0, 時計回り) [rad, 0〜2π)。</summary>
    public static double ToSurveyAzimuth(double mathAngle) => GeoMath.NormalizeAngle(Math.PI / 2 - mathAngle);

    /// <summary>測量の方向角 (北=0, 時計回り) → CAD の数学角 (東=0, 反時計回り) [rad, 0〜2π)。ToSurveyAzimuth の逆。</summary>
    public static double FromSurveyAzimuth(double azimuth) => GeoMath.NormalizeAngle(Math.PI / 2 - azimuth);

    /// <summary>
    /// 角度の文字 → ラジアン。度の小数 (123.4567)・度分秒 (123°45'06.7" / 123-45-06.7 / 123 45 06.7) を読める。
    /// 数として読めなければ false。
    /// </summary>
    public static bool TryParseDegrees(string? text, out double radians)
    {
        radians = 0;
        var t = (text ?? "").Trim();
        if (t.Length == 0) return false;
        var sign = 1.0;
        if (t[0] is '-' or '−') { sign = -1; t = t[1..]; }
        var parts = t.Split(['°', '度', '\'', '′', '"', '″', '分', '秒', '-', ' ', '　'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 3) return false;
        double deg = 0, scale = 1;
        foreach (var p in parts)
        {
            if (!double.TryParse(p, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) || v < 0) return false;
            deg += v / scale;
            scale *= 60;
        }
        radians = sign * deg * Math.PI / 180;
        return true;
    }

    /// <summary>ラジアン → "123°45'06.7""" (度分秒)。</summary>
    public static string ToDms(double rad, int secDecimals = 1)
    {
        var sign = rad < 0 ? "-" : "";
        var totalSec = Math.Round(Math.Abs(rad) * 180 / Math.PI * 3600, secDecimals);
        var d = (int)(totalSec / 3600);
        var m = (int)((totalSec - d * 3600) / 60);
        var s = totalSec - d * 3600 - m * 60;
        return $"{sign}{d}°{m:00}'{s.ToString("00." + new string('0', secDecimals), System.Globalization.CultureInfo.InvariantCulture)}\"";
    }
}
