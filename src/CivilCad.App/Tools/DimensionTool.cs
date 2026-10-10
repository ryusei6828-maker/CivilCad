using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>寸法の向き。</summary>
public enum DimMode { Aligned, Horizontal, Vertical }

/// <summary>寸法の設定 (作図入力バーで変更。次の作図でも使う)。大きさは用紙上の mm。</summary>
public static class DimSettings
{
    public static DimMode Mode { get; set; } = DimMode.Aligned;
    public static DimTerminator Terminator { get; set; } = DimTerminator.Arrow;
    public static DimUnit Unit { get; set; } = DimUnit.M;
    public static int Decimals { get; set; } = 3;
    public static double TextHeightMm { get; set; } = 2.5;
    public static double TerminatorSizeMm { get; set; } = 2.5;

    public static string ModeName(DimMode m) => m switch { DimMode.Horizontal => "水平", DimMode.Vertical => "垂直", _ => "平行" };

    public static string TerminatorName(DimTerminator t) => t switch { DimTerminator.Dot => "黒丸", DimTerminator.Tick => "斜線", _ => "矢印" };

    public static string UnitName(DimUnit u) => u == DimUnit.Mm ? "mm" : "m";

    public static double? AngleOf(DimMode m) => m switch { DimMode.Horizontal => 0, DimMode.Vertical => Math.PI / 2, _ => null };
}

/// <summary>
/// 寸法 (DIM): 1 点目 → 2 点目 → 寸法線の位置 の順にクリックする。
/// 置いたあとは、続けてクリックした点までの寸法を同じ寸法線の上に並べる (直列寸法)。Enter で新しい寸法、Esc で終了。
/// </summary>
public sealed class DimensionTool : CadTool, IStyledDrawTool
{
    private static readonly RgbColor PreviewColor = new(0x66, 0xCC, 0xFF);
    private Vec2? _p1;
    private Vec2? _p2;
    private Vec2? _line;   // 直列寸法のときの寸法線の位置
    private Vec2 _cursor;

    public override string Name => "寸法";
    public override Vec2? BasePoint => _p2 ?? _p1;
    public override bool CanStepBack => _p1 != null;
    public override string? ConfirmLabel => _line != null ? "直列をやめて新しい寸法へ (Enter)" : null;

    protected override void OnActivated() => UpdatePrompt();

    private void UpdatePrompt() => SetPrompt(
        _p1 == null ? $"寸法 ({DimSettings.ModeName(DimSettings.Mode)}): 1 点目を指定"
        : _line != null ? "続けて次の点を指定 (直列寸法) / Enter で新しい寸法 / Esc で終了"
        : _p2 == null ? "2 点目を指定"
        : "寸法線の位置を指定");

    public override void StepBack()
    {
        if (_line != null) { _line = null; _p1 = null; }
        else if (_p2 != null) _p2 = null;
        else _p1 = null;
        UpdatePrompt();
    }

    public override void OnPoint(Vec2 p)
    {
        if (_p1 is not Vec2 a)
        {
            _p1 = p;
        }
        else if (_line is Vec2 line)
        {
            if ((p - a).Length < 1e-9) return;
            Place(a, p, line);
        }
        else if (_p2 is not Vec2 b)
        {
            if ((p - a).Length < 1e-9) return;
            _p2 = p;
        }
        else
        {
            Place(a, b, p);
        }
        UpdatePrompt();
    }

    public override void OnEnter()
    {
        if (_p1 == null)
        {
            Finish();
            return;
        }
        _p1 = _p2 = _line = null;
        UpdatePrompt();
    }

    private DimensionEntity Make(Vec2 a, Vec2 b, Vec2 line) => new(Doc.CurrentLayer, a, b, line, DimSettings.AngleOf(DimSettings.Mode),
        Doc.PaperMmToMeters(DimSettings.TextHeightMm), Doc.PaperMmToMeters(DimSettings.TerminatorSizeMm))
    {
        Terminator = DimSettings.Terminator,
        Unit = DimSettings.Unit,
        Decimals = DimSettings.Decimals,
    };

    private void Place(Vec2 a, Vec2 b, Vec2 line)
    {
        var dim = Styled(Make(a, b, line));
        if (dim.Measured < 1e-9)
        {
            Canvas.ShowMessage("寸法: 長さが 0 です (水平・垂直寸法では、その向きに離れた 2 点を指定してください)。");
            return;
        }
        Execute(new AddEntitiesCommand([dim], "寸法"));
        Canvas.ShowMessage($"寸法 {dim.DisplayText} を記入しました。続けてクリックすると直列寸法になります。");
        // 直列寸法: 今の 2 点目から、同じ寸法線の上に続ける
        _p1 = b;
        _p2 = null;
        _line = dim.DimLine.B;
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void DrawPreview(DrawingContext dc)
    {
        if (_p1 is not Vec2 a) return;
        if (_line is Vec2 line)
        {
            if ((_cursor - a).Length > 1e-9) Canvas.DrawEntity(dc, Make(a, _cursor, line), PreviewColor);
            return;
        }
        if (_p2 is not Vec2 b)
        {
            Canvas.DrawWorldLine(dc, CadCanvas.PreviewDashPen, a, _cursor);
            return;
        }
        Canvas.DrawEntity(dc, Make(a, b, _cursor), PreviewColor);
    }
}
