using System.Windows;
using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>文字の変更の結果: 文字 (改行は '\n')・文字高 (m)・揃え・幅指定を残すか。</summary>
public sealed record TextEditResult(string Text, double HeightM, TextHAlign HAlign, TextVAlign VAlign, bool KeepFit);

/// <summary>文字を変える (文字変更 ED、選択中のダブルクリック)。Excel のセルのように、文字・大きさ・揃え・改行を決める。</summary>
public static class TextEditing
{
    /// <summary>
    /// 新しい内容を尋ねる (やめたら null)。既定は入力の窓。テストでは差し替える。
    /// </summary>
    public static Func<CadCanvas, TextEntity, TextEditResult?> Ask { get; set; } = (canvas, text) =>
        TextEditWindow.Ask(Window.GetWindow(canvas), text, canvas.Document);

    private static TextVAlign Norm(TextVAlign v) => v == TextVAlign.Baseline ? TextVAlign.Bottom : v;

    /// <summary>文字を変える。変えたら true (元に戻すで戻せる)。</summary>
    public static bool Edit(CadCanvas canvas, TextEntity text)
    {
        var r = Ask(canvas, text);
        if (r == null) return false;
        var s = r.Text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        if (s.Trim().Length == 0)
        {
            canvas.ShowMessage("文字変更: 空の文字にはできません (消すときは削除してください)。");
            return false;
        }
        var height = text.ScreenHeightPx != null ? text.Height : r.HeightM;
        if (!(height > 0)) height = text.Height;
        var fit = r.KeepFit && !s.Contains('\n') ? text.FitWidth : null;
        var alignChanged = r.HAlign != text.HAlign || Norm(r.VAlign) != Norm(text.VAlign);
        if (s == text.Text && Math.Abs(height - text.Height) < 1e-12 && !alignChanged && fit == text.FitWidth) return false;

        var changed = (TextEntity)text.Clone();
        if (alignChanged)
        {
            // 元の文字の枠 (セル) の中で揃えを変える: 新しい揃えの基準点を、元の枠の左・中央・右 / 下・中・上に置く
            var w = text.ApproxWidth;
            var h = text.BlockHeight;
            var u = Vec2.FromAngle(text.Rotation);
            var v = u.LeftNormal;
            var ax = r.HAlign switch { TextHAlign.Center => w / 2, TextHAlign.Right => w, _ => 0 };
            var ay = r.VAlign switch { TextVAlign.Middle => h / 2, TextVAlign.Top => h, _ => 0 };
            changed.Position = text.BaselineStart(w) + u * ax + v * ay;
        }
        changed.Text = s;
        changed.Height = height;
        changed.HAlign = r.HAlign;
        changed.VAlign = r.VAlign;
        changed.FitWidth = fit;
        canvas.Document.History.Execute(new ReplaceEntitiesCommand([(text, changed)], "文字変更"));
        if (canvas.Selection.Remove(text)) canvas.AddToSelection([changed]);
        canvas.ShowMessage($"文字変更: 「{Summary(text.Text)}」→「{Summary(s)}」");
        return true;
    }

    /// <summary>メッセージ用: 複数行は 1 行目と「…」。</summary>
    private static string Summary(string s)
    {
        var i = s.IndexOf('\n');
        return i < 0 ? s : s[..i] + "…";
    }
}

/// <summary>文字変更 (ED): 文字をクリックすると中身を変えられる。続けて別の文字も変えられる (Esc で終了)。</summary>
public sealed class TextEditTool : CadTool
{
    private static readonly RgbColor HoverColor = new(0x66, 0xCC, 0xFF);
    private TextEntity? _hover;

    public override string Name => "文字変更";
    public override bool WantsSnap => false;
    public override string? ConfirmLabel => "終了 (Enter)";

    protected override void OnActivated() => SetPrompt("変える文字をクリック (Enter・Esc で終了)");

    public override void OnPoint(Vec2 p)
    {
        if (HitText(p) is not { } text)
        {
            if (Canvas.HitTest(p) != null) Canvas.ShowMessage("文字変更: 文字をクリックしてください。");
            return;
        }
        TextEditing.Edit(Canvas, text);
        _hover = null;
    }

    public override void OnMouseMove(Vec2 p) => _hover = HitText(p);

    public override void DrawPreview(DrawingContext dc)
    {
        if (_hover != null && Doc.Entities.Contains(_hover)) Canvas.DrawEntity(dc, _hover, HoverColor, selected: true);
    }

    /// <summary>クリックした位置の文字 (ほかの図形が重なっていても文字を優先する)。</summary>
    private TextEntity? HitText(Vec2 p)
    {
        if (Canvas.HitTest(p) is TextEntity t) return t;
        var tol = 6 / Canvas.Scale;
        return Doc.Entities.OfType<TextEntity>().Where(Doc.IsEditable).Where(x => x.DistanceTo(p) <= tol).MinBy(x => x.DistanceTo(p));
    }
}
