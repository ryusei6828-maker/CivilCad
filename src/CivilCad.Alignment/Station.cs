using System.Globalization;
using System.Text.RegularExpressions;

namespace CivilCad.Alignment;

/// <summary>測点表記 (No.12+5.000) と追加距離の相互変換。</summary>
public static partial class Station
{
    public const double DefaultPitch = 20;

    public static string Format(double station, double pitch = DefaultPitch, int decimals = 3)
    {
        var no = (int)Math.Floor((station + 1e-9) / pitch);
        var plus = Math.Round(station - no * pitch, decimals);
        if (plus >= pitch) (no, plus) = (no + 1, 0);
        return plus < Math.Pow(10, -decimals) / 2
            ? $"No.{no}"
            : $"No.{no}+{plus.ToString("F" + decimals, CultureInfo.InvariantCulture)}";
    }

    /// <summary>"No.3+12.5" / "3+12.5" / "No.3" / "72.5"(追加距離) を受け付ける。</summary>
    public static bool TryParse(string text, out double station, double pitch = DefaultPitch)
    {
        station = 0;
        var m = StationRegex().Match(text.Trim());
        if (m.Success)
        {
            var no = double.Parse(m.Groups["no"].Value, CultureInfo.InvariantCulture);
            var plus = m.Groups["plus"].Success ? double.Parse(m.Groups["plus"].Value, CultureInfo.InvariantCulture) : 0;
            station = no * pitch + plus;
            return true;
        }
        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out station);
    }

    [GeneratedRegex(@"^(?:No\.?\s*(?<no>-?\d+)|(?<no>-?\d+)(?=\s*\+))\s*(?:\+\s*(?<plus>\d+(?:\.\d+)?))?$", RegexOptions.IgnoreCase)]
    private static partial Regex StationRegex();
}
