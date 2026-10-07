using System.Windows.Media;
using CivilCad.App.Drawing;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;

namespace CivilCad.App.Tools;

/// <summary>図枠の配置。クリックした点が外枠の左下になる。Enter なら描いてある図形が枠の中央に来るように置く。</summary>
public sealed class FrameTool(DrawingFrameOptions options) : CadTool
{
    private Vec2 _cursor;
    private Box2? _extents;

    public override string Name => "図枠";

    protected override void OnActivated()
    {
        _extents = DrawingFrame.DrawingExtents(Doc);
        SetPrompt($"外枠の左下の位置を指定 ({options.PaperSize.Name} 1:{options.ScaleDenominator:0.##})" +
                  (_extents != null ? " / Enter: 描いてある図形が枠の中央に来るように配置" : ""));
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void OnEnter()
    {
        if (_extents is Box2 ext) Place(DrawingFrame.CenteredOrigin(options, ext));
        else Finish();
    }

    public override void OnPoint(Vec2 p) => Place(p);

    private void Place(Vec2 origin)
    {
        var entities = DrawingFrame.Build(Doc, options, origin);
        Execute(new AddEntitiesCommand(entities, "図枠"));
        var (w, h) = DrawingFrame.FrameSize(options);
        var note = _extents is Box2 ext && !DrawingFrame.Fits(options, ext)
            ? $" ※描いてある図形 ({ext.Width:0.###}×{ext.Height:0.###} m) が枠に収まりません。縮尺 1:{DrawingFrame.FitScale(options, ext):0.##} 以上の図枠にしてください。"
            : "";
        Canvas.ShowMessage($"図枠を配置しました ({options.PaperSize.Name}, 1:{options.ScaleDenominator:0.##}, 外枠 {w:0.###}×{h:0.###} m)。{note}");
        // 枠が画面からはみ出していれば、枠全体が見えるようにする
        var box = new Box2(origin, origin + new Vec2(w, h));
        if (!Canvas.VisibleWorldBox.Contains(box)) Canvas.ZoomTo(box.Inflate(Math.Max(w, h) * 0.05));
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
