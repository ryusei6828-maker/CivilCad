using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CivilCad.Core.Model;

namespace CivilCad.App.Views;

/// <summary>線種を選ぶ一覧 (見本の線と名前)。作図入力バーとプロパティで使う。</summary>
public static class LineTypeCombo
{
    /// <summary>current が null なら未選択 (複数の線種が混ざっているとき)。</summary>
    public static ComboBox Create(LineType? current, Action<LineType> set, Brush lineBrush, double width = 120)
    {
        var box = new ComboBox
        {
            Width = width,
            ItemsSource = LineTypes.All.Select(t => Item(t, lineBrush)).ToList(),
            SelectedIndex = current is LineType c ? LineTypes.All.ToList().IndexOf(c) : -1,
            ToolTip = "線種 (実線・破線・点線・一点鎖線・二点鎖線)。模様の長さは用紙上の mm で、縮尺によらず印刷したとき同じ見た目",
        };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0) set(LineTypes.All[box.SelectedIndex]);
        };
        return box;
    }

    private static FrameworkElement Item(LineType t, Brush brush)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        // 見本: 用紙 1mm = 2.5px くらいで描く
        var sample = new Line
        {
            X1 = 0, Y1 = 6, X2 = 34, Y2 = 6,
            Stroke = brush,
            StrokeThickness = 1.5,
            StrokeDashCap = PenLineCap.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = 36,
            Height = 12,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var pattern = LineTypes.PatternMm(t);
        if (pattern.Count > 0)
        {
            const double pxPerMm = 2.5;
            sample.StrokeDashArray = new DoubleCollection(pattern.Select(mm =>
                mm >= 0 ? Math.Max(0, mm * pxPerMm - 1.5) / 1.5 : (-mm * pxPerMm + 1.5) / 1.5));
        }
        panel.Children.Add(sample);
        panel.Children.Add(new TextBlock { Text = LineTypes.Name(t), VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }
}
