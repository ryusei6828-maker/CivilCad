using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CivilCad.App.Views;

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
    private readonly Window _owner;
    private readonly Border _host;
    private readonly FrameworkElement _content;
    private readonly string _title;
    private readonly PanelState _state;
    private readonly Size _defaultSize;
    private readonly Func<Point> _defaultPosition;
    private Window? _window;

    /// <param name="defaultPosition">初めてフロートするときの位置 (画面座標, DIP)。</param>
    public DockablePanel(Window owner, Border host, FrameworkElement content, string title, PanelState state,
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
            Owner = _owner, // 本体より前に表示され、本体を最小化すると一緒に隠れる
            WindowStyle = WindowStyle.ToolWindow,
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
        return w;
    }

    private void RememberBounds()
    {
        if (_window is not { IsVisible: true, WindowState: WindowState.Normal } w) return;
        _state.Bounds = [w.Left, w.Top, w.ActualWidth, w.ActualHeight];
    }

    /// <summary>本体ウィンドウ内の点 (DIP) を画面座標 (DIP) にする。既定位置の計算用。</summary>
    public static Point OwnerPointToScreen(Window owner, Point p)
    {
        var px = owner.PointToScreen(p);
        var dpi = VisualTreeHelper.GetDpi(owner);
        return new Point(px.X / dpi.DpiScaleX, px.Y / dpi.DpiScaleY);
    }
}
