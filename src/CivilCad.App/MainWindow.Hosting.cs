using System.ComponentModel;
using System.Windows;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App;

/// <summary>「スケッチへエクスポート」で CivilCad3D へ渡す内容。</summary>
/// <param name="Document">送り元のシート (図面の縮尺・座標の向きを持つ)。</param>
/// <param name="Entities">送る図形。選んだ図形があればそれ、無ければシートの表示中の図形すべて。</param>
/// <param name="FromSelection">選んだ図形を送るとき true。</param>
/// <param name="SheetName">送り元のシートの名前 (ファイル名に使う)。</param>
/// <param name="ReferencePoint">移動基準点 (CAD 座標)。スケッチで置くときにカーソルに付く点。決めていなければ null (図面の原点)。</param>
/// <param name="SheetPath">送り元のシートのファイル。まだ保存していないシートは null (CivilCad3D の「リンク」が送り元を覚えるのに使う)。</param>
public sealed record SketchExportRequest(CadDocument Document, IReadOnlyList<Entity> Entities, bool FromSelection, string SheetName, Vec2? ReferencePoint = null, string? SheetPath = null);

/// <summary>
/// 画面 (MainWindow) を「部品」として載せる側 (CivilCad.exe の <see cref="ShellWindow"/>・CivilCad3D の図面タブ) とのつなぎ。
/// 画面はもとは Window だったので、窓の持ち物 (窓への変換・Title・Closing・Closed・Close・Activate) をここで同じ形で用意し、
/// MainWindow.xaml.cs の呼び出し (Owner = this、ShowDialog(this)、MessageBox.Show(this, ...) など) を変えずに済ませている。
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// 載っている窓への変換。ダイアログの Owner などに「this」をそのまま渡せる。
    /// まだ窓に載っていなければ、アプリの主窓。
    /// </summary>
    public static implicit operator Window(MainWindow m) => (Window.GetWindow(m) ?? Application.Current?.MainWindow)!;

    private string _title = "CivilCad";

    /// <summary>窓のタイトルにするとき用の文字 (シート名・縮尺・座標系)。</summary>
    public string Title
    {
        get => _title;
        private set
        {
            if (_title == value) return;
            _title = value;
            TitleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? TitleChanged;

    /// <summary>閉じる前の確認 (保存していないシートがあれば確認を出す)。Cancel にすると閉じない。</summary>
    public event CancelEventHandler? Closing;

    /// <summary>閉じた。</summary>
    public event EventHandler? Closed;

    /// <summary>載せている窓が閉じるとき (または閉じられるか知りたいとき) に呼ぶ。閉じてよければ true。</summary>
    public bool RequestClose()
    {
        var e = new CancelEventArgs();
        Closing?.Invoke(this, e);
        return !e.Cancel;
    }

    /// <summary>窓が閉じたあとに呼ぶ。</summary>
    public void NotifyClosed() => Closed?.Invoke(this, EventArgs.Empty);

    /// <summary>載っている窓を閉じる (「終了」)。</summary>
    public void Close() => Window.GetWindow(this)?.Close();

    /// <summary>載っている窓を前に出す。</summary>
    public void Activate() => Window.GetWindow(this)?.Activate();

    /// <summary>載っている窓の状態・手前表示・アクティブか (OpenFromArgs・FocusCommandBox が使う)。窓が無ければ通常・false。</summary>
    private WindowState WindowState
    {
        get => Window.GetWindow(this)?.WindowState ?? WindowState.Normal;
        set { if (Window.GetWindow(this) is { } w) w.WindowState = value; }
    }

    private bool Topmost
    {
        get => Window.GetWindow(this)?.Topmost ?? false;
        set { if (Window.GetWindow(this) is { } w) w.Topmost = value; }
    }

    private bool IsActive => Window.GetWindow(this)?.IsActive ?? false;

    /// <summary>窓のキー入力を画面に渡す (フォーカスが窓そのものにあるとき用)。</summary>
    internal void HandleWindowKey(object sender, System.Windows.Input.KeyEventArgs e) => Window_PreviewKeyDown(sender, e);

    // ---- CivilCad3D に載せたときだけの機能 ----

    private Action<SketchExportRequest>? _sketchExport;

    /// <summary>
    /// 「スケッチへエクスポート」の受け取り先。設定すると図面タブのリボンにボタンが出て、「終了」は隠れる
    /// (CivilCad3D に載せたとき。CivilCad.exe 単体では null のまま)。
    /// </summary>
    public Action<SketchExportRequest>? SketchExport
    {
        get => _sketchExport;
        set
        {
            _sketchExport = value;
            SketchExportButton.Visibility = value != null ? Visibility.Visible : Visibility.Collapsed;
            MoveReferenceButton.Visibility = SketchExportButton.Visibility;
            if (value == null) SketchLink = null;
            Canvas.OverlayExtra = value != null ? DrawMoveReference : null;
            Canvas.RedrawOverlay();
            BackstageExitButton.Visibility = value != null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private Action<SketchExportRequest>? _sketchLink;

    /// <summary>
    /// 「リンク」の受け取り先。設定するとリボンに「リンク」のボタンが出る (CivilCad3D に載せたときだけ)。
    /// 押すと、今のシートの全体 (表示中の図形すべて) を渡す。CivilCad3D は、このシートから送った下絵を今の図面の中身で作り直す。
    /// </summary>
    public Action<SketchExportRequest>? SketchLink
    {
        get => _sketchLink;
        set
        {
            _sketchLink = value;
            LinkButton.Visibility = value != null ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 今のシートから送る図形を決める: 選んでいる図形があればそれ、無ければ表示中の図形すべて (図形が無ければ空)。
    /// </summary>
    public SketchExportRequest BuildSketchExport()
    {
        var selected = Canvas.Selection.Where(_doc.IsVisible).ToList();
        return selected.Count > 0
            ? new SketchExportRequest(_doc, selected, true, _active.FileName, _active.MoveReference, _active.FilePath)
                        : new SketchExportRequest(_doc, _doc.Entities.Where(_doc.IsVisible).ToList(), false, _active.FileName, _active.MoveReference, _active.FilePath);
    }

    /// <summary>「リンク」: 今のシートの全体を CivilCad3D へ渡す (送った下絵を作り直してもらう)。</summary>
    private void SketchLinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sketchLink == null) return;
        _sketchLink(new SketchExportRequest(_doc, _doc.Entities.Where(_doc.IsVisible).ToList(), false, _active.FileName, _active.MoveReference, _active.FilePath));
    }

    private void SketchExport_Click(object sender, RoutedEventArgs e)
    {
        if (_sketchExport == null) return;
        var request = BuildSketchExport();
        if (request.Entities.Count == 0)
        {
            Log("スケッチへエクスポート: 送る図形がありません。");
            return;
        }
        _sketchExport(request);
    }

    // ---- 移動基準点 (「スケッチへ」で送ったあと、スケッチのカーソルに付ける点) ----

    /// <summary>「移動基準点」: 図面の上で 1 点をクリックして決める (吸着が効く。Esc でやめる)。</summary>
    private void MoveReference_Click(object sender, RoutedEventArgs e)
    {
        if (_sketchExport == null) return;
        var sheet = _active;
        Log("移動基準点をクリック (Esc でやめる)");
        StartTool(new MoveReferenceTool(p =>
        {
            sheet.MoveReference = p;
            Canvas.RedrawOverlay();
            var (x, y) = sheet.Doc.ToSurvey(p);
            Log($"移動基準点: X={x:F3} Y={y:F3}");
        }), "MR");
    }

    /// <summary>決めた移動基準点の印 (十字と丸)。シートごとに覚えている。</summary>
    private void DrawMoveReference(System.Windows.Media.DrawingContext dc)
    {
        if (_active?.MoveReference is not { } p) return;
        var c = Canvas.WorldToScreen(p);
        dc.DrawEllipse(null, CadCanvas.PreviewPen, c, 6, 6);
        dc.DrawLine(CadCanvas.PreviewPen, new System.Windows.Point(c.X - 10, c.Y), new System.Windows.Point(c.X + 10, c.Y));
        dc.DrawLine(CadCanvas.PreviewPen, new System.Windows.Point(c.X, c.Y - 10), new System.Windows.Point(c.X, c.Y + 10));
    }
}
