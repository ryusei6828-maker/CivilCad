using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CivilCad.App.Tools;
using CivilCad.Core.Model;

namespace CivilCad.App.Views;

/// <summary>
/// 作図入力バー。線・円などの作図中だけ作図領域の上に出て、長さ・角度・半径・文字高と、
/// 線幅・色 (これから描く図形の設定) を入力できる。数値の欄は空欄なら「マウスで決める」。
/// </summary>
public sealed class DrawInputBar : Border
{
    private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)));
    private static readonly Brush LiveBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x9C, 0xDC, 0xFE)));
    private static readonly double[] StandardWeights = [0.13, 0.18, 0.25, 0.35, 0.5, 0.7, 1.0, 1.4, 2.0];

    private readonly StackPanel _panel = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 3, 8, 3) };
    private readonly TextBlock _live = new() { Foreground = LiveBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), FontFamily = new FontFamily("Consolas, Yu Gothic UI") };
    private CadCanvas? _canvas;
    private CadDocument? _doc;

    public DrawInputBar()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26));
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46));
        BorderThickness = new Thickness(0, 0, 0, 1);
        Child = _panel;
        Visibility = Visibility.Collapsed;
    }

    /// <summary>値を確定した (コマンド欄にフォーカスを戻すのに使う)。</summary>
    public event EventHandler? Committed;

    public void Attach(CadCanvas canvas)
    {
        _canvas = canvas;
        canvas.ToolChanged += (_, _) => Rebuild();
        canvas.DocumentReplaced += (_, _) => WatchDocument();
        canvas.CursorMoved += (_, _) => UpdateLive();
        WatchDocument();
    }

    private void WatchDocument()
    {
        if (_doc != null) _doc.PropertyChanged -= Doc_PropertyChanged;
        _doc = _canvas!.Document;
        _doc.PropertyChanged += Doc_PropertyChanged;
        Rebuild();
    }

    private void Doc_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CadDocument.CurrentLineWeight) or nameof(CadDocument.CurrentColor) or nameof(CadDocument.ScaleDenominator))
            Rebuild();
    }

    private void Rebuild()
    {
        _panel.Children.Clear();
        var tool = _canvas?.Tool;
        if (tool is not IStyledDrawTool || _doc is not { } doc)
        {
            Visibility = Visibility.Collapsed;
            return;
        }
        Visibility = Visibility.Visible;

        _panel.Children.Add(new TextBlock
        {
            Text = $"{tool.Name}の入力",
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        });

        switch (tool)
        {
            case ILengthAngleInput la:
                AddNumber("長さ (m)", la.FixedLength, v => la.FixedLength = v, "次の点までの長さ。入れておくとクリックした方向にこの長さで線を引く");
                AddNumber("角度 (°)", la.FixedAngleDeg, v => la.FixedAngleDeg = v, "線の向き。東 = 0°、反時計回り (北 = 90°)");
                break;
            case ArcTool arcTool:
                AddNumber("半径 (m)", arcTool.FixedRadius, v => arcTool.FixedRadius = v,
                    "入れておくと 始点 → 終点 → 膨らむ側 の指定で、この半径の円弧を作図\n空欄なら 3 点 (始点・通過点・終点) で作図");
                break;
            case IRadiusInput ri:
                AddNumber("半径 (m)", ri.FixedRadius, v => ri.FixedRadius = v, "入れておくと、中心をクリックするだけでこの半径の円を作図");
                break;
            case TextTool:
                AddNumber($"文字高 (mm, {doc.ScaleText})", TextTool.DefaultHeightMm, v => { if (v is double h) TextTool.DefaultHeightMm = h; },
                    "用紙上の文字の高さ。図面の縮尺で m に換算", allowEmpty: false);
                break;
        }

        AddSeparator();
        AddWeight(doc);
        AddColor(doc);
        _panel.Children.Add(_live);
        UpdateLive();
    }

    /// <summary>今の長さ・角度・半径を表示する (固定していなくても目安になるように)。</summary>
    private void UpdateLive()
    {
        if (Visibility != Visibility.Visible || _canvas == null) return;
        _live.Text = _canvas.Tool switch
        {
            ILengthAngleInput { Live: var (len, ang) } => $"現在 長さ {len:F3} m  角度 {ang:F2}°",
            IRadiusInput { LiveRadius: double r } => $"現在 半径 {r:F3} m  直径 {r * 2:F3} m",
            _ => "空欄の項目はマウスで決めます",
        };
    }

    // ---- 部品 ----

    private void AddLabel(string text) =>
        _panel.Children.Add(new TextBlock { Text = text, Foreground = LabelBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });

    private void AddSeparator() =>
        _panel.Children.Add(new Border { Width = 1, Background = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x5A)), Margin = new Thickness(10, 2, 10, 2) });

    /// <summary>数値の欄。Enter かフォーカス移動で確定、空欄なら固定しない (null)、Esc で元に戻す。</summary>
    private void AddNumber(string label, double? value, Action<double?> set, string tip, bool allowEmpty = true)
    {
        AddLabel(label);
        var box = new TextBox
        {
            Width = 76,
            Margin = new Thickness(0, 0, 12, 0),
            Text = value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "",
            ToolTip = tip + (allowEmpty ? "\n空欄 = マウスで決める" : "") + "\nEnter で確定",
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var current = box.Text;
        void Commit()
        {
            var text = box.Text.Trim();
            if (text == current) return;
            if (text.Length == 0 && allowEmpty)
            {
                set(null);
            }
            else if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && (label.StartsWith("角度") || v > 0))
            {
                set(v);
                box.Text = v.ToString("0.###", CultureInfo.InvariantCulture);
            }
            else
            {
                box.Text = current; // 不正な値は戻す
                return;
            }
            current = box.Text;
            _canvas?.RedrawOverlay();
            Committed?.Invoke(this, EventArgs.Empty);
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { box.Text = current; e.Handled = true; Committed?.Invoke(this, EventArgs.Empty); }
        };
        box.LostKeyboardFocus += (_, _) => Commit();
        _panel.Children.Add(box);
    }

    private void AddWeight(CadDocument doc)
    {
        AddLabel("線幅 (mm)");
        var box = new ComboBox
        {
            Width = 76,
            IsEditable = true,
            Margin = new Thickness(0, 0, 12, 0),
            ItemsSource = new[] { "既定" }.Concat(StandardWeights.Select(w => w.ToString("0.00", CultureInfo.InvariantCulture))).ToList(),
            Text = doc.CurrentLineWeight is double w ? w.ToString("0.00", CultureInfo.InvariantCulture) : "既定",
            ToolTip = "これから描く図形の線の太さ (印刷時の mm)。一覧から選ぶか数値を入れて Enter",
        };
        var current = box.Text;
        void Commit()
        {
            var text = box.Text.Trim();
            if (text == current) return;
            if (text is "既定" or "") doc.CurrentLineWeight = null;
            else if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var mm) && mm > 0) doc.CurrentLineWeight = mm;
            else { box.Text = current; return; }
            Committed?.Invoke(this, EventArgs.Empty);
        }
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is string s) { box.Text = s; Commit(); }
        };
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        box.LostKeyboardFocus += (_, _) => Commit();
        _panel.Children.Add(box);
    }

    private sealed record ColorItem(string Label, RgbColor? Color);

    private void AddColor(CadDocument doc)
    {
        AddLabel("色");
        var items = new List<ColorItem> { new("レイヤ色", null) };
        items.AddRange(RgbColor.Palette.Select(c => new ColorItem(c.ToHex(), c)));
        if (doc.CurrentColor is RgbColor custom && !RgbColor.Palette.Contains(custom)) items.Add(new(custom.ToHex(), custom));

        var swatch = new FrameworkElementFactory(typeof(Border));
        swatch.SetValue(WidthProperty, 22.0);
        swatch.SetValue(HeightProperty, 11.0);
        swatch.SetValue(Border.BorderBrushProperty, Brushes.Gray);
        swatch.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        swatch.SetValue(MarginProperty, new Thickness(0, 0, 6, 0));
        swatch.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(ColorItem.Color)) { Converter = new RgbColorToBrushConverter() });
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ColorItem.Label)));
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.AppendChild(swatch);
        panel.AppendChild(label);

        var box = new ComboBox
        {
            Width = 120,
            ItemsSource = items,
            ItemTemplate = new DataTemplate { VisualTree = panel },
            SelectedItem = items.First(i => i.Color == doc.CurrentColor),
            ToolTip = "これから描く図形の色",
        };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ColorItem ci && ci.Color != doc.CurrentColor)
            {
                doc.CurrentColor = ci.Color;
                Committed?.Invoke(this, EventArgs.Empty);
            }
        };
        _panel.Children.Add(box);
    }

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }
}
