using CivilCad.Core.Geometry;

namespace CivilCad.Alignment;

public static class Angles
{
    /// <summary>CAD の数学角 (東=0, 反時計回り) → 測量の方向角 (北=0, 時計回り) [rad, 0〜2π)。</summary>
    public static double ToSurveyAzimuth(double mathAngle) => GeoMath.NormalizeAngle(Math.PI / 2 - mathAngle);

    /// <summary>ラジアン → "123°45'06.7""" (度分秒)。</summary>
    public static string ToDms(double rad, int secDecimals = 1)
    {
        var sign = rad < 0 ? "-" : "";
        var totalSec = Math.Round(Math.Abs(rad) * 180 / Math.PI * 3600, secDecimals);
        var d = (int)(totalSec / 3600);
        var m = (int)((totalSec - d * 3600) / 60);
        var s = totalSec - d * 3600 - m * 60;
        return $"{sign}{d}°{m:00}'{s.ToString("00." + new string('0', secDecimals))}\"";
    }
}
