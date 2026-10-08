using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Editing;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>
/// トリム・延長の共通部分。
/// 1. 境界にする図形を選ぶ (先に選択してあればそれを使う。何も選ばず Enter で全図形)
/// 2. 対象をクリックして切り取る / 伸ばす (Shift+クリックで逆の操作)
/// </summary>
public abstract class EdgeEditTool : CadTool
{
    private static readonly RgbColor EdgeColor = new(0xFF, 0xAA, 0x00);
    private static readonly RgbColor RemoveColor = new(0xFF, 0x50, 0x50);
    private static readonly RgbColor AddColor = new(0x66, 0xCC, 0xFF);

    private readonly HashSet<Entity> _edges = [];
    private bool _allEdges;
    private bool _choosingEdges = true;
    private Vec2 _cursor;
    private Entity? _hover;

    public override bool WantsSnap => false;
    protected abstract string EdgePrompt { get; }
    protected abstract string TargetPrompt { get; }

    /// <summary>true = 延長、false = トリム (Shift を押している間は逆)。</summary>
    protected abstract bool ExtendsByDefault { get; }

    private bool Extending => ExtendsByDefault != ((Keyboard.Modifiers & ModifierKeys.Shift) != 0);
    public override string? ConfirmLabel => _choosingEdges ? "境界を確定 (Enter)" : "終了 (Enter)";
    private IEnumerable<Entity> Edges => _allEdges ? Doc.Entities.Where(Doc.IsVisible) : _edges.Where(Doc.Entities.Contains);

    protected override void OnActivated()
    {
        if (!_choosingEdges)
        {
            PromptTargets();
            return;
        }
        if (Canvas.Selection.Count > 0)
        {
            _edges.UnionWith(Canvas.Selection);
            Canvas.ClearSelection();
            StartTargets();
            return;
        }
        SetPrompt(EdgePrompt + " (クリックで追加/解除、Enter で確定。何も選ばず Enter で全図形が境界)");
    }

    private void StartTargets()
    {
        _choosingEdges = false;
        _allEdges = _edges.Count == 0;
        PromptTargets();
    }

    private void PromptTargets() =>
        SetPrompt(TargetPrompt + (_allEdges ? " [境界: 全図形]" : $" [境界: {_edges.Count} 個]") + " (Shift+クリックで逆の操作 / Enter・Esc で終了)");

    public override void OnPoint(Vec2 p)
    {
        var hit = Canvas.HitTest(p);
        if (_choosingEdges)
        {
            if (hit != null && !_edges.Remove(hit)) _edges.Add(hit);
            return;
        }
        if (hit == null) return;
        var error = Extending ? DoExtend(hit, p) : DoTrim(hit, p);
        if (error != null) Canvas.ShowMessage(error);
        _hover = null;
    }

    public override void OnMouseMove(Vec2 p)
    {
        _cursor = p;
        _hover = Canvas.HitTest(p);
    }

    public override void OnEnter()
    {
        if (_choosingEdges) StartTargets();
        else Finish();
    }

    private string? DoTrim(Entity target, Vec2 pick)
    {
        if (TrimExtend.Trim(target, pick, Edges) is not { } r)
            return "トリム: 境界との交点がありません (線・円弧・円・ポリラインが対象。円と閉じたポリラインは交点が 2 つ以上必要)。";
        Execute(new SplitEntityCommand(target, r.Pieces, "トリム"));
        ReplaceEdge(target, r.Pieces);
        return null;
    }

    private string? DoExtend(Entity target, Vec2 pick)
    {
        if (TrimExtend.Extend(target, pick, Edges) is not { } e)
            return "延長: 伸ばした先に境界がありません (線・円弧・開いたポリラインが対象)。";
        Execute(new ReplaceEntitiesCommand([(target, e)], "延長"));
        ReplaceEdge(target, [e]);
        return null;
    }

