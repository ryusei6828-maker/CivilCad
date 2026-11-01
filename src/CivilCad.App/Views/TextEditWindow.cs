using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CivilCad.App.Tools;
using CivilCad.Core.Model;

namespace CivilCad.App.Views;

/// <summary>
/// 文字を変える小さな窓。Excel のセルのように、文字・大きさ・揃え・改行を自分で決める
/// (Enter で変更、Alt+Enter・Shift+Enter で改行、Esc でやめる)。
/// </summary>
public sealed class TextEditWindow : Window
{
    private readonly TextEntity _source;
    private readonly CadDocument _doc;
    private readonly TextBox _box;
    private readonly TextBox _height;
    private readonly RadioButton[] _h = new RadioButton[3];
    private readonly RadioButton[] _v = new RadioButton[3];
    private readonly CheckBox _fit;
    private readonly TextBlock _error;
    private readonly double _originalMm;

    public TextEditResult? Result { get; private set; }

    public TextEditWindow(TextEntity source, CadDocument doc)
    {
        _source = source;
        _doc = doc;
        Title = "文字の変更";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _box = new TextBox
        {
            Text = source.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 90,
            FontSize = 14,
            Margin = new Thickness(0, 4, 0, 10),
        };
        _box.PreviewKeyDown += Box_PreviewKeyDown;
        _box.TextChanged += (_, _) => UpdateFitEnabled();

        _originalMm = Math.Round(source.Height / doc.ScaleDenominator * 1000, 3);
        _height = new TextBox { Width = 70, VerticalContentAlignment = VerticalAlignment.Center };
        if (source.ScreenHeightPx != null)
        {
            _height.Text = "画面で一定";
            _height.IsEnabled = false;
        }
        else _height.Text = _originalMm.ToString("0.###", CultureInfo.InvariantCulture);
        _height.ToolTip = "用紙上の文字の高さ (mm)";

        var heightRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        heightRow.Children.Add(new TextBlock { Text = "文字高 (mm)", Width = 80, VerticalAlignment = VerticalAlignment.Center });
        heightRow.Children.Add(_height);

        var hRow = Group("横の揃え", ["左", "中央", "右"], _h, (int)source.HAlign);
        var vNow = source.VAlign switch { TextVAlign.Middle => 1, TextVAlign.Top => 2, _ => 0 };
        var vRow = Group("縦の揃え", ["下", "中", "上"], _v, vNow);

        _fit = new CheckBox
        {
            Content = "元の幅に合わせる (幅指定)",
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = source.FitWidth != null ? Visibility.Visible : Visibility.Collapsed,
            ToolTip = "チェックすると、文字全体を元の幅に伸ばす・縮める。1 行のときだけ使える",
        };

        _error = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap };

        var ok = new Button { Content = "変更", Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "キャンセル", Width = 80, IsCancel = true };
        ok.Click += (_, _) => Confirm();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "Enter で変更、Alt+Enter で改行、Esc でやめる" });
        panel.Children.Add(_box);
        panel.Children.Add(heightRow);
        panel.Children.Add(hRow);
        panel.Children.Add(vRow);
        panel.Children.Add(_fit);
        panel.Children.Add(_error);
        panel.Children.Add(buttons);
        Content = panel;
        UpdateFitEnabled();
        Loaded += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
            Keyboard.Focus(_box);
        };
    }

    private static StackPanel Group(string label, string[] names, RadioButton[] buttons, int selected)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new TextBlock { Text = label, Width = 80, VerticalAlignment = VerticalAlignment.Center });
        for (var i = 0; i < names.Length; i++)
        {
            buttons[i] = new RadioButton { Content = names[i], GroupName = label, Margin = new Thickness(0, 0, 14, 0), IsChecked = i == selected };
            row.Children.Add(buttons[i]);
        }
        return row;
    }

    /// <summary>複数行のときは幅指定は使えない。</summary>
    private void UpdateFitEnabled() => _fit.IsEnabled = !_box.Text.Contains('\n') && !_box.Text.Contains('\r');

    private void Box_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key != Key.Enter) return;
        if ((Keyboard.Modifiers & (ModifierKeys.Alt | ModifierKeys.Shift)) != 0)
        {
            var at = _box.SelectionStart;
            _box.SelectedText = "\n";
            _box.CaretIndex = at + 1;
        }
        else Confirm();
        e.Handled = true;
    }

    private static int Checked(RadioButton[] group) => Array.FindIndex(group, b => b.IsChecked == true);

    private void Confirm()
    {
        var heightM = _source.Height;
        if (_source.ScreenHeightPx == null)
        {
            var s = _height.Text.Trim();
            if ((!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var mm) && !double.TryParse(s, out mm)) || mm <= 0 || double.IsInfinity(mm))
            {
                _error.Text = "文字高は 0 より大きい数値 (mm) にしてください。";
                _height.Focus();
                return;
            }
            if (Math.Abs(mm - _originalMm) > 1e-9) heightM = _doc.PaperMmToMeters(mm); // 触っていなければ元の高さのまま
        }
        var h = (TextHAlign)Math.Max(0, Checked(_h));
        var vi = Checked(_v);
        var v = vi switch
        {
            1 => TextVAlign.Middle,
            2 => TextVAlign.Top,
            _ => _source.VAlign is TextVAlign.Baseline or TextVAlign.Bottom ? _source.VAlign : TextVAlign.Bottom,
        };
        var text = _box.Text.Replace("\r\n", "\n").Replace('\r', '\n');
        Result = new TextEditResult(text, heightM, h, v, _fit.Visibility == Visibility.Visible && _fit.IsEnabled && _fit.IsChecked == true);
        DialogResult = true;
    }

    /// <summary>窓を出して結果を返す (やめたら null)。</summary>
    public static TextEditResult? Ask(Window? owner, TextEntity source, CadDocument doc)
    {
        var w = new TextEditWindow(source, doc);
        if (owner != null && owner.IsVisible) w.Owner = owner;
        return w.ShowDialog() == true ? w.Result : null;
    }
}
