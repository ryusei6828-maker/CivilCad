using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>
/// 削除モード (消しゴム)。図形をクリックするとすぐ消える。ドラッグで範囲に完全に入る図形をまとめて消す
/// (向きは問わない)。Esc で終了するまで続く。
/// </summary>
public sealed class EraseTool : CadTool
{
    private static readonly RgbColor HighlightColor = new(255, 80, 80);
    private static readonly Brush WindowFill = Frozen(new SolidColorBrush(Color.FromArgb(40, 255, 80, 80)));

    private readonly BoxDrag _drag = new();
    private Entity? _hover;

    public override string Name => "削除";
    public override bool WantsSnap => false;
    public override bool IsDragging => _drag.IsMouseDrag;

    protected override void OnActivated() =>
        SetPrompt($"消す図形をクリック / ドラッグで{BoxDrag.ModeText}をまとめて削除 / Esc で終了");

    public override void OnPress(Vec2 p) => _drag.Press(p);

    public override void OnRelease(Vec2 p)
    {
        if (_drag.Release(Canvas, p) is not var (start, moved)) return;
        if (moved) Remove(BoxDrag.Hits(Doc, start, p));
        else if (Canvas.HitTest(start) is { } hit) Execute(new RemoveEntitiesCommand([hit]));
        _hover = Canvas.HitTest(p);
    }

    /// <summary>コマンド欄からの座標入力: 2 点で範囲削除。</summary>
    public override void OnPoint(Vec2 p)
    {
        if (_drag.TypedCorner is not Vec2 corner)
        {
            _drag.TypedCorner = p;
            SetPrompt("範囲のもう一方の角を指定 (範囲内の図形を削除)");
            return;
        }
        Remove(BoxDrag.Hits(Doc, corner, p));
        _drag.TypedCorner = null;
        OnActivated();
    }

    private void Remove(List<Entity> hits)
    {
        if (hits.Count == 0) return;
        Execute(new RemoveEntitiesCommand(hits, $"削除 ({hits.Count} 個)"));
        Canvas.ShowMessage($"{hits.Count} 個の図形を削除しました。");
    }

    public override void OnMouseMove(Vec2 p)
    {
        _drag.Current = p;
        _hover = _drag.IsActive ? null : Canvas.HitTest(p);
    }

    public override void OnEnter() { }

    public override void OnCancel()
    {
        if (_drag.IsActive)
        {
            _drag.Reset();
            OnActivated();
        }
        else
        {
            Finish();
        }
    }

    public override void DrawPreview(DrawingContext dc)
    {
        // カーソル下の図形を赤く強調 (クリックで消える図形)
        if (_hover != null && Doc.Entities.Contains(_hover)) Canvas.DrawEntity(dc, _hover, HighlightColor, true);
        _drag.Draw(Canvas, dc, WindowFill);
    }

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }
}

/// <summary>移動 / 複写 (基点 → 目的点)。複写は Enter まで繰り返し。</summary>
public sealed class MoveCopyTool(bool copy) : CadTool
{
    private const int MaxPreviewEntities = 300;
    private List<Entity> _targets = [];
    private Vec2? _base;
    private Vec2 _cursor;

    public override string Name => copy ? "複写" : "移動";
    public override Vec2? BasePoint => _base;
    public override bool RequiresSelection => true;
    public override bool CanStepBack => _base != null;
    public override string? ConfirmLabel => copy && _base != null ? "複写を終える (Enter)" : null;

    public override void StepBack()
    {
        _base = null;
        SetPrompt("基点を指定");
    }

    protected override void OnActivated()
    {
        _targets = [.. Canvas.Selection];
        SetPrompt("基点を指定");
    }

    public override void OnPoint(Vec2 p)
    {
        if (_base is not Vec2 b)
        {
            _base = p;
            SetPrompt(copy ? "複写先を指定 (Enter で終了)" : "移動先を指定");
            return;
        }

        var delta = p - b;
        if (copy)
        {
            var clones = _targets.Select(e => e.Clone()).ToList();
            clones.ForEach(e => e.Translate(delta));
            Execute(new AddEntitiesCommand(clones, "複写"));
        }
        else
        {
            Execute(new TranslateEntitiesCommand(_targets, delta));
            // 続けて同じ図形を移動できる (Esc で終了)
            _base = null;
            SetPrompt("続けて移動: 基点を指定 (Esc で終了)");
        }
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void DrawPreview(DrawingContext dc)
    {
        if (_base is not Vec2 b) return;
        Canvas.DrawWorldLine(dc, CadCanvas.PreviewDashPen, b, _cursor);
        var delta = _cursor - b;
        if (_targets.Count > MaxPreviewEntities)
        {
            var box = _targets.Aggregate(Box2.Empty, (acc, e) => acc.Union(e.Bounds));
            Canvas.DrawWorldPolyline(dc, CadCanvas.PreviewPen,
                [box.Min + delta, new Vec2(box.Max.X, box.Min.Y) + delta, box.Max + delta, new Vec2(box.Min.X, box.Max.Y) + delta], true);
            return;
        }
        foreach (var e in _targets)
        {
            var ghost = e.Clone();
            ghost.Translate(delta);
            Canvas.DrawEntity(dc, ghost, new RgbColor(0x66, 0xCC, 0xFF));
        }
    }
}
