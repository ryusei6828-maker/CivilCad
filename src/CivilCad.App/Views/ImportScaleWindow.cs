using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CivilCad.App.Drawing;
using CivilCad.App.IO;
using CivilCad.Core.Geometry;

namespace CivilCad.App.Views;

/// <summary>
/// 図面を開くときに、その図面の縮尺 (と DXF / DWG の座標の単位) を選ぶ。読み込んだときの図形の大きさ (実寸と用紙上) を見ながら選べる。
/// </summary>
public sealed class ImportScaleWindow : Window
{
    private static readonly double[] CommonScales = [1, 10, 20, 50, 100, 200, 250, 300, 500, 1000, 2500, 5000];

    private readonly DrawingFiles.OpenResult _result;
    private readonly Box2? _extents;
    private readonly ComboBox _scale = new() { Width = 120, IsEditable = true };
    private readonly ComboBox _vertical = new() { Width = 120, IsEditable = true, ToolTip = "縦断図など、縦と横で縮尺が違うときの縦の縮尺。空なら横と同じ" };
    private readonly RadioButton _meter = new() { Content = "実寸 m (座標がそのまま m。CivilCad の DXF など)", GroupName = "unit" };
    private readonly RadioButton _milli = new() { Content = "実寸 mm (AutoCAD などで mm で描いた図面)", GroupName = "unit" };
    private readonly RadioButton _centi = new() { Content = "実寸 cm", GroupName = "unit" };
    private readonly RadioButton _paper = new() { Content = "用紙上の mm (図寸で描いた図面。縮尺を掛けて実寸にする)", GroupName = "unit" };
    private readonly TextBlock _info = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button _ok = new() { Content = "この縮尺で読み込む", IsDefault = true, Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(0, 0, 8, 0) };

    public double Scale { get; private set; }

    /// <summary>縦の縮尺 (null なら横と同じ)。</summary>
    public double? VerticalScale { get; private set; }
    public ImportUnit Unit { get; private set; }