    /// <summary>境界にしていた図形が置き換わったら、新しい図形を境界に引き継ぐ。</summary>
    private void ReplaceEdge(Entity old, IEnumerable<Entity> replacements)
    {
        if (_edges.Remove(old)) _edges.UnionWith(replacements);
    }

    public override void DrawPreview(DrawingContext dc)
    {
        if (_choosingEdges || !_allEdges)
            foreach (var edge in _edges.Where(Doc.Entities.Contains))
                Canvas.DrawEntity(dc, edge, EdgeColor, selected: true);
        if (_hover == null || !Doc.Entities.Contains(_hover)) return;
        if (_choosingEdges)
        {
            Canvas.DrawEntity(dc, _hover, EdgeColor);
            return;
        }
        // 消える部分は赤、伸びた形は水色
        if (Extending)
        {
            if (TrimExtend.Extend(_hover, _cursor, Edges) is { } e) Canvas.DrawEntity(dc, e, AddColor, selected: true);
        }
        else if (TrimExtend.Trim(_hover, _cursor, Edges) is { } r)
        {
            Canvas.DrawEntity(dc, r.Removed, RemoveColor, selected: true);
        }
    }
}

public sealed class TrimTool : EdgeEditTool
{
    public override string Name => "トリム";
    protected override string EdgePrompt => "切り取りの境界にする図形を選択";
    protected override string TargetPrompt => "切り取る部分をクリック";
    protected override bool ExtendsByDefault => false;
}

public sealed class ExtendTool : EdgeEditTool
{
    public override string Name => "延長";
    protected override string EdgePrompt => "延長先の境界にする図形を選択";
    protected override string TargetPrompt => "伸ばす図形の端の近くをクリック";
    protected override bool ExtendsByDefault => true;
}

/// <summary>距離を入力 → 図形をクリック → ずらす側をクリック。距離はそのままで続けて別の図形に使える。</summary>
public sealed class OffsetTool : CadTool
{
    private static readonly RgbColor PreviewColor = new(0x66, 0xCC, 0xFF);

    /// <summary>前回の距離 (アプリを閉じるまで覚える)。</summary>
    private static double s_lastDistance = 1;

    private double? _distance;
    private Entity? _target;
    private Entity? _hover;
    private Vec2 _cursor;

    public override string Name => "オフセット";
    public override bool WantsSnap => false;
    public override bool CanStepBack => _target != null;
    public override string? ConfirmLabel => _distance.HasValue ? "終了 (Enter)" : null;

    /// <summary>選んだ図形を選び直す。</summary>
    public override void StepBack() => PromptPick();

    public override bool WantsTextInput => true;

    protected override void OnActivated()
    {
        if (_distance.HasValue) PromptPick();
        else SetPrompt($"オフセット距離 (m) を入力 (Enter で前回の {Format(s_lastDistance)} m)");
    }

    public override bool OnText(string text)
    {
        if (!TryParseNumber(text, out var d)) return false;
        if (!(d > 0))
        {
            Canvas.ShowMessage("オフセット: 距離は 0 より大きい値を入力してください。");
            return true;
        }
        s_lastDistance = d;
        _distance = d;
        PromptPick();
        return true;
    }

    private void PromptPick()
    {
        _target = null;
        SetPrompt($"ずらす図形をクリック [距離 {Format(_distance ?? s_lastDistance)} m] (数値入力で距離を変更 / Enter・Esc で終了)");
    }

    public override void OnPoint(Vec2 p)
    {
        if (!_distance.HasValue)
        {
            _distance = s_lastDistance;
            PromptPick();
        }
        if (_target == null)
        {
            if (Canvas.HitTest(p) is not { } hit) return;
            if (hit is not (LineEntity or CircleEntity or ArcEntity or PolylineEntity))
            {
                Canvas.ShowMessage("オフセット: 線・円・円弧・ポリラインが対象です。");
                return;
            }
            _target = hit;
            SetPrompt("ずらす側をクリック (Esc で図形を選び直す)");
            return;
        }
        if (Offset.Create(_target, _distance!.Value, p) is not { } result)
        {
            Canvas.ShowMessage("オフセット: この距離では作れません (内側に縮めると半径が 0 以下になるなど)。");
            return;
        }
        Execute(new AddEntitiesCommand([result], "オフセット"));
        PromptPick();
    }

