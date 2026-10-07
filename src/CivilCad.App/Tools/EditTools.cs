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

/// <summary>
/// 拡大縮小: 選択した図形を基点を中心に拡大・縮小する。
/// 基点 → 倍率を入力 (2 = 2 倍、0.5 = 半分)、または 参照: 元の長さを 2 点で指定 → 新しい長さを入力 / クリック。
/// Esc を押すまで続けて同じ図形を拡大縮小できる。
/// </summary>
public sealed class ScaleTool : CadTool
{
    private const int MaxPreviewEntities = 300;
    private static readonly RgbColor GhostColor = new(0x66, 0xCC, 0xFF);
    private List<Entity> _targets = [];
    private Vec2? _base;
    private Vec2? _ref1;
    private double? _refLength;
    private Vec2 _cursor;

    public override string Name => "拡大縮小";
    public override bool RequiresSelection => true;
    public override bool WantsTextInput => _base != null;
    public override bool CanStepBack => _base != null;

    /// <summary>参照の始点を決めたあとは、そこからの長さを測る。</summary>
    public override Vec2? BasePoint => _ref1 ?? _base;

    protected override void OnActivated()
    {
        _targets = [.. Doc.Entities.Where(Canvas.Selection.Contains)];
        UpdatePrompt();
    }

    private void UpdatePrompt() => SetPrompt(
        _base == null ? $"拡大縮小 ({_targets.Count} 個): 基点 (動かない点) を指定"
        : _ref1 == null ? "倍率を入力 (2 = 2 倍、0.5 = 半分)、または 参照する長さの始点をクリック"
        : _refLength == null ? "参照: 元の長さの終点を指定"
        : $"参照: 新しい長さを入力 (m)、またはクリック (元の長さ {_refLength:0.###}m)");

    public override void StepBack()
    {
        if (_refLength != null) _refLength = null;
        else if (_ref1 != null) _ref1 = null;
        else _base = null;
        UpdatePrompt();
    }

    public override void OnPoint(Vec2 p)
    {
        if (_base == null) _base = p;
        else if (_ref1 == null) _ref1 = p;
        else if (_refLength is not double refLen)
        {
            var len = (p - _ref1.Value).Length;
            if (len < 1e-9)
            {
                Canvas.ShowMessage("拡大縮小: 元の長さが 0 です。別の点を指定してください。");
                return;
            }
            _refLength = len;
        }
        else
        {
            Apply((p - _ref1.Value).Length / refLen);
            return;
        }
        UpdatePrompt();
    }

    public override bool OnText(string text)
    {
        if (_base == null || !TryParseNumber(text, out var v)) return false;
        if (_ref1 == null) Apply(v);                                  // 倍率
        else if (_refLength is double refLen) Apply(v / refLen);       // 新しい長さ
        else return false;
        return true;
    }

