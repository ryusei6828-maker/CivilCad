using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;

namespace CivilCad.App;

/// <summary>別ウィンドウに出したシート 1 枚。中身は作図領域だけ (コマンド・パネルは本体の画面のものを使う)。</summary>
public sealed class SheetWindow : Window
{
    public Sheet Sheet { get; }

    /// <summary>作図領域を載せる場所 (編集中はここに本体の作図領域が入る)。</summary>
    public Grid Root { get; } = new();

    /// <summary>編集していない間の「見るだけ」の表示 (拡大・移動はできる。クリックするとこの窓が編集の対象になる)。</summary>
    public CadCanvas Mirror { get; } = new();

    public SheetWindow(Sheet sheet)
    {
        Sheet = sheet;
        Width = 900;
        Height = 650;
        Content = Root;
        Root.Children.Add(Mirror);
        SetBinding(TitleProperty, new System.Windows.Data.Binding(nameof(Sheet.Title)) { Source = sheet, StringFormat = "{0} - CivilCad" });
    }
}

/// <summary>
/// 分割画面: シートのタブを外へドラッグすると、そのシートが別ウィンドウになる。
/// コマンド・パネルは本体の画面のまま。クリックした方の窓のシートが編集の対象になる。
/// 編集用の作図領域 (Canvas と、その上の文字の入力欄など = EditSurface) は 1 つだけで、編集の対象の窓へ移す。
/// ほかの窓は「見るだけ」の作図領域 (同じ図面を映すので、編集はすぐ映る) を出す。
/// </summary>
public partial class MainWindow
{
    private readonly Dictionary<Sheet, SheetWindow> _popups = [];
    private CadCanvas? _mainMirror; // 本体の画面の「見るだけ」の表示 (別ウィンドウのシートを編集している間)
    private Sheet? _mainSheet;      // 本体の画面に出しているシート

    /// <summary>タブをこれだけ (px) タブの並びの上下に離して放すと、別ウィンドウにする。</summary>
    public const double TabTearDistance = 40;

    /// <summary>別ウィンドウに出しているシート。</summary>
    public IReadOnlyCollection<Sheet> PoppedOutSheets => _popups.Keys;

    /// <summary>別ウィンドウ (無ければ null)。</summary>
    public SheetWindow? PopupOf(Sheet sheet) => _popups.GetValueOrDefault(sheet);

    /// <summary>本体の画面に出しているシート。</summary>
    public Sheet? MainSheet => _mainSheet;

    /// <summary>編集用の作図領域が今載っている場所 (本体の画面か、別ウィンドウ)。</summary>
    public Panel? EditSurfaceHost => EditSurface.Parent as Panel;

