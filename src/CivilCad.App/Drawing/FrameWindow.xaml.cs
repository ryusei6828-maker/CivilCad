using System.Globalization;
using System.Windows;
using CivilCad.Core.Geometry;

namespace CivilCad.App.Drawing;

public partial class FrameWindow : Window
{
    private static readonly double[] CommonScales = [10, 20, 50, 100, 200, 250, 300, 500, 1000, 2500, 5000];
    private readonly Box2? _extents;
    private bool _loaded;

    /// <param name="extents">描いてある図形 (図枠を除く) の範囲。「図形が収まる縮尺」と大きさの比較に使う。</param>
    public FrameWindow(DrawingFrameOptions options, Box2? extents = null)
    {
        InitializeComponent();
        _extents = extents;
        FitButton.IsEnabled = extents != null;
        PaperBox.ItemsSource = PaperSize.All;
        PaperBox.SelectedItem = options.PaperSize;
        ScaleBox.ItemsSource = CommonScales;
        ScaleBox.Text = options.ScaleDenominator.ToString(CultureInfo.InvariantCulture);
        MarginBox.Text = options.Margin.ToString(CultureInfo.InvariantCulture);
        TitleCheck.IsChecked = options.IncludeTitleBlock;
        ProjectBox.Text = options.ProjectName;
        DrawingNameBox.Text = options.DrawingName;
        DateBox.Text = options.Date;
        NumberBox.Text = options.DrawingNumber;
        CompanyBox.Text = options.Company;
        ClientBox.Text = options.Client;
        TitleFields.IsEnabled = options.IncludeTitleBlock;
        _loaded = true;
        UpdateSizeText();
    }

    public DrawingFrameOptions? Result { get; private set; }

    private bool TryRead(out DrawingFrameOptions o)
    {
        o = new DrawingFrameOptions
        {
            Paper = (PaperBox.SelectedItem as PaperSize)?.Name ?? "A1",
            IncludeTitleBlock = TitleCheck.IsChecked == true,
            ProjectName = ProjectBox.Text.Trim(),
            DrawingName = DrawingNameBox.Text.Trim(),
            Date = DateBox.Text.Trim(),
            DrawingNumber = NumberBox.Text.Trim(),
            Company = CompanyBox.Text.Trim(),
            Client = ClientBox.Text.Trim(),
        };
        if (!double.TryParse(ScaleBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) || scale <= 0)
        {
            ErrorText.Text = "縮尺には正の数値を入れてください (例: 500)。";
            return false;
        }
        if (!double.TryParse(MarginBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var margin) || margin < 0)
        {
            ErrorText.Text = "余白には 0 以上の数値を入れてください。";
            return false;
        }
        o.ScaleDenominator = scale;
        o.Margin = margin;
        var p = o.PaperSize;
        if (p.Width - 2 * margin <= 0 || p.Height - 2 * margin <= 0)
        {
            ErrorText.Text = "余白が大きすぎます。";
            return false;
        }
        if (o.IncludeTitleBlock && (p.Width - 2 * margin < DrawingFrame.TitleWidth || p.Height - 2 * margin < DrawingFrame.TitleHeight))
        {
            ErrorText.Text = "この用紙と余白では表題欄が入りません。";
            return false;
        }
        ErrorText.Text = "";
        return true;
    }

    private void UpdateSizeText()
    {
        if (!_loaded) return;
        if (!TryRead(out var o))
        {
            SizeText.Text = "";
            SizeHint.Text = "";
            return;
        }
        var (w, h) = DrawingFrame.FrameSize(o);
        var text = $"外枠の大きさ (図面上): {w:0.###} m × {h:0.###} m  (用紙 1mm = {o.MetersPerPaperMm:0.###} m)";
        SizeHint.Text = "";
        if (_extents is Box2 ext)
        {
            text += $"\n描いてある図形: {ext.Width:0.###} m × {ext.Height:0.###} m";
            var fit = DrawingFrame.FitScale(o, ext);
            if (!DrawingFrame.Fits(o, ext))
                SizeHint.Text = $"図形が外枠に収まりません。縮尺 1:{fit:0.##} 以上にしてください (「図形が収まる縮尺にする」で選べます)。";
            else if (fit < o.ScaleDenominator * (1 - 1e-9))
                SizeHint.Text = $"外枠に対して図形が小さめです。1:{fit:0.##} にすると図形を大きく描けます (「図形が収まる縮尺にする」)。";
        }
        SizeText.Text = text;
    }

    private void FitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_extents is not Box2 ext || !TryRead(out var o)) return;
        ScaleBox.Text = DrawingFrame.FitScale(o, ext).ToString(CultureInfo.InvariantCulture);
    }

    private void Input_Changed(object sender, RoutedEventArgs e) => UpdateSizeText();

    private void TitleCheck_Click(object sender, RoutedEventArgs e)
    {
        TitleFields.IsEnabled = TitleCheck.IsChecked == true;
        UpdateSizeText();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!TryRead(out var o)) return;
        Result = o;
        DialogResult = true;
    }
}
