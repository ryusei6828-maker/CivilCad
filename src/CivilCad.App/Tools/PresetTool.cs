using System.Windows.Media;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>プリセットの設定 (作図入力バーで変更。次の配置でも使う)。</summary>
public static class PresetSettings
{
    public static string Kind { get; set; } = SymbolLibrary.NorthArrow;

    /// <summary>用紙上の大きさ (mm)。プリセットを選び直すと、そのプリセットの標準の大きさになる。</summary>
    public static double SizeMm { get; set; } = SymbolLibrary.Get(SymbolLibrary.NorthArrow).DefaultSizeMm;

    /// <summary>向き (度)。0 = 図面の上が北、反時計回りが正。</summary>
    public static double AngleDeg { get; set; }
}

/// <summary>
/// プリセット (方位記号など) の配置。カーソルに記号が付いて動き、クリックした位置に置く。
/// 大きさは用紙上の mm で、図面の縮尺によらず用紙上 (画面で図面全体を見たとき) 同じ大きさになる。
/// Esc で終了するまで続けて置ける。
/// </summary>
public sealed class PresetTool : CadTool, IStyledDrawTool
{
    private static readonly RgbColor PreviewColor = new(0x66, 0xCC, 0xFF);
    private Vec2 _cursor;
    private bool _hasCursor;

    public override string Name => "プリセット";

    protected override void OnActivated() =>
        SetPrompt($"{SymbolLibrary.Get(PresetSettings.Kind).Name} を置く位置を指定 [大きさ {PresetSettings.SizeMm:0.##}mm、向き {PresetSettings.AngleDeg:0.##}°] (Esc で終了)");

    private SymbolEntity Make(Vec2 p) =>
        new(Doc.CurrentLayer, PresetSettings.Kind, p, PresetSettings.SizeMm, Doc.PaperMmToMeters(PresetSettings.SizeMm), PresetSettings.AngleDeg * Math.PI / 180);

    public override void OnPoint(Vec2 p)
    {
        Execute(new AddEntitiesCommand([Styled(Make(p))], SymbolLibrary.Get(PresetSettings.Kind).Name));
        OnActivated();
    }

    public override void OnMouseMove(Vec2 p)
    {
        _cursor = p;
        _hasCursor = true;
    }

    public override void DrawPreview(DrawingContext dc)
    {
        if (_hasCursor) Canvas.DrawEntity(dc, Make(_cursor), PreviewColor);
    }
}
