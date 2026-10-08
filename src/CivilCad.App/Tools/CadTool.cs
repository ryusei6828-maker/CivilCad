using System.Globalization;
using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>
/// 作図・編集ツールの基底。点はマウスクリックかコマンド欄の座標入力で渡される。
/// </summary>
public abstract class CadTool
{
    protected CadCanvas Canvas { get; private set; } = null!;
    protected CadDocument Doc => Canvas.Document;

    public abstract string Name { get; }

    /// <summary>起動したコマンド名 (L, E など)。ツールバーで実行中のボタンを強調するのに使う。</summary>
    public string? CommandCode { get; set; }
    public string Prompt { get; private set; } = "";

    /// <summary>直交モードと相対座標 (@) の基準点。</summary>
    public virtual Vec2? BasePoint => null;
    public virtual bool WantsSnap => true;

    /// <summary>true なら文字の枠 (4 隅・辺の中点・中心) にもスナップする (回転コマンドだけ)。</summary>
    public virtual bool SnapsToTextBox => false;

    /// <summary>true の間はコマンド欄に文字を入力中とみなし、英字キー単独のショートカットを無効にする。</summary>
    public virtual bool WantsTextInput => false;

    /// <summary>
    /// マウスで指した点 (スナップ・直交の後) をツールの条件で補正する (長さ・角度の固定など)。
    /// コマンド欄に打ち込んだ座標には使わない。
    /// </summary>
    public virtual Vec2 ConstrainCursor(Vec2 p) => p;

    /// <summary>true なら実行前に図形の選択が必要。</summary>
    public virtual bool RequiresSelection => false;

    internal void Attach(CadCanvas canvas)
    {
        Canvas = canvas;
        OnActivated();
    }

    protected virtual void OnActivated() { }

    /// <summary>プロンプトを出し直す (選択・削除モードで、範囲選択の方法を切り替えたとき)。</summary>
    internal void RefreshPrompt() => OnActivated();
    public virtual void OnPoint(Vec2 p) { }

    /// <summary>マウスの左ボタンを押した。既定はクリック = 点の指定 (コマンド欄の座標入力は OnPoint に直接来る)。</summary>
    public virtual void OnPress(Vec2 p) => OnPoint(p);

    /// <summary>マウスの左ボタンを離した (ドラッグの終わり)。</summary>
    public virtual void OnRelease(Vec2 p) { }

    /// <summary>ダブルクリックの 2 回目。既定は普通のクリック (選択ではダブルクリックした文字を変える)。</summary>
    public virtual void OnDoubleClick(Vec2 p) => OnPress(p);

    /// <summary>true の間は左ドラッグ中 (キャンバスがマウスをキャプチャする)。</summary>
    public virtual bool IsDragging => false;
    public virtual void OnMouseMove(Vec2 p) { }
    public virtual void OnEnter() => Finish();

    /// <summary>Esc: 入力途中のものを取り消してコマンドを終える (選択では選択解除)。</summary>
    public virtual void OnCancel() => Finish();

    // ---- 右クリックメニュー ----

    /// <summary>「一つ戻す」ができる (指定済みの点などがある)。</summary>
    public virtual bool CanStepBack => false;

    /// <summary>直前の手順 (最後に指定した点など) を取り消す。</summary>
    public virtual void StepBack() { }

    /// <summary>Enter に当たる操作の名前 (「確定」「終了」など)。null なら右クリックメニューに出さない。</summary>
    public virtual string? ConfirmLabel => null;

    /// <summary>右クリックメニューに足す、ツール固有の項目。</summary>
    public virtual IEnumerable<(string Label, Action Run)> MenuItems => [];

    /// <summary>直前に作った図形を取り消す (元に戻すの一番上ならそれを戻し、違えば削除する)。</summary>
    protected void Retract(IUndoableCommand created, IReadOnlyList<Entity> entities)
    {
        if (Doc.History.Peek == created) Doc.History.Undo();
        else if (entities.Any(Doc.Entities.Contains)) Execute(new RemoveEntitiesCommand([.. entities.Where(Doc.Entities.Contains)], "取り消し"));
    }

    /// <summary>座標以外の文字入力 (半径・文字列・オプションなど)。処理したら true。</summary>
    public virtual bool OnText(string text) => false;

    public virtual void DrawPreview(DrawingContext dc) { }

    protected void Finish() => Canvas.SetTool(new SelectTool());

    protected void SetPrompt(string prompt)
    {
        Prompt = prompt;
        Canvas.RaisePromptChanged();
    }

    protected void Execute(IUndoableCommand command) => Doc.History.Execute(command);

    /// <summary>新しく描く図形に、現在の線幅・色 (プロパティの「作図の設定」) を付ける。</summary>
    protected T Styled<T>(T e) where T : Entity
    {
        e.LineWeight = Doc.CurrentLineWeight;
        e.Color = Doc.CurrentColor;
        e.LineType = Doc.CurrentLineType;
        return e;
    }

    protected static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}

public static class CoordinateInput
{
    /// <summary>
    /// "x,y" (絶対) / "@dx,dy" (相対) / "@距離&lt;角度" (極座標、角度は東から反時計回りの度) /
    /// "sX,Y" (測量座標 X=北, Y=東。doc があれば座標設定の向きで図面上の点にする) を解釈する。
    /// </summary>
    public static bool TryParse(string text, Vec2? basePoint, out Vec2 point, CadDocument? doc = null)
    {
        point = default;
        text = text.Trim().Replace('，', ',').Replace('＠', '@').Replace('＜', '<');
        if (text.StartsWith('s') || text.StartsWith('S'))
        {
            if (!CoordinateSetTool.TryParseSurvey(text, out var survey)) return false;
            point = doc?.FromSurveyCad(survey) ?? survey;
            return true;
        }
        var relative = text.StartsWith('@');
        if (relative)
        {
            if (basePoint == null) return false;
            text = text[1..];
        }

        Vec2 v;
        var polar = text.Split('<');
        var xy = text.Split(',');
        if (polar.Length == 2 && TryNum(polar[0], out var dist) && TryNum(polar[1], out var deg))
            v = Vec2.FromAngle(deg * Math.PI / 180) * dist;
        else if (xy.Length == 2 && TryNum(xy[0], out var x) && TryNum(xy[1], out var y))
            v = new Vec2(x, y);
        else
            return false;

        point = relative ? basePoint!.Value + v : v;
        return true;
    }

    private static bool TryNum(string s, out double v) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
}
