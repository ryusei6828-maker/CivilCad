using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace CivilCad.App.Sections;

/// <summary>縦横断図の縮尺を入れる窓。前に使った値を覚えておく (外から開いたときもその値を使う)。</summary>
public sealed class SectionScaleWindow : Window
{
    /// <summary>前に使った縮尺。</summary>
    public static SectionScales Last { get; private set; } = new();

    private readonly TextBox _h = Box(), _v = Box(), _s = Box(), _cols = Box(), _dl = Box();

    public SectionScales? Result { get; private set; }

    public SectionScaleWindow(string routeName, bool hasProfile, bool hasSections)
    {
        Title = "縦横断図の縮尺";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = $"路線「{routeName}」の縦断図・横断図をそれぞれ新しいシートに作ります。", Margin = new Thickness(0, 0, 0, 8) });
        if (hasProfile)
        {
            panel.Children.Add(Header("縦断図"));
            panel.Children.Add(Row("横 (距離)  1 :", _h, Last.ProfileHorizontal));
            panel.Children.Add(Row("縦 (高さ)  1 :", _v, Last.ProfileVertical));
        }
        if (hasSections)
        {
            panel.Children.Add(Header("横断図"));
            panel.Children.Add(Row("縦横  1 :", _s, Last.Section));
            panel.Children.Add(Row("横に並べる数", _cols, Last.Columns));
            panel.Children.Add(Row("DL の刻み (m)", _dl, Last.DatumStep));
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var ok = new Button { Content = "作る", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "キャンセル", Width = 80, IsCancel = true };
        ok.Click += (_, _) =>
        {
            double h = Last.ProfileHorizontal, v = Last.ProfileVertical, s = Last.Section, c = Last.Columns, dl = Last.DatumStep;
            if (hasProfile && (!Pos(_h, out h) || !Pos(_v, out v)) || hasSections && (!Pos(_s, out s) || !Pos(_cols, out c) || !Pos(_dl, out dl)))
            {
                MessageBox.Show(this, "0 より大きい数を入れてください。", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Result = Last = new SectionScales(h, v, s, Math.Max(1, (int)Math.Round(c)), dl);
            DialogResult = true;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
    }

    private static TextBox Box() => new() { Width = 70 };

    private static TextBlock Header(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) };

    private static FrameworkElement Row(string label, TextBox box, double value)
    {
        box.Text = value.ToString("0.###", CultureInfo.InvariantCulture);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        row.Children.Add(new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(box);
        return row;
    }

    private static bool Pos(TextBox box, out double v) =>
        double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0;
}
