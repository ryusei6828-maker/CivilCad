using System.Globalization;
using System.Windows.Media;
using CivilCad.Core.Commands;
using CivilCad.Core.Editing;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>
/// 面取り (CHA): 2 本の線の角を斜めに落とす。距離は 1 本目の側・2 本目の側の順 (「2,3」。1 つなら両方同じ、0 で角を突き合わせる)。
/// 距離はそのまま続けて別の角にも使える。線をクリックした側が残る。
/// </summary>
public sealed class ChamferTool : CadTool
{
    private static readonly RgbColor PreviewColor = new(0x66, 0xCC, 0xFF);

    /// <summary>前回の距離 (アプリを閉じるまで覚える)。</summary>
    private static double s_d1 = 1;
    private static double s_d2 = 1;

    private Entity? _first;
    private Vec2 _firstPick;
    private Entity? _hover;
    private Vec2 _cursor;

    public override string Name => "面取り";
    public override bool WantsSnap => false;
    public override bool WantsTextInput => true;
    public override bool CanStepBack => _first != null;
    public override string? ConfirmLabel => "終了 (Enter)";

    private static string Setting => s_d1 == s_d2 ? $"距離 {Format(s_d1)} m" : $"距離 {Format(s_d1)} m, {Format(s_d2)} m";

    /// <summary>1 本目を選び直す。</summary>
    public override void StepBack() => PromptFirst();

    protected override void OnActivated() => PromptFirst();

    private void PromptFirst()
    {
        _first = null;
        SetPrompt($"1 本目の線をクリック (残す側) [{Setting}] (数値で距離を変更、「2,3」で 1 本目・2 本目を別々に / Enter・Esc で終了)");
    }

    private void PromptSecond() =>
        SetPrompt($"2 本目の線をクリック (残す側) [{Setting}] (Esc で 1 本目を選び直す)");

    public override bool OnText(string text)
    {
        var parts = text.Replace('，', ',').Split(',');
        if (parts.Length > 2 || !TryParseNumber(parts[0], out var d1)) return false;
        var d2 = d1;
        if (parts.Length == 2 && !TryParseNumber(parts[1], out d2)) return false;
        if (d1 < 0 || d2 < 0)
        {
            Canvas.ShowMessage("面取り: 距離は 0 以上を入力してください。");
            return true;
        }
        (s_d1, s_d2) = (d1, d2);
        if (_first == null) PromptFirst();
        else PromptSecond();
        return true;
    }

    public override void OnPoint(Vec2 p)
    {
        if (Canvas.HitTest(p) is not { } hit) return;
        if (_first == null || !Doc.Entities.Contains(_first))
        {
            if (hit is not (LineEntity or PolylineEntity))
            {
                Canvas.ShowMessage("面取り: 線・ポリラインが対象です。");
                return;
            }
            _first = hit;
            _firstPick = p;
            PromptSecond();
            return;
        }
        if (Chamfer.Create(_first, _firstPick, hit, p, s_d1, s_d2, out var error) is not { } r)
        {
            Canvas.ShowMessage("面取り: " + error);
            return;
        }
        var commands = new List<IUndoableCommand> { new ReplaceEntitiesCommand(r.Replaced, Name) };
        if (r.Cut != null) commands.Add(new AddEntitiesCommand([r.Cut], Name));
        Execute(new CompositeCommand(Name, [.. commands]));
        _hover = null;
        PromptFirst();
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
        if (_hover is not (LineEntity or PolylineEntity) || !Doc.Entities.Contains(_hover)) return;
        if (first == null)
        {
            Canvas.DrawEntity(dc, _hover, PreviewColor);
            return;
        }
        // できあがりの形を先に見せる
        if (Chamfer.Create(first, _firstPick, _hover, _cursor, s_d1, s_d2, out _) is not { } r) return;
        foreach (var (_, changed) in r.Replaced) Canvas.DrawEntity(dc, changed, PreviewColor, selected: true);
        if (r.Cut != null) Canvas.DrawEntity(dc, r.Cut, PreviewColor, selected: true);
    }

    private static string Format(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
}
