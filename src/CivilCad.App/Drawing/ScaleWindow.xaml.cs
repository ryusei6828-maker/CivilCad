using System.Globalization;
using System.Windows;

namespace CivilCad.App.Drawing;

public partial class ScaleWindow : Window
{
    public static IReadOnlyList<double> CommonScales { get; } = [50, 100, 200, 250, 300, 500, 1000, 2500, 5000];

    public ScaleWindow(double current, double? currentVertical = null)
    {
        InitializeComponent();
        ScaleBox.ItemsSource = CommonScales;
        ScaleBox.Text = current.ToString(CultureInfo.InvariantCulture);
        VerticalBox.ItemsSource = CommonScales;
        VerticalBox.Text = currentVertical is double v ? v.ToString(CultureInfo.InvariantCulture) : "";
        (s_lastMode == ScaleChangeMode.KeepReal ? KeepRealRadio : KeepPaperRadio).IsChecked = true;
        Loaded += (_, _) => ScaleBox.Focus();
    }

    public double Result { get; private set; }

    /// <summary>縦の縮尺 (null なら横と同じ)。</summary>
    public double? VerticalResult { get; private set; }

    /// <summary>すでに描いた図形の扱い。</summary>
    public ScaleChangeMode Mode { get; private set; }

    /// <summary>前回選んだ扱い (アプリを閉じるまで覚える)。</summary>
    private static ScaleChangeMode s_lastMode = ScaleChangeMode.KeepPaper;

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
        double? v = null;
        if (VerticalBox.Text.Trim().Length > 0)
        {
            if (!TryParse(VerticalBox.Text, out var vs))
            {
                ErrorText.Text = "縦の縮尺は正の数値にしてください (横と同じなら空)。";
                return;
            }
            if (Math.Abs(vs - s) > 1e-9) v = vs;
        }
        Result = s;
        VerticalResult = v;
        Mode = KeepRealRadio.IsChecked == true ? ScaleChangeMode.KeepReal : ScaleChangeMode.KeepPaper;
        s_lastMode = Mode;
        DialogResult = true;
    }
}
