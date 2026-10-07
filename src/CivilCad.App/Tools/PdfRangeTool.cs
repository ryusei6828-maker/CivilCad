using System.Windows;
using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;

namespace CivilCad.App.Tools;

/// <summary>PDF に印刷する範囲を 2 点 (四角の対角) で指定する。指定したら done を呼ぶ。Esc でやめる。</summary>
public sealed class PdfRangeTool(Action<Box2> done) : CadTool
{
    private Vec2? _first;
    private Vec2 _cursor;

    public override string Name => "印刷範囲";
    public override Vec2? BasePoint => _first;
    public override bool CanStepBack => _first != null;

    protected override void OnActivated() => UpdatePrompt();

    private void UpdatePrompt() => SetPrompt(_first == null ? "PDF に印刷する範囲の 1 点目 (四角の角) を指定 (Esc でやめる)" : "範囲の対角の点を指定");

    public override void StepBack()
    {
        _first = null;
        UpdatePrompt();
    }

    public override void OnPoint(Vec2 p)
    {
        if (_first is not Vec2 a)
        {
            _first = p;
            UpdatePrompt();
            return;
        }
        var box = Box2.FromPoints(a, p);
        if (box.Width < 1e-9 || box.Height < 1e-9) return; // 幅か高さが 0 の四角は範囲にならない
        Finish();
        done(box);
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void DrawPreview(DrawingContext dc)
    {
        if (_first is not Vec2 a) return;
        var r = new Rect(Canvas.WorldToScreen(a), Canvas.WorldToScreen(_cursor));
        dc.DrawRectangle(null, CadCanvas.PreviewDashPen, r);
    }
}
