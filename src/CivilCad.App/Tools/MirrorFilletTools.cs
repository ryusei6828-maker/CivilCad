using System.Globalization;
using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Editing;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>
/// ミラー (MI): 選択した図形を、2 点で決めた直線 (対称軸) について線対称に写す。既定は元を残す (C で残す/消す)。
/// 線・円・円弧・ポリライン・ハッチング・寸法・引き出し線は裏返し、文字・記号・画像は読める向きのまま位置だけ写す。
/// </summary>
public sealed class MirrorTool : CadTool
{
    private const int MaxPreviewEntities = 300;
    private static readonly RgbColor PreviewColor = new(0x66, 0xCC, 0xFF);

    /// <summary>元を残すか (アプリを閉じるまで覚える)。</summary>
    private static bool s_keepOriginal = true;

    private List<Entity> _targets = [];
    private Vec2? _first;
    private Vec2 _cursor;

    public override string Name => "ミラー";
    public override bool RequiresSelection => true;
    public override bool CanStepBack => _first.HasValue;
    public override Vec2? BasePoint => _first;
    public override bool WantsTextInput => true;

    public override IEnumerable<(string Label, Action Run)> MenuItems =>
        [(s_keepOriginal ? "元を消す (C)" : "元を残す (C)", () => OnText("C"))];

    /// <summary>1 点目を指定し直す。</summary>
    public override void StepBack()
    {
        _first = null;
        UpdatePrompt();
    }

    protected override void OnActivated()
    {
        if (!_first.HasValue) _targets = [.. Canvas.Selection];
        UpdatePrompt();
    }

    private void UpdatePrompt()
    {
        var keep = s_keepOriginal ? " [元を残す]" : " [元を消す]";
        SetPrompt((_first == null ? "対称軸の 1 点目を指定" : "対称軸の 2 点目を指定 (F8 の直交で水平・垂直の軸)") + keep + " (C で元を残す/消す)");
    }

    public override bool OnText(string text)
    {
        if (!text.Trim().Equals("C", StringComparison.OrdinalIgnoreCase)) return false;
        s_keepOriginal = !s_keepOriginal;
        UpdatePrompt();
        return true;
    }

    public override void OnPoint(Vec2 p)
    {
        if (_first is not Vec2 a)
        {
            _first = p;
            UpdatePrompt();
            return;
        }
        if (p.DistanceTo(a) <= 0) return;
        Apply(a, p);
    }

    private void Apply(Vec2 a, Vec2 b)
    {
        List<Entity> result;
        if (s_keepOriginal)
        {
            result = Grouping.RenewGroups(_targets.Select(e => e.Clone()).ToList());
            result.ForEach(e => e.Mirror(a, b));
            Execute(new AddEntitiesCommand(result, "ミラー"));
        }
        else
        {
            var pairs = _targets.Select(e =>
            {
                var m = e.Clone();
                m.Mirror(a, b);
                return (Old: e, New: m);
            }).ToList();
            Execute(new ReplaceEntitiesCommand(pairs, "ミラー"));
            result = [.. pairs.Select(p => p.New)];
        }
        var count = _targets.Count;
        Finish();
        Canvas.SetSelection(result);
        Canvas.ShowMessage($"{count} 個の図形をミラーしました" + (s_keepOriginal ? " (元を残して複写)。" : "。"));
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void DrawPreview(DrawingContext dc)
    {
        if (_first is not Vec2 a || _cursor.DistanceTo(a) == 0) return;
        // 対称軸は画面の端まで伸ばして見せる
        var d = (_cursor - a).Normalized();
        var view = Canvas.VisibleWorldBox;
        var span = (view.Max - view.Min).Length + a.DistanceTo(view.Center);
        Canvas.DrawWorldLine(dc, CadCanvas.PreviewDashPen, a - d * span, a + d * span);
        if (_targets.Count > MaxPreviewEntities) return;
        foreach (var e in _targets)
        {
            var ghost = e.Clone();
            ghost.Mirror(a, _cursor);
            Canvas.DrawEntity(dc, ghost, PreviewColor);
        }
    }
}

/// <summary>
/// フィレット (F): 2 本の線の角を半径 R の円弧でつなぐ (線は接点まで切る / 伸ばす)。R = 0 で角を突き合わせる (コーナー)。
/// 半径は数値で入れ、そのまま続けて別の角にも使える。線をクリックした側が残る。
/// </summary>
public sealed class FilletTool : CadTool
{
    private static readonly RgbColor PreviewColor = new(0x66, 0xCC, 0xFF);

    /// <summary>前回の半径 (アプリを閉じるまで覚える)。</summary>
    private static double s_radius;

