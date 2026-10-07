using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.IO;

/// <summary>座標読込の設定 (次回も同じ設定で開く)。</summary>
public sealed class PointImportOptions
{
    public string LayerName { get; set; } = "座標点";
    public bool ShowName { get; set; } = true;
    public bool ShowElevation { get; set; }
    public bool DrawLots { get; set; } = true;

    /// <summary>点名の文字高 (用紙 mm)。</summary>
    public double TextHeightMm { get; set; } = 2.5;

    /// <summary>点の印 (円) の直径 (用紙 mm)。</summary>
    public double MarkerMm { get; set; } = 1.5;

    public PointImportOptions Clone() => (PointImportOptions)MemberwiseClone();

    /// <summary>
    /// 点を図形にする: 点ごとに小さな円 (中心にスナップできる) と、右上に点名・右下に標高の文字。
    /// SIMA の画地は点番号をつないだ閉じたポリライン。
    /// </summary>
    public List<Entity> Build(CadDocument doc, PointFile file)
    {
        var layer = doc.GetOrCreateLayer(string.IsNullOrWhiteSpace(LayerName) ? "座標点" : LayerName.Trim(), RgbColor.Yellow);
        var r = doc.PaperMmToMeters(MarkerMm) / 2;
        var h = doc.PaperMmToMeters(TextHeightMm);
        var list = new List<Entity>();
        foreach (var p in file.Points)
        {
            var c = doc.FromSurvey(p.X, p.Y); // 座標設定の向きで図面上の点に
            list.Add(new CircleEntity(layer, c, r) { Tag = "POINT:" + p.Label });
            var x = c.X + r * 1.5;
            if (ShowName && p.Label.Length > 0)
                list.Add(new TextEntity(layer, new Vec2(x, c.Y + r * 0.5), p.Label, h));
            if (ShowElevation && p.Z is double z)
                list.Add(new TextEntity(layer, new Vec2(x, c.Y - r * 0.5), z.ToString("F3", CultureInfo.InvariantCulture), h * 0.8) { VAlign = TextVAlign.Top });
        }
        if (DrawLots)
        {
            var byNumber = new Dictionary<string, SurveyPoint>();
            foreach (var p in file.Points) byNumber.TryAdd(p.Number, p);
            foreach (var (_, numbers) in file.Lots)
            {
                var pts = numbers.Where(byNumber.ContainsKey).Select(n => doc.FromSurvey(byNumber[n].X, byNumber[n].Y)).ToList();
                if (pts.Count >= 2) list.Add(new PolylineEntity(layer, pts, pts.Count >= 3));
            }
        }
        return list;
    }
}

/// <summary>座標読込の確認画面: 読み取った点の一覧と、表示の設定。</summary>
public sealed class PointImportWindow : Window
{
    private readonly PointImportOptions _options;
    private readonly TextBox _layer = new() { Width = 160 };
    private readonly CheckBox _name = new() { Content = "点名を表示", Margin = new Thickness(0, 0, 16, 0) };
    private readonly CheckBox _elevation = new() { Content = "標高を表示", Margin = new Thickness(0, 0, 16, 0) };
    private readonly CheckBox _lots = new() { Content = "画地 (区画) を線で結ぶ" };
    private readonly TextBox _height = new() { Width = 50 };
    private readonly TextBox _marker = new() { Width = 50 };
    private readonly TextBlock _error = new() { Foreground = System.Windows.Media.Brushes.Firebrick, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };

