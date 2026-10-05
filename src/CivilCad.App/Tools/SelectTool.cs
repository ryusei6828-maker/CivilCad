using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>
/// 選択。図形クリックで選択、ドラッグで範囲選択、空き部分クリックで選択解除。
/// 左→右へのドラッグは窓 (完全に含まれる図形)、右→左は交差 (かかる図形)。Shift で選択解除。
/// </summary>
public sealed class SelectTool : CadTool
{
    private static readonly Brush WindowFill = Frozen(new SolidColorBrush(Color.FromArgb(40, 80, 140, 255)));
    private static readonly Brush CrossingFill = Frozen(new SolidColorBrush(Color.FromArgb(40, 80, 255, 120)));

    private readonly BoxDrag _drag = new();

    public override string Name => "選択";
    public override bool WantsSnap => false;
    public override bool IsDragging => _drag.IsMouseDrag;

    protected override void OnActivated() =>
        SetPrompt("図形をクリックで選択 / ドラッグで範囲選択 (左→右 = 窓、右→左 = 交差) / Shift で解除、またはコマンドを入力");

    public override void OnPress(Vec2 p) => _drag.Press(p);

    public override void OnRelease(Vec2 p)
    {
        if (_drag.Release(Canvas, p) is not var (start, moved)) return;
        var remove = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (moved) Canvas.AddToSelection(BoxDrag.Hits(Doc, start, p), remove);
        else if (Canvas.HitTest(start) is { } hit) Canvas.AddToSelection([hit], remove);
        else if (!remove) Canvas.ClearSelection();
    }

    /// <summary>コマンド欄からの座標入力: 2 点で範囲選択。</summary>
    public override void OnPoint(Vec2 p)
    {
        if (_drag.TypedCorner is not Vec2 corner)
        {
            _drag.TypedCorner = p;
            SetPrompt("範囲のもう一方の角を指定");
            return;
        }
        Canvas.AddToSelection(BoxDrag.Hits(Doc, corner, p), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        _drag.TypedCorner = null;
        OnActivated();
    }

    public override void OnMouseMove(Vec2 p) => _drag.Current = p;

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
            Canvas.ClearSelection();
        }
    }

    public override void DrawPreview(DrawingContext dc) => _drag.Draw(Canvas, dc, WindowFill, CrossingFill);

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }
}

/// <summary>
/// 選択・削除で使う範囲指定。マウスは押した位置からドラッグした範囲、コマンド欄からは 2 点。
/// 数ピクセル以内しか動かさなければドラッグではなくクリックとみなす。
/// </summary>
internal sealed class BoxDrag
{
    private const double ClickTolerancePx = 4;

    /// <summary>マウスを押した位置 (ドラッグ中だけ)。</summary>
    public Vec2? Start { get; private set; }

    /// <summary>コマンド欄で入力した 1 点目。</summary>
    public Vec2? TypedCorner { get; set; }

    public Vec2 Current { get; set; }
    public bool IsMouseDrag => Start != null;
    public bool IsActive => Start != null || TypedCorner != null;

    public void Press(Vec2 p)
    {
        Start = Current = p;
        TypedCorner = null;
    }

    /// <summary>ボタンを離した。押していなければ null、そうでなければ押した位置とドラッグしたかどうか。</summary>
    public (Vec2 Start, bool Moved)? Release(CadCanvas canvas, Vec2 p)
    {
        if (Start is not Vec2 s) return null;
        Start = null;
        return (s, IsMoved(canvas, s, p));
    }

    public void Reset() => Start = TypedCorner = null;

    public static bool IsMoved(CadCanvas canvas, Vec2 a, Vec2 b) =>
        a.DistanceTo(b) * canvas.Scale >= ClickTolerancePx;

    /// <summary>範囲内の図形。a → b が右向きなら窓 (完全に含まれる)、左向きなら交差 (かかる)。</summary>
    public static List<Entity> Hits(CadDocument doc, Vec2 a, Vec2 b)
    {
        var box = Box2.FromPoints(a, b);
        var crossing = b.X < a.X;
        return doc.Entities.Where(e => doc.IsEditable(e) && (crossing ? e.IntersectsBox(box) : box.Contains(e.Bounds))).ToList();
    }

    public void Draw(CadCanvas canvas, DrawingContext dc, Brush windowFill, Brush crossingFill)
    {
        Vec2 corner;
        if (Start is Vec2 s)
        {
            if (!IsMoved(canvas, s, Current)) return;
            corner = s;
        }
        else if (TypedCorner is Vec2 t) corner = t;
        else return;

        var crossing = Current.X < corner.X;
        dc.DrawRectangle(crossing ? crossingFill : windowFill,
            crossing ? CadCanvas.PreviewDashPen : CadCanvas.PreviewPen,
            new Rect(canvas.WorldToScreen(corner), canvas.WorldToScreen(Current)));
    }
}