    private LineEntity? _first;
    private Vec2 _firstPick;
    private Entity? _hover;
    private Vec2 _cursor;

    public override string Name => "フィレット";
    public override bool WantsSnap => false;
    public override bool WantsTextInput => true;
    public override bool CanStepBack => _first != null;
    public override string? ConfirmLabel => "終了 (Enter)";

    /// <summary>1 本目を選び直す。</summary>
    public override void StepBack() => PromptFirst();

    protected override void OnActivated() => PromptFirst();

    private void PromptFirst()
    {
        _first = null;
        SetPrompt($"1 本目の線をクリック (残す側) [半径 R = {Format(s_radius)} m] (数値で半径を変更、0 で角を突き合わせる / Enter・Esc で終了)");
    }

    private void PromptSecond() =>
        SetPrompt($"2 本目の線をクリック (残す側) [半径 R = {Format(s_radius)} m] (Esc で 1 本目を選び直す)");

    public override bool OnText(string text)
    {
        if (!TryParseNumber(text, out var r)) return false;
        if (r < 0)
        {
            Canvas.ShowMessage("フィレット: 半径は 0 以上を入力してください。");
            return true;
        }
        s_radius = r;
        if (_first == null) PromptFirst();
        else PromptSecond();
        return true;
    }

    public override void OnPoint(Vec2 p)
    {
        if (Canvas.HitTest(p) is not { } hit) return;
        if (hit is not LineEntity line)
        {
            Canvas.ShowMessage("フィレット: 線 (L) が対象です。");
            return;
        }
        if (_first == null || !Doc.Entities.Contains(_first))
        {
            _first = line;
            _firstPick = p;
            PromptSecond();
            return;
        }
        if (Fillet.Create(_first, _firstPick, line, p, s_radius, out var error) is not { } r)
        {
            Canvas.ShowMessage("フィレット: " + error switch
            {
                Fillet.Error.Parallel => "平行な線どうしはつなげません。",
                Fillet.Error.TooLarge => "半径が大きすぎます (円弧の接点が線の外に出ます)。",
                _ => "1 本目と別の線をクリックしてください。",
            });
            return;
        }
        Execute(BuildCommand(_first, line, r));
        Canvas.ShowMessage(r.Arc != null ? $"フィレット: 半径 {Format(s_radius)} m の円弧でつなぎました。" : "フィレット: 角を突き合わせました。");
        _hover = null;
        PromptFirst();
    }

    /// <summary>線は差し替え (長さ 0 になったら消す)、円弧は追加。まとめて 1 回で戻せるようにする。</summary>
    private static CompositeCommand BuildCommand(LineEntity l1, LineEntity l2, Fillet.Result r)
    {
        var replace = new List<(Entity Old, Entity New)>();
        var remove = new List<Entity>();
        if (r.Line1 != null) replace.Add((l1, r.Line1)); else remove.Add(l1);
        if (r.Line2 != null) replace.Add((l2, r.Line2)); else remove.Add(l2);
        var commands = new List<IUndoableCommand>();
        if (replace.Count > 0) commands.Add(new ReplaceEntitiesCommand(replace, "フィレット"));
        if (remove.Count > 0) commands.Add(new RemoveEntitiesCommand(remove, "フィレット"));
        if (r.Arc != null) commands.Add(new AddEntitiesCommand([r.Arc], "フィレット"));
        return new CompositeCommand(r.Arc != null ? "フィレット" : "コーナー", [.. commands]);
    }

    public override void OnMouseMove(Vec2 p)
    {
        _cursor = p;
        _hover = Canvas.HitTest(p);
    }

    public override void OnEnter() => Finish();

    public override void OnCancel()
    {
        if (_first != null) PromptFirst();
        else Finish();
    }

    public override void DrawPreview(DrawingContext dc)
    {
        var first = _first != null && Doc.Entities.Contains(_first) ? _first : null;
        if (first != null) Canvas.DrawEntity(dc, first, PreviewColor, selected: true);
        if (_hover is not LineEntity hover || !Doc.Entities.Contains(hover)) return;
        if (first == null || ReferenceEquals(hover, first))
        {
            Canvas.DrawEntity(dc, hover, PreviewColor);
            return;
        }
        // できあがりの形を先に見せる
        if (Fillet.Create(first, _firstPick, hover, _cursor, s_radius, out _) is not { } r) return;
        foreach (var e in new Entity?[] { r.Line1, r.Line2, r.Arc })
            if (e != null) Canvas.DrawEntity(dc, e, PreviewColor, selected: true);
    }

    private static string Format(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
}