    public override void OnMouseMove(Vec2 p)
    {
        _cursor = p;
        _hover = _target == null ? Canvas.HitTest(p) : null;
    }

    public override void OnEnter()
    {
        if (_distance.HasValue)
        {
            Finish();
            return;
        }
        _distance = s_lastDistance;
        PromptPick();
    }

    public override void OnCancel()
    {
        if (_target != null) PromptPick();
        else Finish();
    }

    public override void DrawPreview(DrawingContext dc)
    {
        if (_target != null && Doc.Entities.Contains(_target))
        {
            Canvas.DrawEntity(dc, _target, PreviewColor, selected: true);
            if (Offset.Create(_target, _distance ?? s_lastDistance, _cursor) is { } preview)
                Canvas.DrawEntity(dc, preview, PreviewColor);
        }
        else if (_distance.HasValue && _hover != null && Doc.Entities.Contains(_hover))
        {
            Canvas.DrawEntity(dc, _hover, PreviewColor);
        }
    }

    private static string Format(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>
/// 選択した図形を回転する (2 点指示): 回転の中心点 → 基準の方向点 (今の向き) → 回転先の方向点 (新しい向き)。
/// 中心から見た基準の方向点の向きが、回転先の方向点の向きになるように回す。
/// 方向点の代わりに回転角 (度、反時計回りが +) を入力しても回せる。C で元を残す。
/// </summary>
public sealed class RotateTool : CadTool
{
    private const int MaxPreviewEntities = 300;
    private List<Entity> _targets = [];
    private Vec2? _base;
    private Vec2? _ref;
    private Vec2 _cursor;
    private bool _copy;

    public override string Name => "回転";
    public override bool CanStepBack => _base.HasValue;

    public override IEnumerable<(string Label, Action Run)> MenuItems =>
        _base.HasValue ? [(_copy ? "元を残さない (C)" : "元を残す (C)", () => OnText("C"))] : [];

    /// <summary>一つ前の指定に戻る (方向点 → 中心点)。</summary>
    public override void StepBack()
    {
        if (_ref.HasValue) _ref = null;
        else _base = null;
        UpdatePrompt();
    }

    public override Vec2? BasePoint => _base;
    public override bool RequiresSelection => true;
    public override bool WantsTextInput => _base.HasValue;

    protected override void OnActivated()
    {
        if (!_base.HasValue) _targets = [.. Canvas.Selection];
        UpdatePrompt();
    }

    private void UpdatePrompt()
    {
        var copy = _copy ? " [元を残す: オン]" : "";
        SetPrompt(_base == null ? "回転の中心点を指定"
            : _ref == null ? $"基準の方向点 (今の向きを示す点) を指定、または回転角 (度、反時計回りが +) を入力{copy} (C で元を残す/残さない)"
            : $"回転先の方向点 (新しい向き) を指定、または回転角を入力{copy}");
    }

    public override bool OnText(string text)
    {
        if (!_base.HasValue) return false;
        var t = text.Trim();
        if (t.Equals("C", StringComparison.OrdinalIgnoreCase))
        {
            _copy = !_copy;
            UpdatePrompt();
            return true;
        }
        if (!TryParseNumber(t, out var deg)) return false;
        Apply(deg * Math.PI / 180);
        return true;
    }

    public override void OnPoint(Vec2 p)
    {
        if (_base is not Vec2 b)
        {
            _base = p;
            UpdatePrompt();
            return;
        }
        if (p.DistanceTo(b) <= 0) return;
        if (_ref is not Vec2 r)
        {
            _ref = p;
            UpdatePrompt();
            return;
        }
        Apply(Angle(b, r, p));
    }

    /// <summary>中心 b から見て、r の向きを p の向きにする回転角 (-π〜π)。</summary>
    public static double Angle(Vec2 b, Vec2 r, Vec2 p)
    {
        var a = (p - b).Angle - (r - b).Angle;
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a <= -Math.PI) a += 2 * Math.PI;
        return a;
    }

    private void Apply(double rad)
    {
        var origin = _base!.Value;
        var deg = rad * 180 / Math.PI;
        var name = $"回転 ({deg:0.###}°)";
        if (_copy)
        {
            var copies = _targets.Select(e => e.Clone()).ToList();
            copies.ForEach(e => e.Rotate(origin, rad));
            Execute(new AddEntitiesCommand(copies, name));
        }
        else
        {
            Execute(new RotateTranslateCommand(_targets, origin, rad, Vec2.Zero, name));
        }
        Canvas.ShowMessage($"{_targets.Count} 個の図形を {deg:0.###}° 回転しました" + (_copy ? " (元を残して複写)。" : "。"));
        Finish();
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void DrawPreview(DrawingContext dc)
    {
        if (_base is not Vec2 b || _cursor.DistanceTo(b) == 0) return;
        if (_ref is not Vec2 r)
        {
            Canvas.DrawWorldLine(dc, CadCanvas.PreviewDashPen, b, _cursor);
            return;
        }
        Canvas.DrawWorldLine(dc, CadCanvas.PreviewPen, b, r);
        Canvas.DrawWorldLine(dc, CadCanvas.PreviewDashPen, b, _cursor);
        if (_targets.Count > MaxPreviewEntities) return;
        var rad = Angle(b, r, _cursor);
        foreach (var e in _targets)
        {
            var ghost = e.Clone();
            ghost.Rotate(b, rad);
            Canvas.DrawEntity(dc, ghost, new RgbColor(0x66, 0xCC, 0xFF));
        }
    }
}

/// <summary>
/// 結合 (J): 選択中の図形をつなぎ直す。ツールではなくすぐに実行する。
/// 1. 同じ直線上の線は 1 本に (間が空いていても埋める)、同じ円上の円弧は 1 つに (<see cref="Join"/>)
/// 2. 端点がつながる線・開いたポリライン・円弧を 1 本のポリラインに (<see cref="Joiner"/>。一周すれば閉じる)
/// </summary>
public static class JoinCommand
{
    /// <summary>実行して、結果のメッセージを返す。</summary>
    public static string Run(CadCanvas canvas)
    {
        var doc = canvas.Document;
        if (canvas.Selection.Count < 2) return "結合: つなげたい線を 2 本以上選択してから実行してください (範囲選択も可)。";
        // 描画順で渡す (結合後の図形は先頭の図形のレイヤ・色・線幅を引き継ぐ)
        var selected = doc.Entities.Where(e => canvas.Selection.Contains(e) && doc.IsEditable(e)).ToList();
        var tolerance = Math.Max(doc.PaperMmToMeters(0.05), 1e-6); // 用紙上 0.05mm までの隙間はつながっているとみなす

        var merged = Join.Run(selected, tolerance, chain: false);
        var pieces = selected.Except(merged.Removed).Concat(merged.Added).ToList();
        var chained = Joiner.Join(pieces, tolerance);

        var removed = merged.Removed.Concat(chained.Consumed.Except(merged.Added)).ToList();
        var added = merged.Added.Except(chained.Consumed).Concat(chained.Joined).ToList();
        if (added.Count == 0)
            return "結合: つなげられる図形がありません (端点がつながる線・開いたポリライン・円弧、同じ直線上の線、同じ円上の円弧が対象です)。";
        doc.History.Execute(new CompositeCommand($"結合 ({removed.Count} → {added.Count})",
            new RemoveEntitiesCommand(removed), new AddEntitiesCommand(added)));
        canvas.SetSelection(added);
        return $"結合: {removed.Count} 個の図形を {added.Count} 個にまとめました" +
               (chained.ClosedCount > 0 ? $" (閉じた図形 {chained.ClosedCount} 個。塗りつぶし (FI) で中を塗れます)" : "") + "。";
    }
}
