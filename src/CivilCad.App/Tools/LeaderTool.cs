using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>引き出し線・旗揚げの設定 (作図入力バーで変更。次の作図でも使う)。大きさは用紙上の mm。</summary>
public static class LeaderSettings
{
    public static LeaderTip Tip { get; set; } = LeaderTip.Arrow;
    public static double TextHeightMm { get; set; } = 3.5;
    public static double TipSizeMm { get; set; } = 3;
}

/// <summary>
/// 引き出し線 (LD) / 旗揚げ (HA)。
/// ・引き出し線: 先端 → 折れ点 (いくつでも) をクリックし Enter → 文字を入力 (行ごとに Enter、空の Enter で確定。文字なしも可)
/// ・旗揚げ: 先端 → 旗竿の始まり をクリック → 文字を入力 (行ごとに Enter、空の Enter で確定)。旗竿は文字の幅で自動
/// 1 行に「|」で区切って複数行を一度に入れることもできる。Esc を押すまで続けて描ける。
/// </summary>
public sealed class LeaderTool(bool flag) : CadTool, IStyledDrawTool, ITextEntryTool
{
    private static readonly RgbColor PreviewColor = new(0x66, 0xCC, 0xFF);
    private readonly List<Vec2> _points = [];
    private readonly List<string> _lines = [];
    private bool _texting;
    private Vec2 _cursor;

    public bool IsFlag => flag;
    public override string Name => flag ? "旗揚げ" : "引き出し線";
    public override Vec2? BasePoint => _points.Count > 0 && !_texting ? _points[^1] : null;
    public override bool WantsTextInput => _texting;
    public override bool CanStepBack => _points.Count > 0;
    public override string? ConfirmLabel => _texting ? "確定 (Enter)" : !flag && _points.Count >= 2 ? "点の指定を終える (Enter)" : null;

    protected override void OnActivated() => UpdatePrompt();

    private void UpdatePrompt() => SetPrompt(
        _points.Count == 0 ? $"{Name}: 先端 ({TipName(LeaderSettings.Tip)}の付く点) を指定"
        : !_texting ? flag ? "旗竿の始まり (折れ点) を指定" : $"次の折れ点を指定 (Enter で点の指定を終えて文字の入力へ)"
        : "右のプロパティの入力欄に文字を入力して Enter (Shift+Enter で改行)");

    public override void StepBack()
    {
        if (_lines.Count > 0) _lines.RemoveAt(_lines.Count - 1);
        else if (_texting) _texting = false;
        else if (_points.Count > 0) _points.RemoveAt(_points.Count - 1);
        if (_texting && flag && _points.Count < 2) _texting = false;
        UpdatePrompt();
    }

    public override void OnPoint(Vec2 p)
    {
        if (_texting)
        {
            // 文字の入力中のクリックは確定 (入力欄に入れかけの文字も入れる)
            _lines.AddRange(DraftText.Replace("\r", "").Split('\n').Where(l => l.Length > 0));
            DraftText = ""; // 行に入れたので、入力中の分としては数えない
            Place();
            UpdatePrompt();
            return;
        }
        if (_points.Count > 0 && (p - _points[^1]).Length < 1e-9) return;
        _points.Add(p);
        if (flag && _points.Count == 2) _texting = true;
        UpdatePrompt();
    }

    public override void OnEnter()
    {
        if (_texting) Place();
        else if (_points.Count >= 2) _texting = true;
        else if (_points.Count == 0)
        {
            Finish();
            return;
        }
        UpdatePrompt();
    }

    public override bool OnText(string text)
    {
        if (!_texting) return false;
        _lines.AddRange(ParseLines(text).Split('\n'));
        UpdatePrompt();
        return true;
    }

    // ---- 右のプロパティの入力欄 ----

    public bool IsWaitingForText => _texting;
    public string TextEntryHint => flag
        ? "旗の文字を入力して Enter で置く。Shift+Enter で改行 (1 行目が一番上)。Esc で点の指定に戻る"
        : "文字を入力して Enter で置く (空のままなら線だけ)。Shift+Enter で改行。Esc で点の指定に戻る";
    public bool AllowsMultipleLines => true;
    public string DraftText { get; set; } = "";

    public void SubmitText(string text)
    {
        if (!_texting) return;
        _lines.AddRange(text.Replace("\r", "").Split('\n').SelectMany(l => ParseLines(l).Split('\n')).Where(l => l.Length > 0));
        DraftText = ""; // 行に入れたので、入力中の分としては数えない
        Place();
        UpdatePrompt();
    }

    public void CancelText()
    {
        DraftText = "";
        _lines.Clear();
        _texting = false;
        if (flag && _points.Count >= 2) _points.RemoveAt(_points.Count - 1); // 旗竿の始まりを選び直す
        UpdatePrompt();
    }

    /// <summary>確定した行と入力中の行。</summary>
    private IEnumerable<string> AllLines => _lines.Concat(DraftText.Replace("\r", "").Split('\n').Where(l => l.Length > 0));

    private LeaderEntity Make(IReadOnlyList<Vec2> points) =>
        new(Doc.CurrentLayer, points, flag, string.Join("\n", _texting ? AllLines : _lines),
            Doc.PaperMmToMeters(LeaderSettings.TextHeightMm), Doc.PaperMmToMeters(LeaderSettings.TipSizeMm), LeaderSettings.Tip);

    private void Place()
    {
        if (_points.Count >= 2)
            Execute(new AddEntitiesCommand([Styled(Make(_points))], Name));
        _points.Clear();
        _lines.Clear();
        _texting = false;
        DraftText = "";
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void DrawPreview(DrawingContext dc)
    {
        if (_points.Count == 0) return;
        var pts = _texting ? _points : [.. _points, _cursor];
        if (pts.Count < 2) return;
        Canvas.DrawEntity(dc, Make(pts), PreviewColor);
    }

    // ---- 入力の解釈 (プロパティ欄でも使う) ----

    /// <summary>「|」(全角も可) で区切った行を改行にする。前後の空白は除く。</summary>
    public static string ParseLines(string s) =>
        string.Join("\n", s.Split('|', '｜').Select(l => l.Trim()).Where(l => l.Length > 0));

    public static string TipName(LeaderTip tip) => tip switch
    {
        LeaderTip.Arrow => "矢印",
        LeaderTip.Dot => "黒丸",
        _ => "なし",
    };

    public static bool TryParseTip(string s, out LeaderTip tip)
    {
        foreach (var t in Enum.GetValues<LeaderTip>())
            if (TipName(t) == s.Trim())
            {
                tip = t;
                return true;
            }
        tip = LeaderTip.Arrow;
        return false;
    }
}
