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
    }

    /// <summary>OK で確定したキー割り当て (コマンド名 → キー、"" は割り当てなし)。</summary>
    public Dictionary<string, string>? Result { get; private set; }

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
        StatusText.Text = "初期値に戻しました (OK で確定)。";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
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
