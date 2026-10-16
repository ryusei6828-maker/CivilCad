using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using CivilCad.App.Tools;

namespace CivilCad.App.Views;

/// <summary>座標の描画 (CD) で描く点の範囲。</summary>
public enum CoordinateDrawTarget { Selected, Listed, All }

/// <summary>座標の描画の窓: 描く点 (選んだ点・一覧の点・すべて)、マークの種類、大きさ、点名・標高を描くか。</summary>
public sealed class CoordinateDrawWindow : Window
{
    private readonly RadioButton _selected, _listed, _all;
    private readonly List<(RadioButton Button, PointMarkKind Kind)> _kinds = [];
    private readonly TextBox _size, _textHeight;
    private readonly CheckBox _name, _elevation;

    public CoordinateDrawTarget Target { get; private set; }

    /// <summary>true なら描かずに、描いてあるマーク・点名を消す。</summary>
    public bool Erase { get; private set; }

    public CoordinateDrawWindow(int selected, int listed, int all, string listedLabel)
    {
        Title = "座標の描画";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var s = PointMarkSettings.Current;

        var panel = new StackPanel { Margin = new Thickness(14), MinWidth = 340 };
        TextBlock Head(string text) => new() { Text = text, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 4) };

        panel.Children.Add(new TextBlock { Text = "描く点", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
        _selected = new RadioButton { Content = $"選んだ点 ({selected} 点)", IsEnabled = selected > 0, GroupName = "target", Margin = new Thickness(0, 0, 0, 2) };
        _listed = new RadioButton { Content = $"{listedLabel} ({listed} 点)", IsEnabled = listed > 0, GroupName = "target", Margin = new Thickness(0, 0, 0, 2) };
        _all = new RadioButton { Content = $"登録したすべての点 ({all} 点)", IsEnabled = all > 0, GroupName = "target" };
        (selected > 0 ? _selected : listed > 0 && listed < all ? _listed : _all).IsChecked = true;
        panel.Children.Add(_selected);
        if (listed != all) panel.Children.Add(_listed);
        panel.Children.Add(_all);

        panel.Children.Add(Head("マーク"));
        var grid = new UniformGrid { Columns = 4 };
        foreach (var kind in Enum.GetValues<PointMarkKind>())
        {
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(Preview(kind));
            content.Children.Add(new TextBlock { Text = CoordinateMarks.Label(kind), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center });
            var b = new RadioButton
            {
                Content = content,
                GroupName = "kind",
                IsChecked = kind == s.Kind,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(4, 2, 4, 2),
                ToolTip = CoordinateMarks.Label(kind),
            };
            _kinds.Add((b, kind));
            grid.Children.Add(b);
        }
        panel.Children.Add(grid);

        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        sizeRow.Children.Add(new TextBlock { Text = "大きさ (用紙上の mm)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _size = new TextBox { Text = F(s.SizeMm), Width = 60, ToolTip = "〇の直径 (用紙上)。図面の縮尺で m に換算" };
        sizeRow.Children.Add(_size);
        panel.Children.Add(sizeRow);

        panel.Children.Add(Head("文字"));
        _name = new CheckBox { Content = "点名も描く", IsChecked = s.DrawName, Margin = new Thickness(0, 0, 0, 4), ToolTip = "マークの右上に点名を書く。オフならマークだけ" };
        _elevation = new CheckBox { Content = "標高も描く (H=…、標高のある点だけ)", IsChecked = s.DrawElevation, Margin = new Thickness(0, 0, 0, 4) };
        panel.Children.Add(_name);
        panel.Children.Add(_elevation);
        var textRow = new StackPanel { Orientation = Orientation.Horizontal };
        textRow.Children.Add(new TextBlock { Text = "文字高 (用紙上の mm)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _textHeight = new TextBox { Text = F(s.TextHeightMm), Width = 60 };
        textRow.Children.Add(_textHeight);
        panel.Children.Add(textRow);

        panel.Children.Add(new TextBlock
        {
            Text = "マークと点名はレイヤ「座標-マーク」「座標-マーク点名」に描きます。同じ点をもう一度描くと、前のマークを置き換えます。",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 340,
            Foreground = Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(0, 10, 0, 0),
        });

        var buttons = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var erase = new Button { Content = "マークを消す", Padding = new Thickness(8, 1, 8, 1), ToolTip = "描く点に描いてあるマーク・点名を消す" };
        var ok = new Button { Content = "描画", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "キャンセル", Width = 80, IsCancel = true };
        ok.Click += (_, _) => Close(erase: false);
        erase.Click += (_, _) => Close(erase: true);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(ok);
        right.Children.Add(cancel);
        DockPanel.SetDock(erase, Dock.Left);
        buttons.Children.Add(erase);
        buttons.Children.Add(right);
        panel.Children.Add(buttons);
        Content = panel;
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static bool TryPositive(TextBox box, out double v) =>
        double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0 && double.IsFinite(v);

    private void Close(bool erase)
    {
        if (!TryPositive(_size, out var size)) { _size.Focus(); _size.SelectAll(); return; }
        if (!TryPositive(_textHeight, out var th)) { _textHeight.Focus(); _textHeight.SelectAll(); return; }
        var s = PointMarkSettings.Current;
        s.Kind = _kinds.First(k => k.Button.IsChecked == true).Kind;
        s.SizeMm = size;
        s.TextHeightMm = th;
        s.DrawName = _name.IsChecked == true;
        s.DrawElevation = _elevation.IsChecked == true;
        Target = _selected.IsChecked == true ? CoordinateDrawTarget.Selected : _listed.IsChecked == true ? CoordinateDrawTarget.Listed : CoordinateDrawTarget.All;
        Erase = erase;
        DialogResult = true;
    }

    /// <summary>マークの見本 (28×28)。</summary>
    private static FrameworkElement Preview(PointMarkKind kind)
    {
        const double size = 28, c = size / 2, r = 7;
        var canvas = new Canvas { Width = size, Height = size, Margin = new Thickness(0, 2, 0, 2) };
        var stroke = Brushes.Black;
        void Circle(double radius) => canvas.Children.Add(Place(new Ellipse { Width = radius * 2, Height = radius * 2, Stroke = stroke, StrokeThickness = 1.2 }, c - radius, c - radius));
        void Dot(double radius) => canvas.Children.Add(Place(new Ellipse { Width = radius * 2, Height = radius * 2, Fill = stroke }, c - radius, c - radius));
        void L(double x1, double y1, double x2, double y2) =>
            canvas.Children.Add(new Line { X1 = c + x1, Y1 = c - y1, X2 = c + x2, Y2 = c - y2, Stroke = stroke, StrokeThickness = 1.2 });
        switch (kind)
        {
            case PointMarkKind.CrossCircle: Circle(r); L(-r * 1.5, 0, r * 1.5, 0); L(0, -r * 1.5, 0, r * 1.5); break;
            case PointMarkKind.DotCircle: Circle(r); Dot(r * 0.3); break;
            case PointMarkKind.Cross: L(-r, 0, r, 0); L(0, -r, 0, r); break;
            case PointMarkKind.XMark: var d = r * Math.Sqrt(0.5); L(-d, -d, d, d); L(-d, d, d, -d); break;
            case PointMarkKind.Circle: Circle(r); break;
            case PointMarkKind.DoubleCircle: Circle(r); Circle(r * 0.55); break;
            case PointMarkKind.TrianglePoint:
                var s3 = Math.Sqrt(3) / 2;
                canvas.Children.Add(new Polygon
                {
                    Points = [new Point(c, c - r), new Point(c - r * s3, c + r / 2), new Point(c + r * s3, c + r / 2)],
                    Stroke = stroke,
                    StrokeThickness = 1.2,
                });
                Dot(r * 0.2);
                break;
            case PointMarkKind.Dot: Dot(r * 0.4); break;
        }
        return canvas;
    }

    private static UIElement Place(UIElement e, double left, double top)
    {
        Canvas.SetLeft(e, left);
        Canvas.SetTop(e, top);
        return e;
    }
}
