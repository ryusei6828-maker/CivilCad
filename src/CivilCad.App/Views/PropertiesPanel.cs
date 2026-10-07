using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CivilCad.Alignment;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Views;

/// <summary>
/// 選択中の図形のプロパティ (長さ・線幅・色など) を表示・編集するパネル。
/// 編集は図形を複製して置き換えるコマンドで行うので、Ctrl+Z で戻せる。
/// </summary>
public sealed class PropertiesPanel : UserControl
{
    private static readonly double[] StandardWeights = [0.13, 0.18, 0.25, 0.35, 0.5, 0.7, 1.0, 1.4, 2.0];
    private static readonly Brush HeaderBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xF6)));
    private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)));

    private readonly StackPanel _body = new() { Margin = new Thickness(6, 4, 6, 6) };
    private readonly Button _floatButton;
    private readonly Button _closeButton;
    private CadCanvas? _canvas;
    private bool _refreshQueued;
    private bool _isFloating;

    public PropertiesPanel()
    {
        _closeButton = HeaderButton("×", "閉じる");
        _closeButton.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        _floatButton = HeaderButton("⧉", "フロート (別ウィンドウに切り離す)");
        _floatButton.Click += (_, _) => FloatToggleRequested?.Invoke(this, EventArgs.Empty);

        var header = new DockPanel { Background = HeaderBrush, ToolTip = "ダブルクリックでフロート / ドッキング切り替え" };
        DockPanel.SetDock(_closeButton, Dock.Right);
        DockPanel.SetDock(_floatButton, Dock.Right);
        header.Children.Add(_closeButton);
        header.Children.Add(_floatButton);
        header.Children.Add(new TextBlock { Text = "プロパティ", FontWeight = FontWeights.Bold, Margin = new Thickness(6, 3, 0, 3) });
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2 && e.OriginalSource is not Button) FloatToggleRequested?.Invoke(this, EventArgs.Empty);
        };

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body });
        Content = root;
        Background = Brushes.White; // 親の背景に左右されないように (フロート時も同じ見た目)
        IsVisibleChanged += (_, _) => QueueRefresh();
    }

    /// <summary>別ウィンドウに浮いているか。ヘッダのボタン表示が変わる (フロート中は × を隠す)。</summary>
    public bool IsFloating
    {
        get => _isFloating;
        set
        {
            _isFloating = value;
            _floatButton.Content = value ? "⇲" : "⧉";
            _floatButton.ToolTip = value ? "ドッキング (メイン画面の右側に戻す)" : "フロート (別ウィンドウに切り離す)";
            _closeButton.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private static Button HeaderButton(string text, string tip) => new()
    {
        Content = text,
        ToolTip = tip,
        Padding = new Thickness(6, 0, 6, 0),
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        Focusable = false,
    };

    /// <summary>× ボタンが押された。</summary>
    public event EventHandler? CloseRequested;

    /// <summary>フロート / ドッキングの切り替えが求められた。</summary>
    public event EventHandler? FloatToggleRequested;

    /// <summary>値を確定した (コマンド欄へフォーカスを戻すのに使う)。</summary>
    public event EventHandler? Committed;

    private CadDocument Doc => _canvas!.Document;

    public void Attach(CadCanvas canvas)
    {
        _canvas = canvas;
        canvas.SelectionChanged += (_, _) => QueueRefresh();
        WatchDocument(canvas.Document);
        // シートタブで図面が替わったら、新しい図面の変更を見張る
        canvas.DocumentReplaced += (_, _) => WatchDocument(canvas.Document);
        QueueRefresh();
    }

    private CadDocument? _watched;

    private void WatchDocument(CadDocument doc)
    {
        if (_watched != null)
        {
            _watched.Changed -= OnWatchedChanged;
            _watched.PropertyChanged -= OnWatchedPropertyChanged;
        }
        _watched = doc;
        doc.Changed += OnWatchedChanged;
        doc.PropertyChanged += OnWatchedPropertyChanged; // 作図入力バーで線幅・色を変えたときも「作図の設定」を合わせる
        QueueRefresh();
    }

    private void OnWatchedChanged(object? sender, EventArgs e) => QueueRefresh();

    private void OnWatchedPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CadDocument.CurrentLineWeight) or nameof(CadDocument.CurrentColor)) QueueRefresh();
    }

    /// <summary>変更が続けて起きても 1 回だけ作り直す。</summary>
    private void QueueRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _refreshQueued = false;
            Refresh();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    public void Refresh()
    {
        _body.Children.Clear();
        if (_canvas == null || !IsVisible) return;

        var sel = Doc.Entities.Where(_canvas.Selection.Contains).ToList();
        if (sel.Count == 0)
        {
            AddDrawingSettings();
            return;
        }

        AddSection(sel.Count == 1 ? KindName(sel[0]) : $"{sel.Count} 個を選択");
        if (sel.Count > 1)
            AddReadOnly("内訳", string.Join("、", sel.GroupBy(KindName).Select(g => $"{g.Key} {g.Count()}")));

        AddSection("共通");
        AddLayerRow(sel);
        AddColorRow("色", sel.Select(e => e.Color).ToList(), c => Apply(x => x.Color = c, "色変更"));
        AddWeightRow("線幅 (mm)", sel.Select(e => e.LineWeight).ToList(), w => Apply(x => x.LineWeight = w, "線幅変更"));
        AddTextSizeRows(sel.OfType<TextEntity>().ToList());
        AddTransparencyRow([.. sel.Where(e => e is HatchEntity or ImageEntity)]);
        var lengths = sel.Select(LengthOf).Where(l => l != null).Select(l => l!.Value).ToList();
        if (sel.Count > 1 && lengths.Count > 0) AddReadOnly("長さ合計", $"{lengths.Sum():F3} m  ({lengths.Count} 個)");

        if (sel.Count == 1) AddDetails(sel[0]);
    }

    // ---- 図形ごとの項目 ----

    private void AddDetails(Entity e)
    {
        AddSection("形状");
        switch (e)
        {
            case LineEntity l:
                AddReadOnly("始点", Xy(l.Start));
                AddReadOnly("終点", Xy(l.End));
                AddEditable("長さ (m)", l.Length.ToString("F3"), s =>
                    TryPositive(s, out var len) && l.Length > 0 &&
                    Apply(x => ((LineEntity)x).End = l.Start + (l.End - l.Start).Normalized() * len, "長さ変更"));
                var d = l.End - l.Start;
                AddReadOnly("方向角", Angles.ToDms(Angles.ToSurveyAzimuth(d.Angle)));
                AddReadOnly("Δx / Δy", $"{d.X:F3} / {d.Y:F3}");
                break;

            case PolylineEntity p:
                AddReadOnly("頂点数", p.Points.Count.ToString());
                AddReadOnly("長さ", $"{p.Length:F3} m");
                AddReadOnly("閉じている", p.IsClosed ? "はい" : "いいえ");
                if (p.IsClosed || p.Filled) AddReadOnly("面積", $"{p.Area:F3} m²");
                if (p.Filled) AddReadOnly("塗りつぶし", "あり");
                break;

            case PointEntity pt:
                AddEditable("x (m)", pt.Position.X.ToString("F3"), s =>
                    TryNumber(s, out var x) && Apply(e2 => ((PointEntity)e2).Position = new Vec2(x, ((PointEntity)e2).Position.Y), "点の移動"));
                AddEditable("y (m)", pt.Position.Y.ToString("F3"), s =>
                    TryNumber(s, out var y) && Apply(e2 => ((PointEntity)e2).Position = new Vec2(((PointEntity)e2).Position.X, y), "点の移動"));
                var (sx, sy) = pt.Position.ToSurvey();
                AddReadOnly("測量座標", $"X {sx:F3}, Y {sy:F3}");
                break;

            case CircleEntity c:
                AddReadOnly("中心", Xy(c.Center));
                AddEditable("半径 (m)", c.Radius.ToString("F3"), s =>
                    TryPositive(s, out var r) && Apply(x => ((CircleEntity)x).Radius = r, "半径変更"));
                AddReadOnly("直径", $"{c.Radius * 2:F3} m");
                AddReadOnly("円周", $"{2 * Math.PI * c.Radius:F3} m");
                AddReadOnly("面積", $"{Math.PI * c.Radius * c.Radius:F3} m²");
                break;

            case ArcEntity a:
                AddReadOnly("中心", Xy(a.Center));
                AddEditable("半径 (m)", a.Radius.ToString("F3"), s =>
                    TryPositive(s, out var r) && Apply(x => ((ArcEntity)x).Radius = r, "半径変更"));
                AddReadOnly("開始角 / 終了角", $"{Deg(a.StartAngle):F4}° / {Deg(a.EndAngle):F4}°");
                AddReadOnly("中心角", Angles.ToDms(a.Sweep));
                AddReadOnly("弧長", $"{a.Length:F3} m");
                AddReadOnly("弦長", $"{a.StartPoint.DistanceTo(a.EndPoint):F3} m");
                break;

            case SymbolEntity sym:
                AddReadOnly("位置", Xy(sym.Position));
                AddEditable($"大きさ (mm, {Doc.ScaleText})", sym.SizeMm.ToString("0.###", CultureInfo.InvariantCulture), s =>
                    TryPositive(s, out var mm) && Apply(x =>
                    {
                        var sx = (SymbolEntity)x;
                        sx.SizeMm = mm;
                        sx.Size = Doc.PaperMmToMeters(mm);
                    }, "プリセットの大きさ変更"), "用紙上の大きさ。縮尺を変えても用紙上の大きさは変わらない");
                if (sym.Definition.PointsNorth && sym.AutoNorth)
                {
                    // 自動: 向きは常に北。角度を入れると手動になる
                    AddReadOnly("向き", $"自動: {Tools.PresetTool.NorthText(Doc)}");
                    if (Math.Abs(sym.Rotation) > 1e-12)
                        AddReadOnly("座標北との差", $"{Angles.ToDms(Math.Abs(sym.Rotation))} {(sym.Rotation > 0 ? "西" : "東")}へ");
                    AddEditable("手動の向き (°)", "", s =>
                        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var deg) &&
                        Apply(x => { var sx = (SymbolEntity)x; sx.AutoNorth = false; sx.Rotation = deg * Math.PI / 180; }, "方位記号の向き (手動)"),
                        "角度を入れると手動になる (図面の上 = 0、反時計回り)。北に戻すには 自動セット (NA)");
                }
                else if (sym.Definition.PointsNorth)
                {
                    AddEditable("向き (°, 手動)", Deg(sym.Rotation).ToString("0.###", CultureInfo.InvariantCulture), s =>
                        s.Trim() == "自動"
                            ? Apply(x => ((SymbolEntity)x).AutoNorth = true, "方位記号を北に合わせる")
                            : double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var deg) &&
                              Apply(x => ((SymbolEntity)x).Rotation = deg * Math.PI / 180, "方位記号の向き (手動)"),
                        "手動の向き。図面の上 = 0、反時計回り。北に戻すには「自動」と入力するか 自動セット (NA)");
                }
                else
                {
                    AddEditable("向き (°)", Deg(sym.Rotation).ToString("0.###", CultureInfo.InvariantCulture), s =>
                        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var deg) &&
                        Apply(x => ((SymbolEntity)x).Rotation = deg * Math.PI / 180, "プリセットの向き変更"),
                        "記号の上の向き。0 = 図面の上、反時計回りが正");
                }
                AddReadOnly("図面上の大きさ", $"{sym.Size:F3} m");
                break;

            case ImageEntity img:
                AddReadOnly("ファイル", img.Path);
                if (IO.PdfRenderer.IsPdf(img.Path))
                    AddReadOnly("ページ", $"{img.Page + 1} (用紙 {img.PixelWidth / IO.PdfRenderer.PixelsPerMm:0}×{img.PixelHeight / IO.PdfRenderer.PixelsPerMm:0}mm)");
                else
                    AddReadOnly("画素数", $"{img.PixelWidth} × {img.PixelHeight}");
                AddEditable("幅 (m)", img.Width.ToString("0.###", CultureInfo.InvariantCulture), s =>
                    TryPositive(s, out var w) && Apply(x =>
                    {
                        // 左下を基準に、縦横比を保って大きさを変える
                        var ix = (ImageEntity)x;
                        var k = w / ix.Width;
                        ix.U *= k;
                        ix.V *= k;
                    }, "画像の大きさ変更"), "左下を基準に、縦横比を保って大きさを変える");
                AddReadOnly("高さ", $"{img.Height:F3} m");
                AddReadOnly("1 画素の大きさ", $"{img.Width / img.PixelWidth:0.####} m");
                AddReadOnly("左下", Xy(img.Origin));
                AddReadOnly("回転角", $"{Deg(img.U.Angle):F2}°");
                break;

            case HatchEntity h:
                AddReadOnly("種類", (h.Solid ? "塗りつぶし" : h.Patterns.Count >= 2 ? $"線 {h.Patterns.Count} 方向" : "斜線")
                                   + (string.IsNullOrEmpty(h.PatternName) ? "" : $" ({h.PatternName})"));
                AddReadOnly("境界", h.Loops.Count > 1 ? $"{h.Loops.Count} 個 (中抜きあり)" : "1 個");
                AddReadOnly("面積", $"{h.Area:F3} m²");
                if (h.Solid || h.Patterns.Count == 0) break;
                var p0 = h.Patterns[0];
                AddEditable("角度 (°)", Deg(p0.Angle).ToString("0.###", CultureInfo.InvariantCulture), s =>
                    double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var deg) &&
                    Apply(x =>
                    {
                        // 全パターンを同じだけ回す (クロスの直交関係を保つ)
                        var hx = (HatchEntity)x;
                        var delta = deg * Math.PI / 180 - p0.Angle;
                        hx.SetPatterns(hx.Patterns.Select(p => p with { Angle = p.Angle + delta }));
                    }, "ハッチ角度変更"));
                var mmPerSpacing = Doc.PaperMmToMeters(1);
                AddEditable($"間隔 (mm, {Doc.ScaleText})", (p0.Spacing / mmPerSpacing).ToString("0.###", CultureInfo.InvariantCulture), s =>
                    TryPositive(s, out var mm) &&
                    Apply(x =>
                    {
                        var hx = (HatchEntity)x;
                        var ratio = mm * mmPerSpacing / p0.Spacing;
                        hx.SetPatterns(hx.Patterns.Select(p => p with { Spacing = p.Spacing * ratio }));
                    }, "ハッチ間隔変更"));
                AddReadOnly("間隔 (図面上)", $"{p0.Spacing:F3} m");
                AddReadOnly("線の本数", h.Lines.Count.ToString());
                break;

            case TextEntity t:
                AddEditable("文字", t.Text, s =>
                    s.Length > 0 && Apply(x => ((TextEntity)x).Text = s, "文字変更"));
                AddReadOnly("文字高 (図面上)", $"{t.Height:F3} m");
                AddReadOnly("位置", Xy(t.Position));
                AddReadOnly("回転角", $"{Deg(t.Rotation):F2}°");
                AddReadOnly("配置", $"{HName(t.HAlign)}・{VName(t.VAlign)}");
                if (t.FitWidth is double fit) AddReadOnly("幅指定", $"{fit:F3} m");
                break;
        }
    }

    // ---- 共通項目 ----

    private void AddLayerRow(List<Entity> sel)
    {
        var box = new ComboBox { ItemsSource = Doc.Layers.ToList(), DisplayMemberPath = nameof(Layer.Name) };
        var layers = sel.Select(e => e.Layer).Distinct().ToList();
        // 複数のレイヤが混ざっているときは未選択 (空欄) にしておく
        var initial = layers.Count == 1 ? layers[0] : null;
        box.SelectedItem = initial;
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is Layer l && l != initial) Apply(x => x.Layer = l, "レイヤ変更");
        };
        AddRow("レイヤ", box);
    }

    private sealed record ColorItem(string Label, RgbColor? Color);

    /// <summary>色の行。values は対象ごとの現在値 (null = レイヤ色)。混在していれば空欄にする。</summary>
    private void AddColorRow(string label, IReadOnlyList<RgbColor?> values, Action<RgbColor?> set)
    {
        var items = new List<ColorItem> { new("レイヤ色", null) };
        items.AddRange(RgbColor.Palette.Select(c => new ColorItem(c.ToHex(), c)));
        var colors = values.Distinct().ToList();
        if (colors is [RgbColor custom] && !RgbColor.Palette.Contains(custom)) items.Add(new ColorItem(custom.ToHex(), custom));

        var box = new ComboBox { ItemsSource = items, ItemTemplate = ColorTemplate() };
        box.SelectedItem = colors.Count == 1 ? items.First(i => i.Color == colors[0]) : null;
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ColorItem ci && !(colors.Count == 1 && colors[0] == ci.Color)) set(ci.Color);
        };
        AddRow(label, box);
    }

    private static DataTemplate ColorTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var swatch = new FrameworkElementFactory(typeof(Border));
        swatch.SetValue(WidthProperty, 22.0);
        swatch.SetValue(HeightProperty, 11.0);
        swatch.SetValue(Border.BorderBrushProperty, Brushes.Gray);
        swatch.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        swatch.SetValue(MarginProperty, new Thickness(0, 0, 6, 0));
        swatch.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(ColorItem.Color)) { Converter = new RgbColorToBrushConverter() });
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ColorItem.Label)));
        panel.AppendChild(swatch);
        panel.AppendChild(label);
        return new DataTemplate { VisualTree = panel };
    }

    /// <summary>線幅の行。一覧から選ぶか数値を入れて Enter。「既定」は細線 (未指定)。</summary>
    private void AddWeightRow(string label, IReadOnlyList<double?> values, Action<double?> set)
    {
        var weights = values.Distinct().ToList();
        var box = new ComboBox
        {
            IsEditable = true,
            ItemsSource = new[] { "既定" }.Concat(StandardWeights.Select(w => w.ToString("0.00", CultureInfo.InvariantCulture))).ToList(),
            Text = weights.Count != 1 ? "" : weights[0] is double w ? w.ToString("0.00", CultureInfo.InvariantCulture) : "既定",
            ToolTip = "印刷時の線の太さ (mm)。一覧から選ぶか数値を入力して Enter",
        };
        var initial = box.Text;
        void Commit()
        {
            var text = box.Text.Trim();
            if (text == initial) return;
            if (text is "既定" or "")
            {
                initial = text;
                set(null);
            }
            else if (TryPositive(text, out var mm))
            {
                initial = text;
                set(mm);
            }
            else
            {
                box.Text = initial;
            }
        }
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is string s)
            {
                box.Text = s;
                Commit();
            }
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Commit();
            e.Handled = true;
        };
        box.LostKeyboardFocus += (_, _) => Commit();
        AddRow(label, box);
    }

    /// <summary>文字高 (用紙 mm) と幅係数。選択に文字が含まれるときだけ出し、文字だけに適用する。</summary>
    private void AddTextSizeRows(List<TextEntity> texts)
    {
        if (texts.Count == 0) return;
        // 画面で一定の文字は、保存するときの大きさ (用紙上 1px = 0.26mm) を出す
        var heights = texts.Select(t => Math.Round(t.ScreenHeightPx is double px ? px * TextEntity.MmPerPixel : t.Height / Doc.ScaleDenominator * 1000, 3)).Distinct().ToList();
        AddEditable("文字高 (mm)", heights.Count == 1 ? heights[0].ToString("0.###") : "", s =>
            TryPositive(s, out var h) && Apply(x =>
            {
                if (x is not TextEntity t) return;
                t.Height = Doc.PaperMmToMeters(h);
                t.ScreenHeightPx = null; // 用紙の大きさを決めたら、画面で一定ではなくなる
            }, "文字高変更"),
            $"用紙上の文字の高さ。図面の縮尺 {Doc.ScaleText} で m に換算されます");

        var pxs = texts.Select(t => t.ScreenHeightPx).Distinct().ToList();
        AddEditable("画面で一定 (px)", pxs.Count == 1 && pxs[0] is double onePx ? onePx.ToString("0.#") : "", s =>
            string.IsNullOrWhiteSpace(s)
                ? Apply(x => { if (x is TextEntity t) t.ScreenHeightPx = null; }, "文字の大きさを図面に固定")
                : TryPositive(s, out var px) && Apply(x =>
                {
                    if (x is not TextEntity t) return;
                    t.ScreenHeightPx = px;
                    t.FitWidth = null;
                }, "文字の大きさを画面で一定に"),
            "数値 (ピクセル) を入れると、縮尺・拡大率によらず画面上でこの大きさに見える。空欄にすると今の大きさで図面に固定");

        var factors = texts.Select(t => Math.Round(t.WidthFactor, 3)).Distinct().ToList();
        AddEditable("幅係数", factors.Count == 1 ? factors[0].ToString("0.###") : "", s =>
            TryPositive(s, out var f) && Apply(x =>
            {
                if (x is not TextEntity t) return;
                t.WidthFactor = f;
                t.FitWidth = null; // 幅指定 (SXF/DXF のフィット) は解除して幅係数で決める
            }, "幅係数変更"),
            "文字の横幅の倍率 (1 = 標準、0.8 = 細め)");
    }

    /// <summary>ハッチング・塗りつぶし・画像の透明度 (複数選択なら、選んだものすべてに)。</summary>
    private void AddTransparencyRow(List<Entity> targets)
    {
        if (targets.Count == 0) return;
        static double Get(Entity e) => e switch { HatchEntity h => h.TransparencyPercent, ImageEntity i => i.TransparencyPercent, _ => 0 };
        var values = targets.Select(h => Math.Round(Get(h), 1)).Distinct().ToList();
        AddEditable("透明度 (%)", values.Count == 1 ? values[0].ToString("0.#", CultureInfo.InvariantCulture) : "", s =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct) && pct is >= 0 and <= 100 &&
            Apply(x =>
            {
                if (x is HatchEntity h) h.TransparencyPercent = pct;
                else if (x is ImageEntity i) i.TransparencyPercent = pct;
            }, "透明度変更"),
            "0 = 不透明、50 = 半透明、100 = 透明。下の線や文字を透かして見せたいときに");
    }

    /// <summary>何も選択していないとき: これから描く図形の設定。</summary>
    private void AddDrawingSettings()
    {
        AddSection("作図の設定 (これから描く図形)");
        var layerBox = new ComboBox { ItemsSource = Doc.Layers.ToList(), DisplayMemberPath = nameof(Layer.Name), SelectedItem = Doc.CurrentLayer };
        layerBox.SelectionChanged += (_, _) =>
        {
            if (layerBox.SelectedItem is Layer l && l != Doc.CurrentLayer) Doc.CurrentLayer = l;
        };
        AddRow("レイヤ", layerBox);
        AddColorRow("色", [Doc.CurrentColor], c =>
        {
            Doc.CurrentColor = c;
            Committed?.Invoke(this, EventArgs.Empty);
        });
        AddWeightRow("線幅 (mm)", [Doc.CurrentLineWeight], w =>
        {
            Doc.CurrentLineWeight = w;
            Committed?.Invoke(this, EventArgs.Empty);
        });
        if (Tools.TextTool.ScreenFixed)
            AddEditable("文字の大きさ (画面 px)", Tools.TextTool.DefaultScreenPx.ToString("0.#"), s =>
            {
                if (!TryPositive(s, out var px)) return false;
                Tools.TextTool.DefaultScreenPx = px;
                Committed?.Invoke(this, EventArgs.Empty);
                QueueRefresh();
                return true;
            }, "文字ツールで書く文字の、画面上の高さ。縮尺・拡大率によらず一定 (文字ツールの入力欄で用紙 mm に切り替えられる)");
        else
        AddEditable("文字高 (mm)", Tools.TextTool.DefaultHeightMm.ToString("0.###"), s =>
        {
            if (!TryPositive(s, out var h)) return false;
            Tools.TextTool.DefaultHeightMm = h;
            Committed?.Invoke(this, EventArgs.Empty);
            QueueRefresh();
            return true;
        }, $"文字ツールで書く文字の高さ (用紙 mm)。{Doc.ScaleText} で {Doc.PaperMmToMeters(Tools.TextTool.DefaultHeightMm):0.###} m");
        AddNote("図形を選択すると、その図形の長さ・線幅・文字の大きさなどを表示・変更できます (Enter で確定、Ctrl+Z で戻す)。文字はダブルクリックか文字変更 (ED) で中身を変えられます。");
    }

    // ---- 編集の適用 ----

    /// <summary>選択中の図形を複製して modify を適用し、置き換える (Undo 可能)。</summary>
    private bool Apply(Action<Entity> modify, string name)
    {
        var sel = Doc.Entities.Where(_canvas!.Selection.Contains).ToList();
        if (sel.Count == 0) return false;
        var pairs = sel.Select(e =>
        {
            var c = e.Clone();
            modify(c);
            return (Old: e, New: c);
        }).ToList();
        Doc.History.Execute(new ReplaceEntitiesCommand(pairs, name));
        _canvas.SetSelection(pairs.Select(p => p.New));
        Committed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    // ---- 行の部品 ----

    private void AddSection(string title) =>
        _body.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, _body.Children.Count == 0 ? 2 : 10, 0, 3),
        });

    private void AddNote(string text) =>
        _body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 0) });

    private void AddRow(string label, FrameworkElement value)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var l = new TextBlock { Text = label, Foreground = LabelBrush, VerticalAlignment = System.Windows.VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(value, 1);
        grid.Children.Add(l);
        grid.Children.Add(value);
        _body.Children.Add(grid);
    }

    /// <summary>読み取り専用 (コピーできるよう TextBox で表示)。</summary>
    private void AddReadOnly(string label, string value) =>
        AddRow(label, new TextBox { Text = value, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, TextWrapping = TextWrapping.Wrap });

    /// <summary>編集可能。Enter かフォーカス移動で確定、Esc で元に戻す。commit が false なら元の値に戻す。</summary>
    private void AddEditable(string label, string value, Func<string, bool> commit, string? tip = null)
    {
        var box = new TextBox { Text = value, ToolTip = (tip != null ? tip + "\n" : "") + "書き換えて Enter で変更" };
        var done = false;
        void Commit()
        {
            if (done || box.Text == value) return;
            done = true;
            if (!commit(box.Text.Trim()))
            {
                box.Text = value;
                done = false;
            }
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { box.Text = value; e.Handled = true; }
        };
        box.LostKeyboardFocus += (_, _) => Commit();
        AddRow(label, box);
    }

    // ---- 表示用 ----

    public static double? LengthOf(Entity e) => e switch
    {
        LineEntity l => l.Length,
        PolylineEntity p => p.Length,
        ArcEntity a => a.Length,
        CircleEntity c => 2 * Math.PI * c.Radius,
        _ => null,
    };

    public static string KindName(Entity e) => e switch
    {
        LineEntity => "線",
        PolylineEntity { Filled: true } => "塗りつぶし",
        PolylineEntity => "ポリライン",
        CircleEntity => "円",
        PointEntity => "点",
        ArcEntity => "円弧",
        TextEntity => "文字",
        HatchEntity => "ハッチング",
        SymbolEntity s => s.Definition.Name,
        ImageEntity i when IO.PdfRenderer.IsPdf(i.Path) => "PDF",
        ImageEntity => "画像",
        _ => e.GetType().Name,
    };

    private static string Xy(Vec2 p) => $"x {p.X:F3}, y {p.Y:F3}";
    private static double Deg(double rad) => GeoMath.NormalizeAngle(rad) * 180 / Math.PI;
    private static string HName(TextHAlign h) => h switch { TextHAlign.Center => "中央", TextHAlign.Right => "右", _ => "左" };
    private static string VName(TextVAlign v) => v switch { TextVAlign.Middle => "中", TextVAlign.Top => "上", _ => "下" };

    private static bool TryPositive(string s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0;

    private static bool TryNumber(string s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && double.IsFinite(v);

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }
}