    private void Apply(double k)
    {
        if (!(k > 0) || double.IsInfinity(k))
        {
            Canvas.ShowMessage("拡大縮小: 倍率は 0 より大きい値にしてください。");
            return;
        }
        var origin = _base!.Value;
        var pairs = _targets.Where(Doc.Entities.Contains).Select(e =>
        {
            var c = e.Clone();
            c.Scale(origin, k);
            return (Old: e, New: c);
        }).ToList();
        Execute(new ReplaceEntitiesCommand(pairs, $"拡大縮小 ×{k:0.####}"));
        _targets = [.. pairs.Select(p => p.New)];
        Canvas.SetSelection(_targets);
        Canvas.ShowMessage($"拡大縮小: {pairs.Count} 個を {k:0.####} 倍にしました。");
        // 続けて同じ図形を拡大縮小できる (Esc で終了)
        _base = null;
        _ref1 = null;
        _refLength = null;
        SetPrompt($"続けて拡大縮小 ({_targets.Count} 個): 基点を指定 (Esc で終了)");
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void DrawPreview(DrawingContext dc)
    {
        if (_base is not Vec2 b) return;
        if (_ref1 is not Vec2 r1)
        {
            Canvas.DrawWorldLine(dc, CadCanvas.PreviewDashPen, b, _cursor);
            return;
        }
        Canvas.DrawWorldLine(dc, CadCanvas.PreviewDashPen, r1, _cursor);
        if (_refLength is not double refLen) return;
        var k = (_cursor - r1).Length / refLen;
        if (k <= 0) return;
        if (_targets.Count > MaxPreviewEntities)
        {
            var box = _targets.Aggregate(Box2.Empty, (acc, e) => acc.Union(e.Bounds));
            Vec2 S(Vec2 p) => b + (p - b) * k;
            Canvas.DrawWorldPolyline(dc, CadCanvas.PreviewPen,
                [S(box.Min), S(new Vec2(box.Max.X, box.Min.Y)), S(box.Max), S(new Vec2(box.Min.X, box.Max.Y))], true);
            return;
        }
        foreach (var e in _targets)
        {
            var ghost = e.Clone();
            ghost.Scale(b, k);
            Canvas.DrawEntity(dc, ghost, GhostColor);
        }
    }
}

/// <summary>
/// 2 点指示 (位置合わせ): 選択した図形 (画像など) の上の 2 点を、図面上の 2 点に合わせる。
/// 元の 1 点目 → 合わせ先の 1 点目 → 元の 2 点目 → 合わせ先の 2 点目 の順に指定すると、
/// 1 点目どうしが重なるよう移動し、ScaleToFit なら 2 点間の長さも合わせる (拡大縮小)。
/// 向きはそのまま (回転しない)。RotateToFit なら 2 点目の方向へ回転もする。
/// </summary>
public sealed class AlignTool : CadTool
{
    private const int MaxPreviewEntities = 300;
    private static readonly RgbColor GhostColor = new(0x66, 0xCC, 0xFF);

    /// <summary>2 点間の長さも合わせる (拡大縮小する)。次の 2 点指示でも同じ設定を使う。</summary>
    public static bool ScaleToFit { get; set; } = true;

    /// <summary>2 点目の方向へ回転もする。既定はオフ (向きはそのまま)。次の 2 点指示でも同じ設定を使う。</summary>
    public static bool RotateToFit { get; set; }

    private List<Entity> _targets = [];
    private readonly List<Vec2> _points = [];
    private Vec2 _cursor;

    public override string Name => "2 点指示";
    public override bool RequiresSelection => true;
    public override bool CanStepBack => _points.Count > 0;
    public override Vec2? BasePoint => _points.Count is 1 or 3 ? _points[^1] : null;

    public override IEnumerable<(string Label, Action Run)> MenuItems =>
    [
        (ScaleToFit ? "大きさも合わせる: オン → オフにする" : "大きさも合わせる: オフ → オンにする (拡大縮小もする)", () =>
        {
            ScaleToFit = !ScaleToFit;
            UpdatePrompt();
            Canvas.RedrawOverlay();
        }),
        (RotateToFit ? "回転も合わせる: オン → オフにする (向きはそのまま)" : "回転も合わせる: オフ → オンにする (2 点目の方向へ回転する)", () =>
        {
            RotateToFit = !RotateToFit;
            UpdatePrompt();
            Canvas.RedrawOverlay();
        }),
    ];

    protected override void OnActivated()
    {
        _targets = [.. Doc.Entities.Where(Canvas.Selection.Contains)];
        UpdatePrompt();
    }

    private static string Mode => "移動" + (ScaleToFit ? "・大きさ" : "") + (RotateToFit ? "・回転" : "、回転しない");

    private void UpdatePrompt() => SetPrompt(_points.Count switch
    {
        0 => $"2 点指示 ({_targets.Count} 個, {Mode}): 合わせる元の 1 点目 (画像の上の点) を指定",
        1 => "1 点目の合わせ先 (図面上の点) を指定",
        2 => "合わせる元の 2 点目 (画像の上の点) を指定",
        _ => $"2 点目の合わせ先 (図面上の点) を指定 [{Mode}、右クリックで切替]",
    });

    public override void StepBack()
    {
        if (_points.Count > 0) _points.RemoveAt(_points.Count - 1);
        UpdatePrompt();
    }

