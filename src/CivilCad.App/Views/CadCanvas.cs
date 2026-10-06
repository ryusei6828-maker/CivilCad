using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CivilCad.App.Tools;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Views;

/// <summary>
/// 作図領域。図形は scene、カーソル・プレビューは overlay に分けて描画し、
/// マウス移動時は overlay だけ描き直す。
/// 座標は double のまま「ビュー中心からの差」を取ってから画面座標にするので、
/// 平面直角座標のような大きな値でも描画精度が落ちない。
/// </summary>
public sealed class CadCanvas : FrameworkElement
{
    private const double PickTolerancePx = 6;
    private const double SnapTolerancePx = 12;

    private static readonly Brush Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
    private static readonly RgbColor DimColor = new(90, 90, 90);
    private static readonly Pen SnapPen = Freeze(new Pen(Brushes.Lime, 2));
    public static readonly Pen PreviewPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x66, 0xCC, 0xFF)), 1));
    public static readonly Pen PreviewDashPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x66, 0xCC, 0xFF)), 1) { DashStyle = DashStyles.Dash });

    private readonly DrawingVisual _scene = new();
    private readonly DrawingVisual _overlay = new();
    private readonly VisualCollection _visuals;
    private readonly Dictionary<(RgbColor, bool, int), Pen> _pens = [];
    private readonly Dictionary<RgbColor, Brush> _brushes = [];
    /// <summary>CAD の文字幅 (全角=高さ, 半角=高さ/2) に合う等幅フォント。</summary>
    private readonly Typeface _typeface = new("MS Gothic");

    private CadDocument _document = new();
    private CadTool _tool;
    private Point? _panStart;
    private Point? _mouseScreen;

    public CadCanvas()
    {
        _visuals = new VisualCollection(this) { _scene, _overlay };
        ClipToBounds = true;
        Cursor = Cursors.Arrow;
        SizeChanged += (_, _) => RedrawAll();
        _tool = new SelectTool();
        _tool.Attach(this);
    }

    public CadDocument Document
    {
        get => _document;
        set
        {
            _document.Changed -= OnDocumentChanged;
            _document = value;
            _document.Changed += OnDocumentChanged;
            _snapIndex = null;
            Selection.Clear();
            DocumentReplaced?.Invoke(this, EventArgs.Empty);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            RedrawAll();
        }
    }

    /// <summary>表示する図面が別の図面に替わった (シートタブの切り替え)。</summary>
    public event EventHandler? DocumentReplaced;

    /// <summary>表示位置と拡大率を設定する (シートごとの表示を戻すとき)。</summary>
    public void SetView(Vec2 center, double scale)
    {
        ViewCenter = center;
        Scale = Math.Clamp(scale, 1e-4, 1e5);
        RedrawAll();
    }

    public HashSet<Entity> Selection { get; } = [];
    public CadTool Tool => _tool;

    /// <summary>ビュー中心のワールド座標と拡大率 (1m あたりのピクセル数)。</summary>
    public Vec2 ViewCenter { get; private set; }
    public double Scale { get; private set; } = 2;

    public bool SnapEnabled { get; set; } = true;
    public bool OrthoEnabled { get; set; }

    /// <summary>スナップ・直交補正後のカーソル位置。</summary>
    public Vec2 CursorWorld { get; private set; }
    public SnapPoint? CurrentSnap { get; private set; }

    public event EventHandler? SelectionChanged;
    public event EventHandler? ToolChanged;
    public event EventHandler? PromptChanged;
    public event EventHandler? CursorMoved;

    /// <summary>作図領域で右クリックされた (右クリックメニューを出す)。</summary>
    public event EventHandler? ContextRequested;
    public event EventHandler<string>? Message;

    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];

    // ---- ツール・選択 ----

    public void SetTool(CadTool tool)
    {
        _tool = tool;
        tool.Attach(this);
        ToolChanged?.Invoke(this, EventArgs.Empty);
        PromptChanged?.Invoke(this, EventArgs.Empty);
        RedrawOverlay();
    }

    internal void RaisePromptChanged() => PromptChanged?.Invoke(this, EventArgs.Empty);
    public void ShowMessage(string text) => Message?.Invoke(this, text);

    public void SetSelection(IEnumerable<Entity> entities)
    {
        Selection.Clear();
        Selection.UnionWith(entities);
        OnSelectionChanged();
    }

    public void AddToSelection(IEnumerable<Entity> entities, bool remove = false)
    {
        if (remove) Selection.ExceptWith(entities);
        else Selection.UnionWith(entities);
        OnSelectionChanged();
    }

    public void ClearSelection()
    {
        if (Selection.Count == 0) return;
        Selection.Clear();
        OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        RedrawScene();
    }

    /// <summary>カーソル付近で最も近い編集可能な図形。</summary>
    public Entity? HitTest(Vec2 world)
    {
        var tol = PickTolerancePx / Scale;
        Entity? best = null;
        var bestD = tol;
        for (var i = Document.Entities.Count - 1; i >= 0; i--)
        {
            var e = Document.Entities[i];
            if (!Document.IsEditable(e) || !e.Bounds.Inflate(tol).Contains(world)) continue;
            var d = e.DistanceTo(world);
            if (d <= bestD) (best, bestD) = (e, d);
        }
        return best;
    }

    // ---- 表示範囲 ----

    public Point WorldToScreen(Vec2 w) =>
        new((w.X - ViewCenter.X) * Scale + ActualWidth / 2, ActualHeight / 2 - (w.Y - ViewCenter.Y) * Scale);

    public Vec2 ScreenToWorld(Point p) =>
        new(ViewCenter.X + (p.X - ActualWidth / 2) / Scale, ViewCenter.Y - (p.Y - ActualHeight / 2) / Scale);

    public Box2 VisibleWorldBox => Box2.FromPoints(ScreenToWorld(new Point(0, 0)), ScreenToWorld(new Point(ActualWidth, ActualHeight)));

    /// <summary>全体表示。大きく離れた図形は除いた範囲を表示し、除いた数を返す。</summary>
    public int ZoomExtents()
    {
        var box = Document.MainExtents(out var excluded);
        ZoomTo(box);
        return excluded;
    }

    /// <summary>指定範囲が画面いっぱい (9 割) に入るように表示する。</summary>
    public void ZoomTo(Box2 box)
    {
        if (box.IsEmpty || ActualWidth <= 0) return;
        ViewCenter = box.Center;
        var w = Math.Max(box.Width, 1e-3);
        var h = Math.Max(box.Height, 1e-3);
        Scale = Math.Clamp(Math.Min(ActualWidth / w, ActualHeight / h) * 0.9, 1e-4, 1e5);
        RedrawAll();
    }

    public void ZoomAt(Point screen, double factor)
    {
        var before = ScreenToWorld(screen);
        Scale = Math.Clamp(Scale * factor, 1e-4, 1e5);
        var after = ScreenToWorld(screen);
        ViewCenter += before - after;
        RedrawAll();
    }

    // ---- マウス ----

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        var p = e.GetPosition(this);
        switch (e.ChangedButton)
        {
            case MouseButton.Middle when e.ClickCount == 2:
                ZoomExtents();
                break;
            case MouseButton.Middle:
                _panStart = p;
                CaptureMouse();
                break;
            case MouseButton.Left:
                UpdateCursor(p);
                _tool.OnPress(CursorWorld);
                if (_tool.IsDragging) CaptureMouse();
                RedrawOverlay();
                break;
            case MouseButton.Right:
                // 右クリックはコマンドの操作メニュー (一つ戻す・やり直しなど) を出す。中身は MainWindow が作る
                ContextRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.ChangedButton == MouseButton.Middle && _panStart != null)
        {
            _panStart = null;
            if (!_tool.IsDragging) ReleaseMouseCapture();
        }
        else if (e.ChangedButton == MouseButton.Left)
        {
            UpdateCursor(e.GetPosition(this));
            _tool.OnRelease(CursorWorld);
            if (_panStart == null && IsMouseCaptured) ReleaseMouseCapture();
            RedrawOverlay();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        if (_panStart is Point start)
        {
            var d = p - start;
            ViewCenter -= new Vec2(d.X / Scale, -d.Y / Scale);
            _panStart = p;
            RedrawAll();
        }
        UpdateCursor(p);
        _tool.OnMouseMove(CursorWorld);
        CursorMoved?.Invoke(this, EventArgs.Empty);
        RedrawOverlay();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _mouseScreen = null;
        RedrawOverlay();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        ZoomAt(e.GetPosition(this), e.Delta > 0 ? 1.25 : 1 / 1.25);
        UpdateCursor(e.GetPosition(this));
        e.Handled = true;
    }

    private void UpdateCursor(Point screen)
    {
        _mouseScreen = screen;
        var raw = ScreenToWorld(screen);
        CurrentSnap = SnapEnabled && _tool.WantsSnap ? FindSnap(raw) : null;
        if (CurrentSnap is SnapPoint sp)
        {
            CursorWorld = sp.Point;
        }
        else if (OrthoEnabled && _tool.BasePoint is Vec2 bp)
        {
            var d = raw - bp;
            CursorWorld = Math.Abs(d.X) >= Math.Abs(d.Y) ? new Vec2(raw.X, bp.Y) : new Vec2(bp.X, raw.Y);
        }
        else
        {
            CursorWorld = raw;
        }
        CursorWorld = _tool.ConstrainCursor(CursorWorld);
    }

    /// <summary>有効なスナップの種類 (設定で切り替え)。</summary>
    public HashSet<SnapKind> SnapKinds { get; } = [.. SnapFinder.DefaultKinds];

    /// <summary>表示中の図形と外形の一覧。図面が変わるたびに作り直す (マウス移動ごとに外形を計算しないため)。</summary>
    private List<(Entity Entity, Box2 Bounds)>? _snapIndex;

    private SnapPoint? FindSnap(Vec2 raw)
    {
        _snapIndex ??= Document.Entities.Where(Document.IsVisible).Select(e => (e, e.Bounds)).ToList();
        var tol = SnapTolerancePx / Scale;
        var near = new List<Entity>();
        foreach (var (e, b) in _snapIndex)
            if (raw.X >= b.Min.X - tol && raw.X <= b.Max.X + tol && raw.Y >= b.Min.Y - tol && raw.Y <= b.Max.Y + tol)
                near.Add(e);
        return SnapFinder.FindAmong(near, raw, tol, _tool.BasePoint, SnapKinds);
    }

    // ---- 描画 ----

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        _snapIndex = null;
        // 削除・非表示・ロックされた図形は選択から外す
        var before = Selection.Count;
        Selection.RemoveWhere(x => !Document.Entities.Contains(x) || !Document.IsEditable(x));
        if (Selection.Count != before) SelectionChanged?.Invoke(this, EventArgs.Empty);
        RedrawScene();
    }

    protected override void OnRender(DrawingContext dc) => dc.DrawRectangle(Background, null, new Rect(RenderSize));

    public void RedrawAll()
    {
        RedrawScene();
        RedrawOverlay();
    }

    public void RedrawScene()
    {
        using var dc = _scene.RenderOpen();
        if (ActualWidth <= 0) return;
        var view = VisibleWorldBox;
        // 塗りつぶしは先 (下) に描き、線や文字が塗りに隠れないようにする
        foreach (var fillPass in new[] { true, false })
        {
            foreach (var e in Document.Entities)
            {
                if (IsFill(e) != fillPass || !Document.IsVisible(e) || !e.Bounds.Intersects(view)) continue;
                var dim = Document.OnlyCurrentLayerEditable && e.Layer != Document.CurrentLayer;
                var color = dim ? DimColor : e.EffectiveColor;
                DrawEntity(dc, e, color, Selection.Contains(e));
            }
        }
    }

    private static bool IsFill(Entity e) => e is HatchEntity { Solid: true } or PolylineEntity { Filled: true };

    public void RedrawOverlay()
    {
        using var dc = _overlay.RenderOpen();
        _tool.DrawPreview(dc);

        if (CurrentSnap is SnapPoint sp && _mouseScreen != null) DrawSnapMarker(dc, sp);
    }

    public void DrawEntity(DrawingContext dc, Entity e, RgbColor color, bool selected = false)
    {
        var pen = GetPen(color, selected, e.LineWeight);
        switch (e)
        {
            case LineEntity l:
                dc.DrawLine(pen, WorldToScreen(l.Start), WorldToScreen(l.End));
                break;
            case PolylineEntity pl when pl.Points.Count > 1:
                dc.DrawGeometry(pl.Filled ? GetBrush(color) : null, pen, PolylineGeometry(pl.Points, pl.IsClosed || pl.Filled));
                break;
            case CircleEntity c:
                dc.DrawEllipse(null, pen, WorldToScreen(c.Center), c.Radius * Scale, c.Radius * Scale);
                break;
            case ArcEntity a:
                dc.DrawGeometry(null, pen, ArcGeometry(a));
                break;
            case TextEntity t:
                DrawText(dc, t, color, selected);
                break;
            case HatchEntity h:
                DrawHatch(dc, h, color, selected, pen);
                break;
        }
    }

    /// <summary>
    /// ハッチング: 線 (または塗り) を 1 つの図形として描く。透明度があれば透かして描く。
    /// 選択中は境界も (透かさずに) 描く。
    /// </summary>
    private void DrawHatch(DrawingContext dc, HatchEntity h, RgbColor color, bool selected, Pen pen)
    {
        StreamGeometry? outline = null;
        if (h.Solid || selected)
        {
            outline = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (var ctx = outline.Open())
            {
                foreach (var loop in h.Loops)
                {
                    ctx.BeginFigure(WorldToScreen(loop[0]), h.Solid, true);
                    for (var i = 1; i < loop.Count; i++) ctx.LineTo(WorldToScreen(loop[i]), true, false);
                }
            }
            outline.Freeze();
        }

        var translucent = h.Opacity < 1;
        if (translucent) dc.PushOpacity(h.Opacity);
        if (h.Solid)
        {
            dc.DrawGeometry(GetBrush(color), null, outline);
        }
        else if (h.Lines.Count > 0)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                foreach (var (a, b) in h.Lines)
                {
                    ctx.BeginFigure(WorldToScreen(a), false, false);
                    ctx.LineTo(WorldToScreen(b), true, false);
                }
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }
        if (translucent) dc.Pop();
        if (selected) dc.DrawGeometry(null, pen, outline);
    }

    public void DrawWorldLine(DrawingContext dc, Pen pen, Vec2 a, Vec2 b) => dc.DrawLine(pen, WorldToScreen(a), WorldToScreen(b));

    public void DrawWorldPolyline(DrawingContext dc, Pen pen, IReadOnlyList<Vec2> pts, bool closed)
    {
        if (pts.Count > 1) dc.DrawGeometry(null, pen, PolylineGeometry(pts, closed));
    }

    private StreamGeometry PolylineGeometry(IReadOnlyList<Vec2> pts, bool closed)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(WorldToScreen(pts[0]), closed, closed);
            for (var i = 1; i < pts.Count; i++) ctx.LineTo(WorldToScreen(pts[i]), true, false);
        }
        g.Freeze();
        return g;
    }

    private StreamGeometry ArcGeometry(ArcEntity a)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(WorldToScreen(a.StartPoint), false, false);
            var r = a.Radius * Scale;
            ctx.ArcTo(WorldToScreen(a.EndPoint), new Size(r, r), 0, a.Sweep > Math.PI, SweepDirection.Counterclockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    private void DrawText(DrawingContext dc, TextEntity t, RgbColor color, bool selected)
    {
        var em = t.Height * Scale;
        if (em < 2)
        {
            // 小さすぎる文字は線で代用
            var start = t.BaselineStart(t.ApproxWidth);
            dc.DrawLine(GetPen(color, selected), WorldToScreen(start),
                WorldToScreen(start + Vec2.FromAngle(t.Rotation) * t.ApproxWidth));
            return;
        }

        var ft = new FormattedText(t.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, _typeface, em,
            GetBrush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var naturalWidth = Math.Max(ft.WidthIncludingTrailingWhitespace, 1e-6);
        var xScale = t.FitWidth is double fit ? fit * Scale / naturalWidth : t.WidthFactor;
        var widthPx = naturalWidth * xScale;

        // 配置基準 (中央・右寄せ・上端など) から文字列の左下を求めて描く
        var p = WorldToScreen(t.BaselineStart(widthPx / Scale));
        dc.PushTransform(new RotateTransform(-t.Rotation * 180 / Math.PI, p.X, p.Y));
        dc.PushTransform(new ScaleTransform(xScale, 1, p.X, p.Y));
        // 全角文字の枠 (高さ = 文字高) の下端を基準点に合わせる (Jw_cad 等と同じ見え方)
        dc.DrawText(ft, new Point(p.X, p.Y - ft.Height));
        dc.Pop();
        if (selected) dc.DrawRectangle(null, GetPen(color, true), new Rect(p.X, p.Y - em, widthPx, em));
        dc.Pop();
    }

    private void DrawSnapMarker(DrawingContext dc, SnapPoint sp)
    {
        var c = WorldToScreen(sp.Point);
        const double s = 6;
        switch (sp.Kind)
        {
            case SnapKind.Endpoint:
                dc.DrawRectangle(null, SnapPen, new Rect(c.X - s, c.Y - s, s * 2, s * 2));
                break;
            case SnapKind.Midpoint:
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(c.X, c.Y - s), false, true);
                    ctx.LineTo(new Point(c.X + s, c.Y + s), true, false);
                    ctx.LineTo(new Point(c.X - s, c.Y + s), true, false);
                }
                dc.DrawGeometry(null, SnapPen, g);
                break;
            case SnapKind.Center:
                dc.DrawEllipse(null, SnapPen, c, s, s);
                break;
            case SnapKind.Quadrant:
                // ◇
                var q = new StreamGeometry();
                using (var ctx = q.Open())
                {
                    ctx.BeginFigure(new Point(c.X, c.Y - s), false, true);
                    ctx.LineTo(new Point(c.X + s, c.Y), true, false);
                    ctx.LineTo(new Point(c.X, c.Y + s), true, false);
                    ctx.LineTo(new Point(c.X - s, c.Y), true, false);
                }
                dc.DrawGeometry(null, SnapPen, q);
                break;
            case SnapKind.Perpendicular:
                // ⊥
                dc.DrawLine(SnapPen, new Point(c.X - s, c.Y + s), new Point(c.X + s, c.Y + s));
                dc.DrawLine(SnapPen, new Point(c.X - s, c.Y + s), new Point(c.X - s, c.Y - s));
                dc.DrawLine(SnapPen, new Point(c.X - s, c.Y), new Point(c.X, c.Y));
                dc.DrawLine(SnapPen, new Point(c.X, c.Y), new Point(c.X, c.Y + s));
                break;
            case SnapKind.Tangent:
                // ○ に接線
                dc.DrawEllipse(null, SnapPen, c, s * 0.8, s * 0.8);
                dc.DrawLine(SnapPen, new Point(c.X - s, c.Y - s * 0.8), new Point(c.X + s, c.Y - s * 0.8));
                break;
            case SnapKind.Nearest:
                // 砂時計
                var h = new StreamGeometry();
                using (var ctx = h.Open())
                {
                    ctx.BeginFigure(new Point(c.X - s, c.Y - s), false, true);
                    ctx.LineTo(new Point(c.X + s, c.Y - s), true, false);
                    ctx.LineTo(new Point(c.X - s, c.Y + s), true, false);
                    ctx.LineTo(new Point(c.X + s, c.Y + s), true, false);
                }
                dc.DrawGeometry(null, SnapPen, h);
                break;
            default:
                // × (交点・挿入点)
                dc.DrawLine(SnapPen, new Point(c.X - s, c.Y - s), new Point(c.X + s, c.Y + s));
                dc.DrawLine(SnapPen, new Point(c.X - s, c.Y + s), new Point(c.X + s, c.Y - s));
                break;
        }

        // 種類の名前をマーカーの右下に出す
        var label = new FormattedText(SnapFinder.Label(sp.Kind), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Yu Gothic UI"), 12, Brushes.Lime, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawRectangle(SnapLabelBackground, null, new Rect(c.X + s + 4, c.Y + s + 2, label.Width + 6, label.Height + 2));
        dc.DrawText(label, new Point(c.X + s + 7, c.Y + s + 3));
    }

    private static readonly Brush SnapLabelBackground = Frozen(new SolidColorBrush(Color.FromArgb(200, 20, 20, 20)));

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }

    /// <summary>
    /// 線幅 (mm) → 画面の太さ (1/4 px 単位)。1mm ≒ 3px とし、細線 (未指定・0.13mm) は 1px。
    /// 0.25 / 0.35 / 0.5 / 0.7 … の違いが画面でも分かるようにする。
    /// </summary>
    public static int WeightQuarterPixels(double? weightMm) =>
        (int)Math.Round(Math.Clamp((weightMm ?? 0) * 3.0, 1.0, 8.0) * 4);

    private Pen GetPen(RgbColor color, bool selected, double? weightMm = null)
    {
        var q = WeightQuarterPixels(weightMm);
        if (_pens.TryGetValue((color, selected, q), out var pen)) return pen;
        var px = q / 4.0;
        pen = new Pen(GetBrush(color), selected ? Math.Max(2, px) : px)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        if (selected) pen.DashStyle = DashStyles.Dash;
        pen.Freeze();
        return _pens[(color, selected, q)] = pen;
    }

    private Brush GetBrush(RgbColor color)
    {
        if (_brushes.TryGetValue(color, out var brush)) return brush;
        brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return _brushes[color] = brush;
    }

    private static Pen Freeze(Pen pen)
    {
        pen.Freeze();
        return pen;
    }
}
