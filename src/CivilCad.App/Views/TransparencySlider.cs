using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace CivilCad.App.Views;

/// <summary>
/// 透明度 (%) のスライダーと数字の欄。スライダーをドラッグしている間は preview を呼び (図面にすぐ映す)、
/// 離したとき・クリックやキーで動かしたとき・数字を入れて Enter のときに commit を 1 回呼ぶ (元に戻すの 1 回分)。
/// </summary>
public sealed class TransparencySlider : DockPanel
{
    private readonly Action<double> _preview;
    private readonly Action<double> _commit;
    private bool _dragging;
    private double _committed;

    public Slider Slider { get; } = new()
    {
        Minimum = 0,
        Maximum = 100,
        SmallChange = 1,
        LargeChange = 10,
        IsMoveToPointEnabled = true,
        VerticalAlignment = System.Windows.VerticalAlignment.Center,
        ToolTip = "左 = 不透明、右 = 透明。下の線や文字・画像を透かして見せたいときに",
    };

    public TextBox Box { get; } = new() { Width = 42, Margin = new Thickness(6, 0, 0, 0), ToolTip = "数字で入れて Enter (0〜100)" };

    /// <param name="start">今の透明度 (選んだ図形で違えば null)。</param>
    public TransparencySlider(double? start, Action<double> preview, Action<double> commit)
    {
        _preview = preview;
        _commit = commit;
        _committed = start ?? 0;
        Slider.Value = _committed;
        Box.Text = start is double s ? Format(s) : "";

        Slider.ValueChanged += (_, e) =>
        {
            Box.Text = Format(e.NewValue);
            if (_dragging) _preview(e.NewValue);
            else Commit(e.NewValue); // クリック・キー操作は 1 回ごとに確定
        };
        Slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => _dragging = true));
        Slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
        {
            _dragging = false;
            Commit(Slider.Value);
        }));
        Box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            if (double.TryParse(Box.Text.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v is >= 0 and <= 100)
                Commit(v);
            else
                Box.Text = Format(Slider.Value);
            e.Handled = true;
        };

        SetDock(Box, Dock.Right);
        Children.Add(Box);
        Children.Add(Slider);
    }

    private static string Format(double v) => v.ToString("0", CultureInfo.InvariantCulture);

    private void Commit(double v)
    {
        v = Math.Round(Math.Clamp(v, 0, 100));
        if (Math.Abs(v - _committed) < 1e-9) return;
        _committed = v;
        _commit(v);
    }
}
