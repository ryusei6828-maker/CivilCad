namespace CivilCad.Core.Model;

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static readonly RgbColor White = new(255, 255, 255);
    public static readonly RgbColor Red = new(255, 0, 0);
    public static readonly RgbColor Yellow = new(255, 255, 0);
    public static readonly RgbColor Green = new(0, 255, 0);
    public static readonly RgbColor Cyan = new(0, 255, 255);
    public static readonly RgbColor Blue = new(0, 0, 255);
    public static readonly RgbColor Magenta = new(255, 0, 255);
    public static readonly RgbColor Orange = new(255, 165, 0);
    public static readonly RgbColor Gray = new(128, 128, 128);
    public static readonly RgbColor LightGray = new(192, 192, 192);

    /// <summary>レイヤ色の選択肢 (CAD の基本色 + α)。</summary>
    public static IReadOnlyList<RgbColor> Palette { get; } =
        [White, Red, Yellow, Green, Cyan, Blue, Magenta, Orange, LightGray, Gray];

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
    public override string ToString() => ToHex();
}