    public PointImportWindow(string path, PointFile file, PointImportOptions options, string scaleText)
    {
        _options = options.Clone();
        Title = "座標読込";
        Width = 720;
        Height = 620;
        MinWidth = 600;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var withZ = file.Points.Count(p => p.Z != null);
        var summary = $"{Path.GetFileName(path)}:  点 {file.Points.Count} 個" +
                      (withZ > 0 ? $" (標高あり {withZ})" : "") +
                      (file.Lots.Count > 0 ? $"、画地 {file.Lots.Count} 区画" : "") +
                      (file.SkippedLines > 0 ? $"、読めなかった行 {file.SkippedLines}" : "");
        if (file.Points.Count > 0)
        {
            var xs = file.Points.Select(p => p.X).ToList();
            var ys = file.Points.Select(p => p.Y).ToList();
            summary += $"\n範囲  X (北) {xs.Min():F3} 〜 {xs.Max():F3}   Y (東) {ys.Min():F3} 〜 {ys.Max():F3}";
        }

        var grid = new DataGrid
        {
            IsReadOnly = true,
            AutoGenerateColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            ItemsSource = file.Points.Take(2000).Select(p => new
            {
                No = p.Number,
                点名 = p.Name,
                X = p.X.ToString("F3", CultureInfo.InvariantCulture),
                Y = p.Y.ToString("F3", CultureInfo.InvariantCulture),
                標高 = p.Z?.ToString("F3", CultureInfo.InvariantCulture) ?? "",
            }).ToList(),
            Margin = new Thickness(0, 6, 0, 8),
        };
        foreach (var (header, binding, width) in new[] { ("No", "No", 70.0), ("点名", "点名", 150.0), ("X (北)", "X", 120.0), ("Y (東)", "Y", 120.0), ("標高", "標高", 90.0) })
            grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new System.Windows.Data.Binding(binding), Width = width });

        _layer.Text = _options.LayerName;
        _name.IsChecked = _options.ShowName;
        _elevation.IsChecked = _options.ShowElevation;
        _elevation.IsEnabled = withZ > 0;
        _lots.IsChecked = _options.DrawLots;
        _lots.IsEnabled = file.Lots.Count > 0;
        _height.Text = _options.TextHeightMm.ToString(CultureInfo.InvariantCulture);
        _marker.Text = _options.MarkerMm.ToString(CultureInfo.InvariantCulture);

        var settings = new StackPanel();
        settings.Children.Add(Row(Label("レイヤ"), _layer));
        settings.Children.Add(Row(_name, _elevation, _lots));
        settings.Children.Add(Row(Label($"文字高 (mm, {scaleText})"), _height, Label("   点の印の大きさ (mm)"), _marker));
        settings.Children.Add(new TextBlock
        {
            Text = "座標は測量座標 (X = 北, Y = 東) として配置します。図面を測量座標に合わせていない場合は、先に 座標設定 (CS) を行ってください。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 6, 0, 0),
        });

        var ok = new Button { Content = "読み込む", Width = 100, IsDefault = true, Margin = new Thickness(0, 0, 8, 0), IsEnabled = file.Points.Count > 0 };
        var cancel = new Button { Content = "キャンセル", Width = 90, IsCancel = true };
        ok.Click += (_, _) => Accept();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(_error);
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new DockPanel { Margin = new Thickness(10) };
        var head = new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(settings, Dock.Bottom);
        root.Children.Add(head);
        root.Children.Add(buttons);
        root.Children.Add(settings);
        root.Children.Add(grid);
        Content = root;
    }

    /// <summary>「読み込む」で確定した設定。</summary>
    public PointImportOptions? Result { get; private set; }

    private void Accept()
    {
        if (!double.TryParse(_height.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var h) || h <= 0 ||
            !double.TryParse(_marker.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) || m <= 0)
        {
            _error.Text = "文字高・印の大きさは正の数値にしてください。";
            return;
        }
        _options.LayerName = _layer.Text.Trim();
        _options.ShowName = _name.IsChecked == true;
        _options.ShowElevation = _elevation.IsChecked == true;
        _options.DrawLots = _lots.IsChecked == true;
        _options.TextHeightMm = h;
        _options.MarkerMm = m;
        Result = _options;
        DialogResult = true;
    }

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };

    private static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        foreach (var c in children) row.Children.Add(c);
        return row;
    }
}