    public override void OnPoint(Vec2 p)
    {
        if (_points.Count == 2 && (p - _points[0]).Length < 1e-9)
        {
            Canvas.ShowMessage("2 点指示: 2 点目は 1 点目と別の点を指定してください。");
            return;
        }
        _points.Add(p);
        if (_points.Count < 4)
        {
            UpdatePrompt();
            return;
        }
        if ((_points[3] - _points[1]).Length < 1e-9)
        {
            _points.RemoveAt(3);
            Canvas.ShowMessage("2 点指示: 合わせ先の 2 点目は 1 点目と別の点を指定してください。");
            return;
        }
        var (rad, k) = Transform(_points[0], _points[1], _points[2], _points[3]);
        var pairs = _targets.Where(Doc.Entities.Contains).Select(e => (Old: e, New: Apply(e, _points[0], _points[1], rad, k))).ToList();
        Execute(new ReplaceEntitiesCommand(pairs, "2 点指示"));
        _targets = [.. pairs.Select(x => x.New)];
        Canvas.SetSelection(_targets);
        Canvas.ShowMessage($"2 点指示: {pairs.Count} 個を " + (RotateToFit ? $"回転 {rad * 180 / Math.PI:0.####}°" : "回転せず") +
                           (ScaleToFit ? $"、{k:0.######} 倍" : "") + " で合わせました。" +
                           (!RotateToFit && ScaleToFit ? " (1 点目を合わせ、2 点間の長さで大きさを合わせました。向きを合わせるには右クリックで「回転も合わせる」)" : ""));
        // 続けて合わせ直せる (Esc で終了)
        _points.Clear();
        UpdatePrompt();
    }

    /// <summary>元の 2 点 (s1, s2) を合わせ先 (d1, d2) に合わせる回転角と倍率。</summary>
    public static (double Rad, double K) Transform(Vec2 s1, Vec2 d1, Vec2 s2, Vec2 d2)
    {
        var vs = s2 - s1;
        var vd = d2 - d1;
        var rad = RotateToFit ? vd.Angle - vs.Angle : 0;
        var k = ScaleToFit && vs.Length > 0 ? vd.Length / vs.Length : 1;
        return (rad, k);
    }

    private static Entity Apply(Entity e, Vec2 s1, Vec2 d1, double rad, double k)
    {
        var c = e.Clone();
        c.Translate(d1 - s1);
        c.Rotate(d1, rad);
        if (Math.Abs(k - 1) > 1e-15) c.Scale(d1, k);
        return c;
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void DrawPreview(DrawingContext dc)
    {
        // 指定済みの組を矢印代わりの線で示す (元 → 合わせ先)
        for (var i = 0; i + 1 < _points.Count; i += 2)
            Canvas.DrawWorldLine(dc, CadCanvas.PreviewPen, _points[i], _points[i + 1]);
        if (BasePoint is Vec2 b) Canvas.DrawWorldLine(dc, CadCanvas.PreviewDashPen, b, _cursor);
        if (_points.Count != 3 || (_cursor - _points[1]).Length < 1e-9) return;

        var (rad, k) = Transform(_points[0], _points[1], _points[2], _cursor);
        if (_targets.Count > MaxPreviewEntities)
        {
            var box = _targets.Aggregate(Box2.Empty, (acc, e) => acc.Union(e.Bounds));
            Vec2 T(Vec2 p) => _points[1] + (p - _points[0]).Rotate(rad) * k;
            Canvas.DrawWorldPolyline(dc, CadCanvas.PreviewPen,
                [T(box.Min), T(new Vec2(box.Max.X, box.Min.Y)), T(box.Max), T(new Vec2(box.Min.X, box.Max.Y))], true);
            return;
        }
        foreach (var e in _targets)
        {
            // 画像は枠だけ (中身まで描くと重い)
            if (e is ImageEntity img)
            {
                var c = img.Corners.Select(p => _points[1] + (p - _points[0]).Rotate(rad) * k).ToList();
                Canvas.DrawWorldPolyline(dc, CadCanvas.PreviewPen, c, true);
                continue;
            }
            Canvas.DrawEntity(dc, Apply(e, _points[0], _points[1], rad, k), GhostColor);
        }
    }
}
