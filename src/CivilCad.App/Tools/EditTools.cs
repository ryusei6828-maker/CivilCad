using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>
/// 削除モード: クリックで消す図形を選び (もう一度クリックで解除、ドラッグで範囲内をまとめて選ぶ)、
/// ダブルクリック (または Enter) で選んだ図形を削除する。1 回のクリックでは消えない。
/// Esc は選んだものの解除、何も選んでいなければ終了。
/// </summary>
public sealed class EraseTool : CadTool
{
    private static readonly RgbColor MarkColor = new(255, 80, 80);
    private static readonly RgbColor HoverColor = new(255, 170, 170);
    private static readonly Brush WindowFill = Frozen(new SolidColorBrush(Color.FromArgb(40, 255, 80, 80)));

    private readonly BoxDrag _drag = new();
    private readonly List<Entity> _marked = [];
    private Entity? _hover;

    public override string Name => "削除";
    public override bool WantsSnap => false;
    public override bool IsDragging => _drag.IsMouseDrag;
    public override bool CanStepBack => _marked.Count > 0;
    public override string? ConfirmLabel => _marked.Count > 0 ? $"{_marked.Count} 個を削除 (ダブルクリック / Enter)" : null;

    /// <summary>削除する図形として選んでいるもの。</summary>
    public IReadOnlyList<Entity> Marked => _marked;

    protected override void OnActivated() => SetPrompt(_marked.Count == 0
        ? $"消す図形をクリックで選ぶ (ドラッグで{BoxDrag.ModeText}をまとめて) → ダブルクリックで削除 / Esc で終了"
        : $"{_marked.Count} 個を選択中: ダブルクリック (または Enter) で削除 / クリックで追加・解除 / Esc で選び直し");

    public override void OnPress(Vec2 p) => _drag.Press(p);

    public override void OnRelease(Vec2 p)
    {
        if (_drag.Release(Canvas, p) is not var (start, moved)) return;
        if (moved) Mark(BoxDrag.Hits(Doc, start, p));
        else if (Canvas.HitTest(start) is { } hit) Toggle(hit);
        _hover = Canvas.HitTest(p);
    }

    /// <summary>ダブルクリック: その図形も含めて、選んだ図形を削除する (1 回目のクリックで解除されていても消す)。</summary>
    public override void OnDoubleClick(Vec2 p)
    {
        if (Canvas.HitTest(p) is { } hit && !_marked.Contains(hit)) _marked.AddRange(Grouping.Expand(Doc, [hit]).Where(x => !_marked.Contains(x)));
        Confirm();
    }

    public override void OnEnter() => Confirm();

    /// <summary>コマンド欄からの座標入力: 2 点の範囲内を選ぶ。</summary>
    public override void OnPoint(Vec2 p)
    {
        if (_drag.TypedCorner is not Vec2 corner)
        {
            _drag.TypedCorner = p;
            SetPrompt("範囲のもう一方の角を指定 (範囲内の図形を選ぶ)");
            return;
        }
        Mark(BoxDrag.Hits(Doc, corner, p));
        _drag.TypedCorner = null;
    }

    /// <summary>グループの図形は、同じグループの図形もまとめて選ぶ・外す。</summary>
    private void Toggle(Entity e)
    {
        var group = Grouping.Expand(Doc, [e]);
        if (_marked.Contains(e)) _marked.RemoveAll(group.Contains);
        else _marked.AddRange(group.Where(x => !_marked.Contains(x)));
        OnActivated();
    }

    private void Mark(List<Entity> hits)
    {
        foreach (var e in Grouping.Expand(Doc, hits))
            if (!_marked.Contains(e)) _marked.Add(e);
        OnActivated();
    }

    private void Confirm()
    {
        var targets = _marked.Where(Doc.Entities.Contains).ToList();
        _marked.Clear();
        if (targets.Count > 0)
        {
            Execute(new RemoveEntitiesCommand(targets, $"削除 ({targets.Count} 個)"));
            Canvas.ShowMessage($"{targets.Count} 個の図形を削除しました。");
        }
        OnActivated();
    }

    public override void StepBack()
    {
        if (_marked.Count > 0) _marked.RemoveAt(_marked.Count - 1);
        OnActivated();
    }

    public override void OnMouseMove(Vec2 p)
    {
        _drag.Current = p;
        _hover = _drag.IsActive ? null : Canvas.HitTest(p);
    }

    public override void OnCancel()
    {
        if (_drag.IsActive) _drag.Reset();
        else if (_marked.Count > 0) _marked.Clear();
        else
        {
            Finish();
            return;
        }
        OnActivated();
    }

