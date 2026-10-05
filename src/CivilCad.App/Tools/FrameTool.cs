using System.Windows.Media;
using CivilCad.App.Drawing;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;

namespace CivilCad.App.Tools;

/// <summary>図枠の配置。クリックした点が外枠の左下になる。</summary>
public sealed class FrameTool(DrawingFrameOptions options) : CadTool
{
    private Vec2 _cursor;

    public override string Name => "図枠";

    protected override void OnActivated() =>
        SetPrompt($"外枠の左下の位置を指定 ({options.PaperSize.Name} 1:{options.ScaleDenominator:0.##})");

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void OnPoint(Vec2 p)
    {
        var entities = DrawingFrame.Build(Doc, options, p);
        Execute(new AddEntitiesCommand(entities, "図枠"));
        var (w, h) = DrawingFrame.FrameSize(options);
        Canvas.ShowMessage($"図枠を配置しました ({options.PaperSize.Name}, 1:{options.ScaleDenominator:0.##}, 外枠 {w:0.###}×{h:0.###} m)。");
        Finish();
    }

    public override void DrawPreview(DrawingContext dc)
    {
        // 外枠と表題欄の輪郭をカーソルに追従させる
        var (w, h) = DrawingFrame.FrameSize(options);
        var o = _cursor;
        Canvas.DrawWorldPolyline(dc, CadCanvas.PreviewPen, [o, o + new Vec2(w, 0), o + new Vec2(w, h), o + new Vec2(0, h)], true);
        if (!options.IncludeTitleBlock) return;
        var k = options.MetersPerPaperMm;
        var tw = DrawingFrame.TitleWidth * k;
        var th = DrawingFrame.TitleHeight * k;
        var t = o + new Vec2(w - tw, 0);
        Canvas.DrawWorldPolyline(dc, CadCanvas.PreviewDashPen, [t, t + new Vec2(tw, 0), t + new Vec2(tw, th), t + new Vec2(0, th)], true);
    }
}
