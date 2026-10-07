using System.Globalization;
using System.Windows;

namespace CivilCad.App.Drawing;

public partial class ScaleWindow : Window
{
    public static IReadOnlyList<double> CommonScales { get; } = [50, 100, 200, 250, 300, 500, 1000, 2500, 5000];

    public ScaleWindow(double current)
    {
        InitializeComponent();
        ScaleBox.ItemsSource = CommonScales;
        ScaleBox.Text = current.ToString(CultureInfo.InvariantCulture);
        Loaded += (_, _) => ScaleBox.Focus();
    }

    public double Result { get; private set; }

    /// <summary>図枠・文字・記号を新しい縮尺に合わせる (用紙上の大きさを保つ)。</summary>
    public bool KeepPaperSize { get; private set; }

    private static bool TryParse(string text, out double scale) =>
        double.TryParse(text.Replace("1:", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out scale) && scale > 0;

    private void ScaleBox_TextChanged(object sender, RoutedEventArgs e) =>
        InfoText.Text = TryParse(ScaleBox.Text, out var s)
            ? $"用紙 1mm = 図面上 {s / 1000:0.####} m   (文字高 3.5mm → {3.5 * s / 1000:0.###} m、A3 の図枠 {380 * s / 1000:0.###} m)"
            : "";

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParse(ScaleBox.Text, out var s))
        {
            ErrorText.Text = "正の数値を入れてください (例: 500)。";
            return;
        }
        Result = s;
        KeepPaperSize = KeepPaperCheck.IsChecked == true;
        DialogResult = true;
    }
}