    public override void DrawPreview(DrawingContext dc)
    {
        // 選んだ図形は赤、カーソル下の図形は薄い赤で強調
        foreach (var e in _marked)
            if (Doc.Entities.Contains(e)) Canvas.DrawEntity(dc, e, MarkColor, true);
        if (_hover != null && !_marked.Contains(_hover) && Doc.Entities.Contains(_hover)) Canvas.DrawEntity(dc, _hover, HoverColor);
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
            var clones = Grouping.RenewGroups(_targets.Select(e => e.Clone()).ToList());
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

/// <summary>拡大縮小のやり方。</summary>
public enum ScaleMode
{
    /// <summary>倍率を指定: 基点 → 倍率 (または 参照する長さ → 新しい長さ)。</summary>
    Factor,

    /// <summary>2 点を合わせる: 選んだ図形の 2 点 → 合わせ先の 2 点。1 点目どうしを重ね、2 点間の長さを合わせる。</summary>
    MatchTwoPoints,
}

/// <summary>
/// 拡大縮小: 選択した図形を拡大・縮小する。やり方は右のプロパティで切り替える (<see cref="ScaleMode"/>)。
/// 倍率を指定: 基点 → 倍率を入力 (2 = 2 倍、0.5 = 半分)、または 参照: 元の長さを 2 点で指定 → 新しい長さを入力 / クリック。
///   先にプロパティで倍率を入れておくと、基点をクリックしただけでその倍率にする。
/// 2 点を合わせる: 選んだ図形の上の 2 点 → 合わせ先 (別の図形の上) の 2 点。1 点目どうしが重なり、2 点間の長さが同じになる。
///   向きはそのまま (RotateToMatch なら 2 点目の方向へ回転もする)。
/// Esc を押すまで続けて同じ図形を拡大縮小できる。
/// </summary>
public sealed class ScaleTool : CadTool
{
    private const int MaxPreviewEntities = 300;
    private static readonly RgbColor GhostColor = new(0x66, 0xCC, 0xFF);

    /// <summary>やり方 (次の拡大縮小でも同じ)。</summary>
    public static ScaleMode Mode { get; set; } = ScaleMode.Factor;

    /// <summary>プロパティで入れた倍率。入っていれば基点をクリックしただけでこの倍率にする。</summary>
    public static double? FixedFactor { get; set; }

    /// <summary>2 点を合わせるとき、2 点目の方向へ回転もする。</summary>
    public static bool RotateToMatch { get; set; }

    private List<Entity> _targets = [];
    private Vec2? _base;
    private Vec2? _ref1;
    private double? _refLength;
    private readonly List<Vec2> _match = []; // 元の 1 点目, 元の 2 点目, 合わせ先の 1 点目
    private Vec2 _cursor;

    public override string Name => "拡大縮小";
    public override bool RequiresSelection => true;
    public override bool WantsTextInput => Mode == ScaleMode.Factor && _base != null;
    public override bool CanStepBack => _base != null || _match.Count > 0;

    /// <summary>参照の始点を決めたあとは、そこからの長さを測る。2 点を合わせるときは直前の点。</summary>
    public override Vec2? BasePoint => Mode == ScaleMode.MatchTwoPoints ? (_match.Count > 0 ? _match[^1] : null) : _ref1 ?? _base;

    /// <summary>基点を指定済みか (倍率を入れるとすぐ拡大縮小する)。</summary>
    public bool HasBasePoint => _base != null;

    /// <summary>2 点を合わせるときに指定済みの点の数 (0〜3)。</summary>
    public int MatchPointCount => _match.Count;

    protected override void OnActivated()
    {
        _targets = [.. Doc.Entities.Where(Canvas.Selection.Contains)];
        UpdatePrompt();
    }

    /// <summary>やり方を変える (指定途中の点は捨てる)。</summary>
    public void SetMode(ScaleMode mode)
    {
        Mode = mode;
        ResetPoints();
        UpdatePrompt();
        Canvas.RedrawOverlay();
    }

    /// <summary>プロパティで倍率を入れた。基点が決まっていればすぐ拡大縮小、まだなら基点のクリックで拡大縮小する。</summary>
    public bool SetFactor(double k)
    {
        if (!(k > 0) || double.IsInfinity(k))
        {
            Canvas.ShowMessage("拡大縮小: 倍率は 0 より大きい値にしてください。");
            return false;
        }
        FixedFactor = k;
        if (Mode == ScaleMode.Factor && _base != null) Apply(k);
        else UpdatePrompt();
        return true;
    }

    private void ResetPoints()
    {
        _base = null;
        _ref1 = null;
        _refLength = null;
        _match.Clear();
    }

    private void UpdatePrompt() => SetPrompt(Mode == ScaleMode.MatchTwoPoints
        ? _match.Count switch
        {
            0 => $"拡大縮小 ({_targets.Count} 個, 2 点を合わせる): 合わせる元の 1 点目 (選んだ図形の上の点) を指定",
            1 => "合わせる元の 2 点目を指定",
            2 => "合わせ先の 1 点目 (別の図形の上の点) を指定",
            _ => $"合わせ先の 2 点目を指定 ({(RotateToMatch ? "向きも合わせる" : "向きはそのまま")})",
        }
        : _base == null
            ? $"拡大縮小 ({_targets.Count} 個): 基点 (動かない点) を指定" + (FixedFactor is double f ? $" → {f:0.####} 倍にする" : "")
            : _ref1 == null ? "倍率を入力 (2 = 2 倍、0.5 = 半分。右のプロパティでも可)、または 参照する長さの始点をクリック"
            : _refLength == null ? "参照: 元の長さの終点を指定"
            : $"参照: 新しい長さを入力 (m)、またはクリック (元の長さ {_refLength:0.###}m)");

    public override void StepBack()
    {
        if (Mode == ScaleMode.MatchTwoPoints)
        {
            if (_match.Count > 0) _match.RemoveAt(_match.Count - 1);
        }
        else if (_refLength != null) _refLength = null;
        else if (_ref1 != null) _ref1 = null;
        else _base = null;
        UpdatePrompt();
    }

    public override void OnPoint(Vec2 p)
    {
        if (Mode == ScaleMode.MatchTwoPoints)
        {
            OnMatchPoint(p);
            return;
        }
        if (_base == null)
        {
            _base = p;
            if (FixedFactor is double fixedK)
            {
                Apply(fixedK);
                return;
            }
        }
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

    private void OnMatchPoint(Vec2 p)
    {
        // 2 点目は 1 点目と別の点
        if (_match.Count is 1 or 3 && p.DistanceTo(_match[^1]) < 1e-9)
        {
            Canvas.ShowMessage("拡大縮小: 2 点目は 1 点目と別の点を指定してください。");
            return;
        }
        if (_match.Count < 3)
        {
            _match.Add(p);
            UpdatePrompt();
            return;
        }
        var (k, rad) = MatchTransform(_match[0], _match[1], _match[2], p, RotateToMatch);
        var (s1, t1) = (_match[0], _match[2]);
        var pairs = _targets.Where(Doc.Entities.Contains).Select(e => (Old: e, New: Matched(e, s1, t1, k, rad))).ToList();
        Execute(new ReplaceEntitiesCommand(pairs, $"拡大縮小 ×{k:0.####}"));
        _targets = [.. pairs.Select(x => x.New)];
        Canvas.SetSelection(_targets);
        Canvas.ShowMessage($"拡大縮小: {pairs.Count} 個を {k:0.######} 倍にして 2 点を合わせました" +
                           (RotateToMatch ? $" (回転 {rad * 180 / Math.PI:0.####}°)。" : " (向きはそのまま)。"));
        _match.Clear();
        SetPrompt($"続けて拡大縮小 ({_targets.Count} 個, 2 点を合わせる): 合わせる元の 1 点目を指定 (Esc で終了)");
    }

    /// <summary>元の 2 点 (s1, s2) を合わせ先 (t1, t2) に合わせる倍率と回転角 (rotate でなければ 0)。</summary>
    public static (double K, double Rad) MatchTransform(Vec2 s1, Vec2 s2, Vec2 t1, Vec2 t2, bool rotate)
    {
        var vs = s2 - s1;
        var vt = t2 - t1;
        var k = vs.Length > 0 ? vt.Length / vs.Length : 1;
        return (k, rotate ? GeoMath.NormalizeSignedAngle(vt.Angle - vs.Angle) : 0);
    }

    private static Entity Matched(Entity e, Vec2 s1, Vec2 t1, double k, double rad)
    {
        var c = e.Clone();
        c.Translate(t1 - s1);
        if (rad != 0) c.Rotate(t1, rad);
        if (Math.Abs(k - 1) > 1e-15) c.Scale(t1, k);
        return c;
    }

    public override bool OnText(string text)
    {
        if (Mode != ScaleMode.Factor || _base == null || !TryParseNumber(text, out var v)) return false;
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
        ResetPoints();
        SetPrompt($"続けて拡大縮小 ({_targets.Count} 個): 基点を指定" + (FixedFactor is double f ? $" → {f:0.####} 倍にする" : "") + " (Esc で終了)");
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void DrawPreview(DrawingContext dc)
    {
        if (Mode == ScaleMode.MatchTwoPoints)
        {
            DrawMatchPreview(dc);
            return;
        }
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
        DrawGhosts(dc, e => { var c = e.Clone(); c.Scale(b, k); return c; }, p => b + (p - b) * k);
    }

    private void DrawMatchPreview(DrawingContext dc)
    {
        // 元の 2 点 (実線) と、指定中の点からカーソルまで (破線)
        if (_match.Count >= 2) Canvas.DrawWorldLine(dc, CadCanvas.PreviewPen, _match[0], _match[1]);
        if (_match.Count is 1 or 3) Canvas.DrawWorldLine(dc, CadCanvas.PreviewDashPen, _match[^1], _cursor);
        if (_match.Count != 3 || _cursor.DistanceTo(_match[2]) < 1e-9) return;
        var (k, rad) = MatchTransform(_match[0], _match[1], _match[2], _cursor, RotateToMatch);
        var (s1, t1) = (_match[0], _match[2]);
        DrawGhosts(dc, e => Matched(e, s1, t1, k, rad), p => t1 + (p - s1).Rotate(rad) * k);
    }

    private void DrawGhosts(DrawingContext dc, Func<Entity, Entity> transform, Func<Vec2, Vec2> map)
    {
        if (_targets.Count > MaxPreviewEntities)
        {
            var box = _targets.Aggregate(Box2.Empty, (acc, e) => acc.Union(e.Bounds));
            Canvas.DrawWorldPolyline(dc, CadCanvas.PreviewPen,
                [map(box.Min), map(new Vec2(box.Max.X, box.Min.Y)), map(box.Max), map(new Vec2(box.Min.X, box.Max.Y))], true);
            return;
        }
        foreach (var e in _targets) Canvas.DrawEntity(dc, transform(e), GhostColor);
    }
}

/// <summary>
/// 2 点指示 (位置合わせ): 選択した図形 (画像など) の上の 2 点を、図面上の 2 点に合わせる。
/// 元の 1 点目 → 合わせ先の 1 点目 → 元の 2 点目 → 合わせ先の 2 点目 の順に指定すると、
/// 1 点目どうしが重なるよう移動し、2 点目の方向へ回転する。ScaleToFit なら 2 点間の長さも合わせる (拡大縮小)。
/// </summary>
public sealed class AlignTool : CadTool
{
    private const int MaxPreviewEntities = 300;
    private static readonly RgbColor GhostColor = new(0x66, 0xCC, 0xFF);

    /// <summary>2 点間の長さも合わせる (拡大縮小する)。次の 2 点指示でも同じ設定を使う。</summary>
    public static bool ScaleToFit { get; set; } = true;

    private List<Entity> _targets = [];
    private readonly List<Vec2> _points = [];
    private Vec2 _cursor;

    public override string Name => "2 点指示";
    public override bool RequiresSelection => true;
    public override bool CanStepBack => _points.Count > 0;
    public override Vec2? BasePoint => _points.Count is 1 or 3 ? _points[^1] : null;

    public override IEnumerable<(string Label, Action Run)> MenuItems =>
    [
        (ScaleToFit ? "大きさも合わせる: オン → オフにする (回転と移動だけ)" : "大きさも合わせる: オフ → オンにする (拡大縮小もする)", () =>
        {
            ScaleToFit = !ScaleToFit;
            UpdatePrompt();
            Canvas.RedrawOverlay();
        }),
    ];

    protected override void OnActivated()
    {
        _targets = [.. Doc.Entities.Where(Canvas.Selection.Contains)];
        UpdatePrompt();
    }

    private static string Mode => ScaleToFit ? "回転・移動・大きさ" : "回転・移動のみ";

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
        Canvas.ShowMessage($"2 点指示: {pairs.Count} 個を 回転 {rad * 180 / Math.PI:0.####}°" +
                           (ScaleToFit ? $"、{k:0.######} 倍" : "") + " で合わせました。");
        // 続けて合わせ直せる (Esc で終了)
        _points.Clear();
        UpdatePrompt();
    }

    /// <summary>元の 2 点 (s1, s2) を合わせ先 (d1, d2) に合わせる回転角と倍率。</summary>
    public static (double Rad, double K) Transform(Vec2 s1, Vec2 d1, Vec2 s2, Vec2 d2)
    {
        var vs = s2 - s1;
        var vd = d2 - d1;
        var rad = vd.Angle - vs.Angle;
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
