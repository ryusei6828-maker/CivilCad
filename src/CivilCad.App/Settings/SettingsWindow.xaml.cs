using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace CivilCad.App.Settings;

public partial class SettingsWindow : Window
{
    private static readonly Brush CapturingBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xC2));
    private readonly List<KeyBindingRow> _rows;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _rows = CommandRegistry.All
            .Select(c => new KeyBindingRow(c) { Key = settings.KeyFor(c.Code) ?? "" })
            .ToList();
        BindingGrid.ItemsSource = _rows;

        // 表示タブ
        foreach (var (name, hex) in BackgroundPresets)
        {
            var b = new Button
            {
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(6, 2, 8, 2),
                Tag = hex,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new Border { Width = 26, Height = 16, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Background = BrushOf(hex)!, Margin = new Thickness(0, 0, 6, 0) },
                        new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
            b.Click += (_, _) => BackgroundBox.Text = hex;
            BackgroundChoices.Items.Add(b);
        }
        BackgroundBox.Text = settings.BackgroundColor ?? DefaultBackgroundHex;
        GridVisibleCheck.IsChecked = settings.GridVisible;
        GridSpacingBox.ItemsSource = GridSpacingChoices;
        GridSpacingBox.Text = FormatSpacing(settings.GridSpacing);
    }

    public const string DefaultBackgroundHex = "#1E1E1E";

    private static readonly (string Name, string Hex)[] BackgroundPresets =
    [
        ("濃い灰 (既定)", DefaultBackgroundHex), ("黒", "#000000"), ("紺", "#1B2533"), ("灰", "#3C3F41"),
        ("薄い灰", "#E8E8E8"), ("白", "#FFFFFF"), ("クリーム", "#FAF6E8"),
    ];

    public static readonly string[] GridSpacingChoices = ["自動", "0.1", "0.5", "1", "5", "10", "20", "50", "100", "500", "1000"];

    public static string FormatSpacing(double? m) => m is double v ? v.ToString("0.###", CultureInfo.InvariantCulture) : "自動";

    /// <summary>「自動」や空欄は null。数値でなければ false。</summary>
    public static bool TryParseSpacing(string text, out double? spacing)
    {
        spacing = null;
        var t = text.Trim().TrimEnd('m', 'ｍ').Trim();
        if (t is "" or "自動") return true;
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0)
        {
            spacing = v;
            return true;
        }
        return false;
    }

    /// <summary>"#RRGGBB" → 色。読めなければ null。</summary>
    public static CivilCad.Core.Model.RgbColor? ParseHex(string text)
    {
        var t = text.Trim().TrimStart('#');
        if (t.Length != 6 || !int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return null;
        return new CivilCad.Core.Model.RgbColor((byte)(v >> 16), (byte)(v >> 8 & 0xFF), (byte)(v & 0xFF));
    }

    private static Brush? BrushOf(string hex) =>
        ParseHex(hex) is { } c ? new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)) : null;

    private void BackgroundBox_TextChanged(object sender, TextChangedEventArgs e) =>
        BackgroundPreview.Background = BrushOf(BackgroundBox.Text) ?? Brushes.Transparent;

    /// <summary>OK で確定したキー割り当て (コマンド名 → キー、"" は割り当てなし)。</summary>
    public Dictionary<string, string>? Result { get; private set; }

    /// <summary>OK で確定した背景色 (#RRGGBB)・グリッド。</summary>
    public string? BackgroundResult { get; private set; }
    public bool GridVisibleResult { get; private set; }
    public double? GridSpacingResult { get; private set; }

    private void KeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: KeyBindingRow row }) return;

        // Tab は項目の移動に使う
        if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None) return;
        e.Handled = true;

        if (KeyChord.FromEvent(e) is not KeyChord chord) return; // 修飾キーだけ押した

        if (chord.Modifiers == ModifierKeys.None && chord.Key is Key.Back or Key.Delete)
        {
            row.Key = "";
            StatusText.Text = $"「{row.Name}」の割り当てを解除しました。";
            return;
        }
        if (chord.IsReserved)
        {
            StatusText.Text = $"{chord} はアプリで決まった役割があるため割り当てできません。";
            return;
        }

        var text = chord.ToString();
        var other = _rows.FirstOrDefault(r => r != row && r.Key == text);
        if (other != null) other.Key = "";
        row.Key = text;
        StatusText.Text = other != null
            ? $"{text} を「{other.Name}」から「{row.Name}」に付け替えました。"
            : $"「{row.Name}」に {text} を割り当てました。" +
              (chord.IsCharacterKey ? " (英字キー単独は、コマンド欄が空のときに働きます)" : "");
    }

    private void KeyBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box) box.Background = CapturingBrush;
    }

    private void KeyBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box) box.ClearValue(BackgroundProperty);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: KeyBindingRow row }) row.Key = "";
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _rows)
            r.Key = CommandRegistry.DefaultBindings.TryGetValue(r.Code, out var k) ? k : "";
        BackgroundBox.Text = DefaultBackgroundHex;
        GridVisibleCheck.IsChecked = true;
        GridSpacingBox.Text = "自動";
        StatusText.Text = "初期値に戻しました (OK で確定)。";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (ParseHex(BackgroundBox.Text) is not { } bg)
        {
            StatusText.Text = "背景色は #RRGGBB の形で入力してください (例: #FFFFFF)。";
            return;
        }
        if (!TryParseSpacing(GridSpacingBox.Text, out var spacing))
        {
            StatusText.Text = "グリッドの間隔は 0 より大きい数値 (m) か「自動」にしてください。";
            return;
        }
        BackgroundResult = bg.ToHex();
        GridVisibleResult = GridVisibleCheck.IsChecked == true;
        GridSpacingResult = spacing;
        Result = _rows.ToDictionary(r => r.Code, r => r.Key);
        DialogResult = true;
    }
}

public sealed class KeyBindingRow(CommandInfo command) : INotifyPropertyChanged
{
    private string _key = "";

    public string Code => command.Code;
    public string Name => command.Name;
    public string? IconKey => command.IconKey;
    public string? Note => command.Note;

    public string Key
    {
        get => _key;
        set
        {
            if (_key == value) return;
            _key = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

/// <summary>アイコンのリソース名 → 画像。</summary>
public sealed class IconKeyToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string key ? Application.Current.TryFindResource(key) as ImageSource : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