    /// <summary>
    /// シートを別ウィンドウに出す。本体の画面にはシートを 1 枚は残す。
    /// position は窓の左上 (画面の位置、px。無ければ既定)。show = false は窓を表示しない (テスト用)。
    /// </summary>
    public bool PopOutSheet(Sheet sheet, Point? position = null, bool show = true)
    {
        if (_popups.TryGetValue(sheet, out var existing))
        {
            if (show) existing.Activate();
            return false;
        }
        if (!_sheets.Contains(sheet)) return false;
        if (_sheets.Count(s => !_popups.ContainsKey(s)) <= 1)
        {
            Log("別ウィンドウ: 本体の画面にシートを 1 枚は残します (ほかのシートを開いてから、タブを外へドラッグしてください)。");
            return false;
        }

        var w = new SheetWindow(sheet);
        CopyCanvasLook(w.Mirror);
        // 見るだけの表示をクリックしたら、この窓のシートを編集の対象にする (拡大・移動の中ボタン・ホイールはそのまま通す)
        w.Mirror.PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle) return;
            ActivateByClick(sheet);
            e.Handled = true;
        };
        w.Activated += (_, _) => { if (_popups.ContainsKey(sheet)) ActivateByClick(sheet); };
        // キーと「元に戻す」などは本体の画面で扱う (コマンド欄は本体にある)
        w.CommandBindings.AddRange(CommandBindings);
        w.PreviewKeyDown += (s, e) => HandleWindowKey(s, e);
        w.Closing += (_, _) => DockSheet(sheet);
        if (show && Window.GetWindow(this) is { } owner)
        {
            w.Owner = owner; // 本体を閉じたら一緒に閉じる
            if (position is Point p && PresentationSource.FromVisual(owner) is { CompositionTarget: { } target })
            {
                var dip = target.TransformFromDevice.Transform(p);
                w.WindowStartupLocation = WindowStartupLocation.Manual;
                (w.Left, w.Top) = (dip.X - 80, dip.Y - 12); // つかんだあたりがタイトルバーに来るように
            }
        }

        _popups[sheet] = w;
        if (_mainSheet == sheet || _mainSheet == null) _mainSheet = NearestMainSheet(sheet);
        PlaceEditSurface();
        if (show) w.Show();
        Log($"シート「{SheetName(sheet)}」を別ウィンドウにしました。クリックした方の窓が編集の対象です (窓を閉じると本体のタブに戻ります)。");
        return true;
    }

    /// <summary>
    /// 「見るだけ」の表示をクリックした: そのシートを編集の対象にする。
    /// 使っていたコマンド (線など) は、切り替えた先のシートで最初からもう一度始める (本体で選んだコマンドを、クリックした窓で続けて使える)。
    /// </summary>
    public void ActivateByClick(Sheet sheet)
    {
        var code = Canvas.Tool is Tools.SelectTool ? null : Canvas.Tool.CommandCode;
        ActivateSheet(sheet);
        if (code != null && Canvas.Tool is Tools.SelectTool) RunCommand(code);
    }

    /// <summary>別ウィンドウのシートを本体の画面に戻す (窓を閉じたとき)。シートは閉じない。</summary>
    private void DockSheet(Sheet sheet)
    {
        if (!_popups.Remove(sheet)) return;
        if (_active == sheet) _mainSheet = sheet; // 編集していたシートは、そのまま本体の画面で続ける
        PlaceEditSurface();
    }

    /// <summary>別ウィンドウを閉じる (シートを閉じるとき・本体に戻すとき)。</summary>
    public void ClosePopup(Sheet sheet)
    {
        if (!_popups.TryGetValue(sheet, out var w)) return;
        DockSheet(sheet);
        w.Close();
    }

    /// <summary>sheet の近く (タブの並びで右、無ければ左) の、本体の画面にあるシート。</summary>
    private Sheet? NearestMainSheet(Sheet sheet)
    {
        var i = _sheets.IndexOf(sheet);
        return _sheets.Skip(i + 1).FirstOrDefault(s => !_popups.ContainsKey(s))
            ?? _sheets.Take(Math.Max(i, 0)).LastOrDefault(s => !_popups.ContainsKey(s));
    }

    /// <summary>
    /// 編集用の作図領域を、編集の対象のシートの窓 (別ウィンドウか本体) に載せ、ほかの窓は「見るだけ」の表示にする。
    /// シートの切り替え・別ウィンドウの開け閉めのたびに呼ぶ。
    /// </summary>
    private void PlaceEditSurface()
    {
        // 本体の画面のシートが無くなっていたら (閉じた・別ウィンドウにした) 選び直す。1 枚も無ければ別ウィンドウを 1 つ戻す
        if (_mainSheet == null || !_sheets.Contains(_mainSheet) || _popups.ContainsKey(_mainSheet))
        {
            _mainSheet = _sheets.FirstOrDefault(s => !_popups.ContainsKey(s));
            if (_mainSheet == null && _popups.Count > 0)
            {
                ClosePopup(_popups.Keys.First());
                return;
            }
        }

        var popup = _active != null ? _popups.GetValueOrDefault(_active) : null;
        Panel target = popup != null ? popup.Root : CanvasHost;
        if (EditSurface.Parent != target)
        {
            (EditSurface.Parent as Panel)?.Children.Remove(EditSurface);
            target.Children.Add(EditSurface);
        }

        foreach (var (sheet, w) in _popups) ShowMirror(w.Mirror, sheet, sheet != _active);
        if (popup != null && _mainSheet != null)
        {
            if (_mainMirror == null)
            {
                _mainMirror = new CadCanvas();
                _mainMirror.PreviewMouseDown += (_, e) =>
                {
                    if (e.ChangedButton == MouseButton.Middle || _mainSheet == null) return;
                    ActivateByClick(_mainSheet);
                    e.Handled = true;
                };
                CanvasHost.Children.Insert(0, _mainMirror);
            }
            CopyCanvasLook(_mainMirror);
            ShowMirror(_mainMirror, _mainSheet, true);
        }
        else if (_mainMirror != null)
        {
            ShowMirror(_mainMirror, null, false);
        }
    }

    /// <summary>「見るだけ」の表示を出す / 隠す。出すときは、そのシートを最後に見ていた位置・拡大率で。</summary>
    private static void ShowMirror(CadCanvas mirror, Sheet? sheet, bool visible)
    {
        var wasVisible = mirror.Visibility == Visibility.Visible;
        mirror.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible || sheet == null) return;
        var changed = mirror.Document != sheet.Doc;
        if (changed) mirror.Document = sheet.Doc;
        if ((changed || !wasVisible) && sheet.ViewCenter is Vec2 c && sheet.ViewScale is double s) mirror.SetView(c, s);
    }

    /// <summary>シートを「見るだけ」で出している表示 (本体か別ウィンドウ)。出していなければ null。</summary>
    private CadCanvas? MirrorShowing(Sheet sheet)
    {
        if (_popups.TryGetValue(sheet, out var w)) return w.Mirror.Visibility == Visibility.Visible ? w.Mirror : null;
        return _mainMirror is { Visibility: Visibility.Visible } m && _mainSheet == sheet ? m : null;
    }

    /// <summary>背景色・グリッドを編集用の作図領域に合わせる。</summary>
    private void CopyCanvasLook(CadCanvas mirror)
    {
        mirror.BackgroundColor = Canvas.BackgroundColor;
        mirror.GridVisible = Canvas.GridVisible;
        mirror.GridSpacing = Canvas.GridSpacing;
    }

    /// <summary>シートを切り替える前: 「見るだけ」の表示で動かした位置・拡大率を、編集でもそのまま使う。</summary>
    private void TakeMirrorView(Sheet sheet)
    {
        if (MirrorShowing(sheet) is not { } mirror) return;
        sheet.ViewCenter = mirror.ViewCenter;
        sheet.ViewScale = mirror.Scale;
    }

    /// <summary>シートを切り替えた後: 作図領域を載せ替え、別ウィンドウのシートならその窓を前に出す。</summary>
    private void OnSheetActivatedForSplit(Sheet sheet)
    {
        if (!_popups.ContainsKey(sheet)) _mainSheet = sheet;
        PlaceEditSurface();
        if (_popups.TryGetValue(sheet, out var w) && w.IsVisible && !w.IsActive) w.Activate();
    }

    /// <summary>タブを放した位置 (SheetTabs の中の座標) が、タブの並びから上下に十分離れているか (別ウィンドウにする)。</summary>
    public static bool IsTearOff(Point p, double tabsHeight) => p.Y < -TabTearDistance || p.Y > tabsHeight + TabTearDistance;
}
