using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace CivilCad.App.Views;

/// <summary>パネルの見出し (名前と、フロート・閉じるのボタン) の大きさ。どのパネルも同じにする。</summary>
public static class PanelHeader
{
    /// <summary>見出しの名前 (「座標管理」など) の文字の大きさ。</summary>
    public const double TitleFontSize = 18;

    /// <summary>フロート (⧉)・閉じる (×) のボタンの文字の大きさ。</summary>
    public const double ButtonFontSize = 20;

    public static readonly Thickness ButtonPadding = new(8, 0, 8, 2);

    /// <summary>Windows の窓の閉じるボタンと同じ赤 (カーソルを乗せたとき)。</summary>
    public static readonly Color CloseHoverColor = Color.FromRgb(0xC4, 0x2B, 0x1C);

    /// <summary>押している間の赤 (少し明るい)。</summary>
    public static readonly Color ClosePressedColor = Color.FromRgb(0xE0, 0x6A, 0x5F);

    /// <summary>
    /// 閉じる (×) のボタンを、Windows の窓の閉じるボタンと同じ見た目にする:
    /// ふだんは背景なし、カーソルを乗せると赤い背景に白い ×、押している間は少し明るい赤。
    /// </summary>
    public static void MakeCloseButton(Button button)
    {
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding(nameof(Control.Padding)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        template.Triggers.Add(ColorTrigger(UIElement.IsMouseOverProperty, CloseHoverColor));
        template.Triggers.Add(ColorTrigger(System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, ClosePressedColor)); // 後に書いた方が勝つ
        button.Template = template;
    }

    private static Trigger ColorTrigger(DependencyProperty property, Color background)
    {
        var brush = new SolidColorBrush(background);
        brush.Freeze();
        var trigger = new Trigger { Property = property, Value = true };
        trigger.Setters.Add(new Setter(Border.BackgroundProperty, brush, "Bd"));
        trigger.Setters.Add(new Setter(System.Windows.Documents.TextElement.ForegroundProperty, Brushes.White, "Bd"));
        return trigger;
    }
}

/// <summary>パネルの状態 (設定ファイルに保存する)。</summary>
public sealed class PanelState
{
    public bool Floating { get; set; }

    /// <summary>フロート時のウィンドウ位置・大きさ [Left, Top, Width, Height]。</summary>
    public double[]? Bounds { get; set; }
}

/// <summary>
/// メイン画面の端 (host) にドッキングするか、別ウィンドウに浮かせる (フロート) パネル。
/// 中身の要素は 1 つで、host とフロートのウィンドウの間を付け替える。
/// </summary>
public sealed class DockablePanel
{
    private readonly FrameworkElement _owner;
    private readonly Border _host;
    private readonly FrameworkElement _content;
    private readonly string _title;
    private readonly PanelState _state;
    private readonly Size _defaultSize;
    private readonly Func<Point> _defaultPosition;
    private Window? _window;

    /// <param name="defaultPosition">初めてフロートするときの位置 (画面座標, DIP)。</param>
    public DockablePanel(FrameworkElement owner, Border host, FrameworkElement content, string title, PanelState state,
        bool open, Size defaultSize, Func<Point> defaultPosition)
    {
        _owner = owner;
        _host = host;
        _content = content;
        _title = title;
        _state = state;
        _defaultSize = defaultSize;
        _defaultPosition = defaultPosition;
        IsOpen = open;
    }

    public bool IsOpen { get; private set; }
    public bool IsFloating => _state.Floating;

    /// <summary>本体を閉じる途中 (フロートのウィンドウの「閉じる」を止めない)。</summary>
    public bool ShuttingDown { get; set; }

    /// <summary>フロートのウィンドウで押されたキー (元に戻す等を本体に回すため)。</summary>
    public event KeyEventHandler? FloatingKeyDown;

    /// <summary>開閉・フロートの状態が変わった。</summary>
    public event EventHandler? StateChanged;

    public void SetOpen(bool open)
    {
        IsOpen = open;
        Update();
    }

    public void SetFloating(bool floating)
    {
        _state.Floating = floating;
        IsOpen = true;
        Update();
    }

    /// <summary>今の状態に合わせて中身を host かウィンドウに置く。本体の表示後に呼ぶこと。</summary>
    public void Update()
    {
        // いったん両方から外す (WPF の要素は親を 1 つしか持てない)
        if (_host.Child == _content) _host.Child = null;
        if (_window?.Content == _content) _window.Content = null;

        var docked = IsOpen && !IsFloating;
        if (docked) _host.Child = _content;
        _host.Visibility = docked ? Visibility.Visible : Visibility.Collapsed;

        if (IsOpen && IsFloating && _owner.IsLoaded)
        {
            var w = _window ??= CreateWindow();
            w.Content = _content;
            if (!w.IsVisible) w.Show();
        }
        else if (_window != null)
        {
            RememberBounds();
            _window.Hide();
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private Window CreateWindow()
    {
        var w = new Window
        {
            Title = _title,
            Owner = Window.GetWindow(_owner), // 本体より前に表示され、本体を最小化すると一緒に隠れる
            // 普通の窓のタイトルバーにする (ツールウィンドウの形式は、タイトルの文字と閉じるボタンが小さい)
            WindowStyle = WindowStyle.SingleBorderWindow,
            Icon = Window.GetWindow(_owner)?.Icon,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Width = _defaultSize.Width,
            Height = _defaultSize.Height,
        };

        // 前回の位置 (画面の外に出ていたら既定の位置)
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (_state.Bounds is [var l, var t, var width, var height] && screen.IntersectsWith(new Rect(l, t, width, height)))
        {
            (w.Left, w.Top, w.Width, w.Height) = (l, t, Math.Max(200, width), Math.Max(200, height));
        }
        else
        {
            var p = _defaultPosition();
            (w.Left, w.Top) = (p.X, p.Y);
        }

        // × で閉じたらパネルを閉じる扱い (ウィンドウは使い回す)
        w.Closing += (_, e) =>
        {
            if (ShuttingDown) return;
            e.Cancel = true;
            SetOpen(false);
        };
        w.LocationChanged += (_, _) => RememberBounds();
        w.SizeChanged += (_, _) => RememberBounds();
        w.PreviewKeyDown += (s, e) => FloatingKeyDown?.Invoke(s, e);
        w.SourceInitialized += (_, _) => HideMinMaxButtons(w);
        return w;
    }

    private const int GwlStyle = -16;
    private const int WsMinimizeBox = 0x20000;
    private const int WsMaximizeBox = 0x10000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    /// <summary>タイトルバーの最小化・最大化のボタンを消す (閉じるボタンだけ残す。パネルは本体と一緒に隠れるので要らない)。</summary>
    private static void HideMinMaxButtons(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        SetWindowLong(hwnd, GwlStyle, GetWindowLong(hwnd, GwlStyle) & ~(WsMinimizeBox | WsMaximizeBox));
    }

    private void RememberBounds()
    {
        if (_window is not { IsVisible: true, WindowState: WindowState.Normal } w) return;
        _state.Bounds = [w.Left, w.Top, w.ActualWidth, w.ActualHeight];
    }

    /// <summary>本体ウィンドウ内の点 (DIP) を画面座標 (DIP) にする。既定位置の計算用。</summary>
    public static Point OwnerPointToScreen(FrameworkElement owner, Point p)
    {
        var px = owner.PointToScreen(p);
        var dpi = VisualTreeHelper.GetDpi(owner);
        return new Point(px.X / dpi.DpiScaleX, px.Y / dpi.DpiScaleY);
    }
}
