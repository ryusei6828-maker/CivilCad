using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CivilCad.App.Drawing;
using CivilCad.App.IO;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Views;

/// <summary>PDF に印刷するときに分かっている範囲: 今の画面・選択した図形・前に指定した範囲。</summary>
public sealed record PdfTargets(Box2 Screen, IReadOnlyCollection<Entity> Selection, Box2? Window);

/// <summary>PDF に印刷する設定 (範囲・用紙・向き・縮尺・白黒・既定の線幅)。前回の設定はアプリを閉じるまで覚える。</summary>
public sealed class PdfWindow : Window
{
    private static readonly PdfOptions Last = new();
    private static bool s_openAfter = true;

    private readonly CadDocument _doc;
    private readonly PdfTargets _targets;
    private readonly ComboBox _range = new() { Width = 220 };
    private readonly Button _pick = new() { Content = "範囲を指定し直す", Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(8, 0, 0, 0) };
    private readonly CheckBox _fit = new() { Content = "用紙に合わせて拡大・縮小する (縮尺は自動)" };
    private readonly ComboBox _paper = new() { Width = 160 };
    private readonly RadioButton _landscape = new() { Content = "横", Margin = new Thickness(0, 0, 12, 0), GroupName = "orient" };
    private readonly RadioButton _portrait = new() { Content = "縦", GroupName = "orient" };
    private readonly TextBox _scale = new() { Width = 80 };
    private readonly CheckBox _mono = new() { Content = "白黒で印刷する (線はすべて黒、塗りは灰色)" };
    private readonly TextBox _weight = new() { Width = 60 };
    private readonly CheckBox _embed = new() { Content = "文字を検索・コピーできるようにする (フォントを埋め込む。数 MB 大きくなる)" };
    private readonly CheckBox _lossless = new() { Content = "画像を劣化なしで保存する (ファイルが大きくなる)" };
    private readonly CheckBox _open = new() { Content = "印刷したら PDF を開く" };
    private readonly TextBlock _summary = new() { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly Button _ok = new() { Content = "保存先を選んで印刷", IsDefault = true, Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(0, 0, 8, 0) };

    public PdfOptions Options { get; private set; } = new();
    public bool OpenAfter { get; private set; }

    /// <summary>true なら、図面上で印刷範囲を指定してから、もう一度この窓を出す。</summary>
    public bool PickWindow { get; private set; }

    public PdfWindow(CadDocument doc, PdfTargets? targets = null)
    {
        _doc = doc;
        _targets = targets ?? new PdfTargets(Box2.Empty, [], null);
        Title = "PDF に印刷";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _paper.Items.Add("自動 (図枠・図形に合わせる)");
        foreach (var p in PaperSize.All) _paper.Items.Add(p.Name);
        _paper.SelectedIndex = Last.Paper is { } name ? Math.Max(0, PaperSize.All.ToList().FindIndex(p => p.Name == name) + 1) : 0;
        (Last.Portrait ? _portrait : _landscape).IsChecked = true;
        _scale.Text = (Last.ScaleDenominator ?? doc.ScaleDenominator).ToString("0.##", CultureInfo.InvariantCulture);
        _mono.IsChecked = Last.Monochrome;
        _weight.Text = Last.DefaultLineWeightMm.ToString("0.##", CultureInfo.InvariantCulture);
        _embed.IsChecked = Last.EmbedFonts;
        _lossless.IsChecked = Last.LosslessImages;
        _open.IsChecked = s_openAfter;
        _fit.IsChecked = Last.FitToPaper;

        _range.Items.Add(new ComboBoxItem { Content = "図面全体 (図枠があれば図枠)" });
        _range.Items.Add(new ComboBoxItem { Content = "今の画面", IsEnabled = !_targets.Screen.IsEmpty });
        _range.Items.Add(new ComboBoxItem { Content = $"選択した図形だけ ({_targets.Selection.Count} 個)", IsEnabled = _targets.Selection.Count > 0 });
        _range.Items.Add(new ComboBoxItem { Content = "範囲を指定 (2 点)" });
        // 選択していれば選択した図形、範囲を指定してきたならその範囲を最初に選ぶ
        var range = _targets.Window != null ? PdfRange.Window
            : _targets.Selection.Count > 0 ? PdfRange.Selection
            : Last.Range == PdfRange.Selection || (Last.Range == PdfRange.Screen && _targets.Screen.IsEmpty) ? PdfRange.Drawing
            : Last.Range;
        _range.SelectedIndex = (int)range;

        var grid = new Grid { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var row = 0;
        void Row(string label, UIElement value, string? tip = null)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 12, 4), ToolTip = tip };
            Grid.SetRow(l, row);
            grid.Children.Add(l);
            if (value is FrameworkElement fe)
            {
                fe.Margin = new Thickness(0, 4, 0, 4);
                fe.ToolTip ??= tip;
                fe.HorizontalAlignment = HorizontalAlignment.Left;
            }
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);
            row++;
        }
        var orient = new StackPanel { Orientation = Orientation.Horizontal };
        orient.Children.Add(_landscape);
        orient.Children.Add(_portrait);
        var scale = new StackPanel { Orientation = Orientation.Horizontal };
        scale.Children.Add(new TextBlock { Text = "1 : ", VerticalAlignment = VerticalAlignment.Center });
        scale.Children.Add(_scale);
        var weight = new StackPanel { Orientation = Orientation.Horizontal };
        weight.Children.Add(_weight);
        weight.Children.Add(new TextBlock { Text = " mm", VerticalAlignment = VerticalAlignment.Center });
        var rangePanel = new StackPanel { Orientation = Orientation.Horizontal };
        rangePanel.Children.Add(_range);
        rangePanel.Children.Add(_pick);
        Row("印刷範囲", rangePanel, "範囲の外は印刷しない。選択した図形だけのときは、ほかの図形は印刷しない");
        Row("用紙", _paper, "自動なら、図枠があればそれに合う A 判、無ければ図形全体が余白付きで収まる A 判 (用紙に合わせるときは A3)");
        Row("向き", orient);
        Row("", _fit, $"範囲が用紙 (余白 {PdfExport.FitMargin:0}mm) にちょうど収まるように拡大・縮小する");
        Row("縮尺", scale, $"印刷の縮尺。図面の縮尺は {doc.ScaleText}");
        Row("既定の線幅", weight, "線幅を指定していない図形の線の太さ");
        Row("", _mono);
        Row("", _embed, "オフなら文字は形 (アウトライン) で印刷する (見た目は同じで、ファイルが小さい)");
        Row("", _lossless, $"オフなら画像は高画質の JPEG (画質 {PdfOptions.JpegQuality}) で保存する (見た目はほぼ同じで、ファイルが小さい)");
        Row("", _open);

        var cancel = new Button { Content = "キャンセル", IsCancel = true, Padding = new Thickness(12, 2, 12, 2) };
        _ok.Click += (_, _) =>
        {
            if (Read() is not { } o) return;
            Options = o;
            OpenAfter = _open.IsChecked == true;
            Remember(o, OpenAfter);
            // 範囲を指定するのに、まだ指定していなければ、図面上で指定してもらう
            PickWindow = o.Range == PdfRange.Window && _targets.Window == null;
            DialogResult = true;
        };
        _pick.Click += (_, _) =>
        {
            if (Read() is not { } o) return;
            Options = o;
            OpenAfter = _open.IsChecked == true;
            Remember(o, OpenAfter);
            PickWindow = true;
            DialogResult = true;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(_ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel();
        panel.Children.Add(grid);
        panel.Children.Add(new Border { Margin = new Thickness(12, 0, 12, 0), Child = _summary });
        panel.Children.Add(new Border { Margin = new Thickness(12, 0, 12, 12), Child = buttons });
        Content = panel;

        _range.SelectionChanged += (_, _) => UpdateSummary();
        _fit.Checked += (_, _) => UpdateSummary();
        _fit.Unchecked += (_, _) => UpdateSummary();
        _paper.SelectionChanged += (_, _) => UpdateSummary();
        _landscape.Checked += (_, _) => UpdateSummary();
        _portrait.Checked += (_, _) => UpdateSummary();
        _scale.TextChanged += (_, _) => UpdateSummary();
        _weight.TextChanged += (_, _) => UpdateSummary();
        UpdateSummary();
    }

    private static void Remember(PdfOptions o, bool openAfter)
    {
        Last.Paper = o.Paper;
        Last.Portrait = o.Portrait;
        Last.ScaleDenominator = o.ScaleDenominator;
        Last.Monochrome = o.Monochrome;
        Last.DefaultLineWeightMm = o.DefaultLineWeightMm;
        Last.EmbedFonts = o.EmbedFonts;
        Last.LosslessImages = o.LosslessImages;
        Last.Range = o.Range;
        Last.FitToPaper = o.FitToPaper;
        s_openAfter = openAfter;
    }

    /// <summary>入力を読む。数字が正しくなければ null (用紙に合わせるときは縮尺は見ない)。</summary>
    private PdfOptions? Read()
    {
        var fit = _fit.IsChecked == true;
        double scale = _doc.ScaleDenominator;
        if (!fit && (!double.TryParse(_scale.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out scale) || !(scale > 0))) return null;
        if (!double.TryParse(_weight.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var weight) || !(weight > 0)) return null;
        var range = (PdfRange)Math.Max(0, _range.SelectedIndex);
        return new PdfOptions
        {
            Paper = _paper.SelectedIndex > 0 ? PaperSize.All[_paper.SelectedIndex - 1].Name : null,
            Portrait = _portrait.IsChecked == true,
            ScaleDenominator = Math.Abs(scale - _doc.ScaleDenominator) < 1e-9 ? null : scale,
            Monochrome = _mono.IsChecked == true,
            DefaultLineWeightMm = weight,
            EmbedFonts = _embed.IsChecked == true,
            LosslessImages = _lossless.IsChecked == true,
            Range = range,
            FitToPaper = fit,
            Region = range switch
            {
                PdfRange.Screen => _targets.Screen,
                PdfRange.Window => _targets.Window,
                _ => null,
            },
            Only = range == PdfRange.Selection ? _targets.Selection : null,
        };
    }

    private void UpdateSummary()
    {
        _scale.IsEnabled = _fit.IsChecked != true;
        _pick.Visibility = _range.SelectedIndex == (int)PdfRange.Window && _targets.Window != null ? Visibility.Visible : Visibility.Collapsed;
        if (Read() is not { } o)
        {
            _summary.Text = "縮尺と線幅には 0 より大きい数を入れてください。";
            _ok.IsEnabled = false;
            return;
        }
        _ok.IsEnabled = true;
        if (o.Range == PdfRange.Window && _targets.Window == null)
        {
            _ok.Content = "範囲を指定する";
            _summary.Text = "「範囲を指定する」を押してから、図面上で印刷する範囲の対角の 2 点をクリックしてください。指定したら、この窓がもう一度出ます。";
            return;
        }
        _ok.Content = "保存先を選んで印刷";
        var l = PdfExport.ComputeLayout(_doc, o);
        var orient = l.Width >= l.Height ? "横" : "縦";
        _summary.Text = $"用紙 {l.PaperName} {orient} ({l.Width:0}×{l.Height:0} mm)、縮尺 1:{l.MetersPerMm * 1000:0.##} で印刷します。" +
            (o.FitToPaper ? " (用紙に合わせた縮尺)" : "");
    }

    /// <summary>窓を出して設定を返す (やめたら null)。PickWindow なら、範囲を指定してからもう一度呼ぶ。</summary>
    public static (PdfOptions Options, bool OpenAfter, bool PickWindow)? Ask(Window owner, CadDocument doc, PdfTargets? targets = null)
    {
        var w = new PdfWindow(doc, targets) { Owner = owner };
        return w.ShowDialog() == true ? (w.Options, w.OpenAfter, w.PickWindow) : null;
    }
}