    /// <param name="extents">読み込んだ図形の範囲 (ファイルの値のまま換算した m)。</param>
    /// <param name="sheetScale">今のシートの縮尺 (ファイルに縮尺が無いときの初期値)。</param>
    public ImportScaleWindow(string path, DrawingFiles.OpenResult result, Box2? extents, double sheetScale)
    {
        _result = result;
        _extents = extents;
        Title = "図面の縮尺を選んで読み込む";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = Path.GetFileName(path), FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis });
        panel.Children.Add(new TextBlock
        {
            Text = result.FileScale is double fs
                ? $"ファイルの縮尺: 1:{fs:0.##}" + (result.FileVerticalScale is double fv ? $"  (縦 1:{fv:0.##})" : "")
                : "ファイルに縮尺の情報はありません",
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 2, 0, 8),
        });

        var scaleRow = new StackPanel { Orientation = Orientation.Horizontal };
        scaleRow.Children.Add(new TextBlock { Text = "図面の縮尺   1 : ", VerticalAlignment = VerticalAlignment.Center, FontSize = 14 });
        _scale.ItemsSource = CommonScales;
        _scale.Text = (result.FileScale ?? sheetScale).ToString("0.##", CultureInfo.InvariantCulture);
        scaleRow.Children.Add(_scale);
        panel.Children.Add(scaleRow);
        var verticalRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        verticalRow.Children.Add(new TextBlock { Text = "縦の縮尺       1 : ", VerticalAlignment = VerticalAlignment.Center, FontSize = 14 });
        _vertical.ItemsSource = CommonScales;
        _vertical.Text = result.FileVerticalScale is double fvs ? fvs.ToString("0.##", CultureInfo.InvariantCulture) : "";
        verticalRow.Children.Add(_vertical);
        verticalRow.Children.Add(new TextBlock { Text = "  (縦断図など。空なら横と同じ)", VerticalAlignment = VerticalAlignment.Center, Foreground = System.Windows.Media.Brushes.Gray });
        panel.Children.Add(verticalRow);

        if (!result.PaperFormat)
        {
            panel.Children.Add(new TextBlock { Text = "座標の単位:", Margin = new Thickness(0, 10, 0, 2) });
            foreach (var r in new[] { _meter, _milli, _centi, _paper })
            {
                r.Margin = new Thickness(0, 2, 0, 2);
                panel.Children.Add(r);
            }
            (result.FileUnit switch
            {
                ImportUnit.Millimeter => _milli,
                ImportUnit.Centimeter => _centi,
                _ => _meter,
            }).IsChecked = true;
            if (result.FileUnit is { } u)
                panel.Children.Add(new TextBlock { Text = $"(ファイルの単位の指定: {UnitName(u)})", Foreground = System.Windows.Media.Brushes.Gray });
        }
        panel.Children.Add(new TextBlock
        {
            Text = result.PaperFormat
                ? "ファイルの縮尺と違う縮尺にすると、用紙上の大きさのまま図面全体の実寸が変わります (図寸固定)。"
                : "縮尺の書いてある図面でファイルと違う縮尺にすると、用紙上の大きさのまま図面全体の実寸が変わります (図寸固定)。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 8, 0, 0),
        });
        panel.Children.Add(_info);

        var cancel = new Button { Content = "キャンセル", IsCancel = true, Padding = new Thickness(12, 2, 12, 2) };
        _ok.Click += (_, _) =>
        {
            if (!TryRead(out var s, out var unit)) return;
            double? v = null;
            if (_vertical.Text.Trim().Length > 0)
            {
                if (!double.TryParse(_vertical.Text.Replace("1:", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var vs) || !(vs > 0))
                {
                    _info.Text = "縦の縮尺は正の数にしてください (横と同じなら空)。";
                    return;
                }
                if (Math.Abs(vs - s) > 1e-9) v = vs;
            }
            Scale = s;
            VerticalScale = v;
            Unit = unit;
            DialogResult = true;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(_ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;

        _scale.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new RoutedEventHandler((_, _) => Update()));
        _scale.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(Update);
        foreach (var r in new[] { _meter, _milli, _centi, _paper }) r.Checked += (_, _) => Update();
        Loaded += (_, _) => _scale.Focus();
        Update();
    }

    private static string UnitName(ImportUnit u) => u switch
    {
        ImportUnit.Millimeter => "mm",
        ImportUnit.Centimeter => "cm",
        ImportUnit.PaperMillimeter => "用紙上の mm",
        _ => "m",
    };

    private bool TryRead(out double scale, out ImportUnit unit)
    {
        unit = _milli.IsChecked == true ? ImportUnit.Millimeter
            : _centi.IsChecked == true ? ImportUnit.Centimeter
            : _paper.IsChecked == true ? ImportUnit.PaperMillimeter
            : ImportUnit.Meter;
        return double.TryParse(_scale.Text.Replace("1:", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out scale) && scale > 0;
    }

    private void Update()
    {
        if (!TryRead(out var s, out var unit))
        {
            _info.Text = "縮尺には正の数を入れてください (例: 250)。";
            _ok.IsEnabled = false;
            return;
        }
        _ok.IsEnabled = true;
        var k = DrawingFiles.ImportFactor(_result, s, unit);
        if (_extents is not Box2 ext)
        {
            _info.Text = "図形がありません。";
            return;
        }
        double w = ext.Width * k, h = ext.Height * k;
        double pw = w / (s / 1000), ph = h / (s / 1000);
        var fit = PaperSize.All.Reverse().FirstOrDefault(p => Math.Max(pw, ph) <= p.Width && Math.Min(pw, ph) <= p.Height);
        _info.Text = $"読み込む大きさ: 実寸 {w:0.###} m × {h:0.###} m  (用紙上 {pw:0} × {ph:0} mm" +
                     (fit != null ? $"、{fit.Name} に収まる)" : "、A0 より大きい)") +
                     (Math.Abs(k - 1) > 1e-12 ? $"\nファイルの値を {k:0.######} 倍して読み込みます。" : "");
    }

    /// <summary>窓を出して縮尺と単位を返す (やめたら null)。</summary>
    public static (double Scale, ImportUnit Unit, double? Vertical)? Ask(Window owner, string path, DrawingFiles.OpenResult result, Box2? extents, double sheetScale)
    {
        var w = new ImportScaleWindow(path, result, extents, sheetScale) { Owner = owner };
        return w.ShowDialog() == true ? (w.Scale, w.Unit, w.VerticalScale) : null;
    }
}
