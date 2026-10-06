using System.Windows;
using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>文字の中身を変える (文字変更 ED、選択中のダブルクリック)。</summary>
public static class TextEditing
{
    /// <summary>
    /// 新しい文字列を尋ねる (元の文字列 → 新しい文字列、やめたら null)。
    /// 既定は入力の窓。テストでは差し替える。
    /// </summary>
    public static Func<CadCanvas, string, string?> Ask { get; set; } = (canvas, current) =>
        TextEditWindow.Ask(Window.GetWindow(canvas), current);

    /// <summary>文字を変える。変えたら true (元に戻すで戻せる)。</summary>
    public static bool Edit(CadCanvas canvas, TextEntity text)
    {
        var s = Ask(canvas, text.Text);
        if (s == null || s == text.Text) return false;
        if (s.Length == 0)
        {
            canvas.ShowMessage("文字変更: 空の文字にはできません (消すときは削除してください)。");
            return false;
        }
        var changed = (TextEntity)text.Clone();
        changed.Text = s;
        canvas.Document.History.Execute(new ReplaceEntitiesCommand([(text, changed)], "文字変更"));
        if (canvas.Selection.Remove(text)) canvas.AddToSelection([changed]);
        canvas.ShowMessage($"文字変更: 「{text.Text}」→「{s}」");
        return true;
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
