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
/// キー操作: Tab で数値の欄へ (欄の中では次の欄へ)、Shift+Tab で長さの単位 (mm → cm → m)、
/// Enter で確定して作図に戻る、Esc で取り消し。下の段に今使えるキーのガイドを出す。
/// </summary>
public sealed class DrawInputBar : Border
{
    private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)));
    private static readonly Brush LiveBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x9C, 0xDC, 0xFE)));
    private static readonly Brush GuideBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA8)));
    private static readonly Brush KeyCapBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x44)));
    private static readonly Brush KeyCapBorder = Frozen(new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x75)));
    private static readonly Brush UnitBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x6A)));
    private static readonly double[] StandardWeights = [0.13, 0.18, 0.25, 0.35, 0.5, 0.7, 1.0, 1.4, 2.0];

    private readonly StackPanel _panel = new() { Orientation = Orientation.Horizontal };
    private readonly WrapPanel _guide = new() { Margin = new Thickness(0, 3, 0, 0) };
    private readonly TextBlock _live = new() { Foreground = LiveBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), FontFamily = new FontFamily("Consolas, Yu Gothic UI") };
    private readonly List<NumberField> _fields = [];
    private CadCanvas? _canvas;
    private CadDocument? _doc;

    public DrawInputBar()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26));
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46));
        BorderThickness = new Thickness(0, 0, 0, 1);
        var rows = new StackPanel { Margin = new Thickness(8, 3, 8, 3) };
        rows.Children.Add(_panel);
        rows.Children.Add(_guide);
        Child = rows;
        Visibility = Visibility.Collapsed;
    }

    /// <summary>値を確定した (コマンド欄にフォーカスを戻すのに使う)。</summary>
    public event EventHandler? Committed;

    /// <summary>今のツールに長さ・半径の欄がある (Shift+Tab で単位を切り替えられる)。</summary>
    public bool HasLengthField => Visibility == Visibility.Visible && _fields.Any(f => f.IsLength);

    public void Attach(CadCanvas canvas)
    {
        _canvas = canvas;
        canvas.ToolChanged += (_, _) => Rebuild();
        canvas.DocumentReplaced += (_, _) => WatchDocument();
        canvas.CursorMoved += (_, _) => UpdateLive();
        WatchDocument();
    }

    /// <summary>最初の数値の欄にカーソルを移す (Tab)。欄が無ければ false。</summary>
    public bool FocusFirstField()
    {
        if (Visibility != Visibility.Visible || _fields.Count == 0) return false;
        _fields[0].Focus();
        return true;
    }

    /// <summary>
    /// 長さの単位を次へ (mm → cm → m)。欄の数字はそのままで単位だけ変える
    /// (500 mm → 500 cm → 500 m)。確定済みの値も入力中の値も同じ扱い。
    /// </summary>
    public void CycleUnit()
    {
        DrawInputMemory.Unit = DrawInputMemory.Unit.Next();
        foreach (var f in _fields.Where(f => f.IsLength)) f.Commit(force: true);
        _canvas?.RedrawOverlay();
        UpdateGuide();
        UpdateLive();
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
        _fields.Clear();
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
                AddNumber("長さ", () => la.FixedLength, v => la.FixedLength = v, "次の点までの長さ。入れておくとクリックした方向にこの長さで線を引く", isLength: true);
                AddNumber("角度 (°)", () => la.FixedAngleDeg, v => la.FixedAngleDeg = v, "線の向き。東 = 0°、反時計回り (北 = 90°)", allowNegative: true);
                break;
            case ArcTool arcTool:
                AddNumber("半径", () => arcTool.FixedRadius, v => arcTool.FixedRadius = v,
                    "入れておくと 始点 → 終点 → 膨らむ側 の指定で、この半径の円弧を作図\n空欄なら 3 点 (始点・通過点・終点) で作図", isLength: true);
                break;
            case IRadiusInput ri:
                AddNumber("半径", () => ri.FixedRadius, v => ri.FixedRadius = v, "入れておくと、中心をクリックするだけでこの半径の円を作図", isLength: true);
                break;
            case PresetTool:
                AddPresetKind();
                AddNumber($"大きさ (mm, {doc.ScaleText})", () => PresetSettings.SizeMm,
                    v => { if (v is double s) { PresetSettings.SizeMm = s; _canvas?.Tool.RefreshPrompt(); } },
                    "用紙上の大きさ。図面の縮尺によらず、用紙上 (図面全体を見たとき) この大きさになる", allowEmpty: false);
                if (SymbolLibrary.Get(PresetSettings.Kind).PointsNorth)
                    AddNorthReference(doc);
                else
                    AddNumber("向き (°)", () => PresetSettings.AngleDeg,
                        v => { if (v is double a) { PresetSettings.AngleDeg = a; _canvas?.Tool.RefreshPrompt(); } },
                        "記号の上の向き。0 = 図面の上、反時計回りが正", allowEmpty: false, allowNegative: true);
                break;
            case MeasureToolBase:
                AddMeasureUnit(doc);
                AddCheck("結果を文字で記入", MeasureSettings.PlaceText, v => MeasureSettings.PlaceText = v,
                    "オンなら、計測のあと結果の文字がカーソルに付いて動き、クリックした位置に置ける (Enter で置かずに次へ)");
                AddNumber($"文字高 (mm, {doc.ScaleText})", () => MeasureSettings.TextHeightMm, v => { if (v is double h) MeasureSettings.TextHeightMm = h; },
                    "記入する文字の高さ (用紙上の mm)", allowEmpty: false);
                AddNumber("小数桁", () => MeasureSettings.Decimals, v => { if (v is double d) MeasureSettings.Decimals = (int)Math.Round(d); },
                    "小数点以下の桁数 (0〜6)", allowEmpty: false, allowNegative: true);
                AddCheck(tool is DistanceTool ? "方向角も記入" : "周長も記入", MeasureSettings.Detail, v => MeasureSettings.Detail = v,
                    tool is DistanceTool ? "例: L=12.345m  θ=45°00'00\"" : "例: A=123.456m²  (周長 45.678m)");
                break;
            case PerpendicularTool:
                AddNumber("長さ", () => PerpendicularTool.FixedLength, v => { PerpendicularTool.FixedLength = v; _canvas?.Tool.RefreshPrompt(); },
                    "垂線の長さ。入れておくと、終点はクリックした側にこの長さで決まる", isLength: true);
                break;
            case HatchTool hatchTool:
                if (!hatchTool.IsFill)
                {
                    AddHatchStyle();
                    AddNumber("角度 (°)", () => HatchSettings.AngleDeg, v => { if (v is double a) { HatchSettings.AngleDeg = a; _canvas?.Tool.RefreshPrompt(); } },
                        "ハッチ線の向き。東 = 0°、反時計回り (45° = 右上がり)", allowEmpty: false, allowNegative: true);
                    AddNumber($"間隔 (mm, {doc.ScaleText})", () => HatchSettings.SpacingMm, v => { if (v is double s) { HatchSettings.SpacingMm = s; _canvas?.Tool.RefreshPrompt(); } },
                        "用紙上の線の間隔。図面の縮尺で m に換算", allowEmpty: false);
                }
                AddNumber("透明度 (%)", () => HatchSettings.TransparencyPercent,
                    v => { if (v is double t) { HatchSettings.TransparencyPercent = t; _canvas?.Tool.RefreshPrompt(); } },
                    "0 = 不透明、50 = 半透明、100 = 透明。下の線や文字を透かして見せたいときに", allowEmpty: false, allowNegative: true);
                break;
            case TextTool:
                AddNumber($"文字高 (mm, {doc.ScaleText})", () => TextTool.DefaultHeightMm, v => { if (v is double h) TextTool.DefaultHeightMm = h; },
                    "用紙上の文字の高さ。図面の縮尺で m に換算", allowEmpty: false);
                break;
        }

        AddSeparator();
        AddWeight(doc);
        AddColor(doc);
        _panel.Children.Add(_live);
        UpdateLive();
        UpdateGuide();
    }

    /// <summary>今の長さ・角度・半径を表示する (固定していなくても目安になるように)。</summary>
    private void UpdateLive()
    {
        if (Visibility != Visibility.Visible || _canvas == null) return;
        var u = DrawInputMemory.Unit;
        string L(double m) => $"{FormatNumber(m / u.Meters())} {u.Label()}";
        _live.Text = _canvas.Tool switch
        {
            ILengthAngleInput { Live: var (len, ang) } => $"現在 長さ {L(len)}  角度 {ang:F2}°",
            IRadiusInput { LiveRadius: double r } => $"現在 半径 {L(r)}  直径 {L(r * 2)}",
            _ => "",
        };
    }

    // ---- キーのガイド ----

    private void UpdateGuide()
    {
        _guide.Children.Clear();
        if (_fields.Count == 0)
        {
            AddGuideText("線幅・色は一覧から選択");
            return;
        }

        var editing = _fields.FirstOrDefault(f => f.Box.IsKeyboardFocused);
        var unit = DrawInputMemory.Unit.Label();
        if (editing == null)
        {
            AddGuide("Tab", "数値を入力");
            if (HasLengthField) AddGuide("Shift+Tab", $"長さの単位を切替 mm→cm→m (今: {unit})");
            AddGuideText("空欄はマウスで決める");
            return;
        }

        AddGuide("Enter", "確定して作図に戻る");
        if (_fields.Count > 1) AddGuide("Tab", "次の欄へ");
        if (editing.IsLength) AddGuide("Shift+Tab", $"単位を切替 mm→cm→m (今: {unit})");
        AddGuide("Esc", "取り消して作図に戻る");
        AddGuideText(editing.AllowEmpty ? "空欄で確定するとマウスで決める" : "");
    }

    private void AddGuide(string key, string text)
    {
        var cap = new Border
        {
            Background = KeyCapBrush,
            BorderBrush = KeyCapBorder,
            BorderThickness = new Thickness(1, 1, 1, 2),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 0, 5, 0),
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = key, Foreground = Brushes.White, FontSize = 11, FontFamily = new FontFamily("Consolas, Yu Gothic UI") },
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
        row.Children.Add(cap);
        row.Children.Add(new TextBlock { Text = text, Foreground = GuideBrush, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        _guide.Children.Add(row);
    }

    private void AddGuideText(string text)
    {
        if (text.Length > 0)
            _guide.Children.Add(new TextBlock { Text = text, Foreground = GuideBrush, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
    }

    // ---- 部品 ----

    private void AddLabel(string text) =>
        _panel.Children.Add(new TextBlock { Text = text, Foreground = LabelBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });

    private void AddSeparator() =>
        _panel.Children.Add(new Border { Width = 1, Background = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x5A)), Margin = new Thickness(10, 2, 10, 2) });

    private static string FormatNumber(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>
    /// 数値の欄。値は get/set (長さは m)。表示と入力は今の単位。
    /// Enter で確定、Tab で次の欄、Shift+Tab で単位切替 (長さだけ)、Esc で元に戻す。空欄なら固定しない (null)。
    /// </summary>
    private void AddNumber(string label, Func<double?> get, Action<double?> set, string tip,
        bool isLength = false, bool allowEmpty = true, bool allowNegative = false)
    {
        AddLabel(label);
        var box = new TextBox
        {
            Width = 76,
            Margin = new Thickness(0, 0, isLength ? 3 : 12, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = tip + (allowEmpty ? "\n空欄 = マウスで決める" : "") + "\nEnter で確定 / Tab で次の欄" + (isLength ? " / Shift+Tab で単位 (mm→cm→m)" : ""),
        };
        var field = new NumberField(box, isLength, allowEmpty, allowNegative, get, set);
        _fields.Add(field);
        _panel.Children.Add(box);

        if (isLength)
        {
            var unitButton = new Button
            {
                Content = field.UnitLabel,
                MinWidth = 30,
                Padding = new Thickness(4, 0, 4, 0),
                Margin = new Thickness(0, 0, 12, 0),
                Foreground = UnitBrush,
                Background = KeyCapBrush,
                BorderBrush = KeyCapBorder,
                Focusable = false,
                ToolTip = "長さの単位 (クリックか Shift+Tab で mm → cm → m)",
            };
            unitButton.Click += (_, _) => CycleUnit();
            field.UnitLabel = unitButton;
            _panel.Children.Add(unitButton);
        }
        field.Refresh();

        box.GotKeyboardFocus += (_, _) => { box.SelectAll(); UpdateGuide(); };
        box.LostKeyboardFocus += (_, _) =>
        {
            field.Commit();
            UpdateGuide();
        };
        box.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter:
                    if (field.Commit()) Committed?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    field.Refresh();
                    Committed?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    break;
                case Key.Tab when Keyboard.Modifiers == ModifierKeys.Shift:
                    if (field.IsLength) CycleUnit();
                    e.Handled = true;
                    break;
                case Key.Tab when Keyboard.Modifiers == ModifierKeys.None:
                    if (field.Commit())
                    {
                        var next = _fields[(_fields.IndexOf(field) + 1) % _fields.Count];
                        if (next == field) box.SelectAll();
                        else next.Focus();
                    }
                    e.Handled = true;
                    break;
            }
        };
    }

    private sealed class NumberField(TextBox box, bool isLength, bool allowEmpty, bool allowNegative, Func<double?> get, Action<double?> set)
    {
        public TextBox Box { get; } = box;
        public bool IsLength { get; } = isLength;
        public bool AllowEmpty { get; } = allowEmpty;
        public Button? UnitLabel { get; set; }
        private string _shown = "";

        private double Factor => IsLength ? DrawInputMemory.Unit.Meters() : 1;

        public void Focus()
        {
            Box.Focus();
            Keyboard.Focus(Box);
            Box.SelectAll();
        }

        /// <summary>今の値を今の単位で表示し直す。</summary>
        public void Refresh()
        {
            _shown = get() is double v ? FormatNumber(v / Factor) : "";
            Box.Text = _shown;
            if (UnitLabel != null) UnitLabel.Content = DrawInputMemory.Unit.Label();
        }

        /// <summary>入力を確定する。不正な値なら元に戻して false。force なら同じ文字でも (単位を変えたとき) 読み直す。</summary>
        public bool Commit(bool force = false)
        {
            var text = Box.Text.Trim();
            if (text == _shown && !force)
            {
                Refresh();
                return true;
            }
            double? value;
            if (text.Length == 0 && AllowEmpty) value = null;
            else if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && (allowNegative || v > 0)) value = v * Factor;
            else
            {
                Refresh(); // 不正な値は戻す
                return false;
            }
            if (!Nullable.Equals(get(), value)) set(value); // 同じ値なら設定し直さない (円弧の点がリセットされないように)
            var focused = Box.IsKeyboardFocused;
            Refresh();
            if (focused) Box.SelectAll();
            return true;
        }
    }

    /// <summary>計測結果の単位 (mm / cm / m)。結果は CAD 上の長さに縮尺を掛けた値。</summary>
    private void AddMeasureUnit(CadDocument doc)
    {
        AddLabel("単位");
        var units = new[] { LengthUnit.Mm, LengthUnit.Cm, LengthUnit.M };
        var box = new ComboBox
        {
            Width = 56,
            Margin = new Thickness(0, 0, 6, 0),
            ItemsSource = units.Select(u => u.Label()).ToList(),
            SelectedIndex = Array.IndexOf(units, MeasureSettings.Unit),
            ToolTip = $"計測結果の単位。結果は CAD 上の長さに縮尺 ({doc.ScaleText}) を掛けた値 (面積は縮尺の 2 乗)",
        };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex < 0) return;
            MeasureSettings.Unit = units[box.SelectedIndex];
            Committed?.Invoke(this, EventArgs.Empty);
        };
        _panel.Children.Add(box);
        _panel.Children.Add(new TextBlock
        {
            Text = $"(×縮尺 {doc.ScaleText})",
            Foreground = LabelBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
        });
    }

    private void AddCheck(string label, bool value, Action<bool> set, string tip)
    {
        var box = new CheckBox
        {
            Content = label,
            IsChecked = value,
            Foreground = LabelBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
            Focusable = false,
            ToolTip = tip,
        };
        box.Click += (_, _) =>
        {
            set(box.IsChecked == true);
            _canvas?.RedrawOverlay();
            Committed?.Invoke(this, EventArgs.Empty);
        };
        _panel.Children.Add(box);
    }

    private void AddPresetKind()
    {
        AddLabel("種類");
        var defs = SymbolLibrary.All;
        var box = new ComboBox
        {
            Width = 170,
            Margin = new Thickness(0, 0, 12, 0),
            ItemsSource = defs.Select(d => d.IsUser ? $"★ {d.Name}" : d.Name).ToList(),
            SelectedIndex = Math.Max(0, defs.ToList().FindIndex(d => d.Kind == PresetSettings.Kind)),
            ToolTip = "置くプリセット",
        };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex < 0 || defs[box.SelectedIndex].Kind == PresetSettings.Kind) return;
            PresetSettings.Kind = defs[box.SelectedIndex].Kind;
            PresetSettings.SizeMm = defs[box.SelectedIndex].DefaultSizeMm;
            _canvas?.Tool.RefreshPrompt();
            Dispatcher.BeginInvoke(Rebuild); // 大きさの欄を標準の大きさにする
            Committed?.Invoke(this, EventArgs.Empty);
        };
        _panel.Children.Add(box);

        // 登録したプリセットは削除できる
        if (SymbolLibrary.Get(PresetSettings.Kind) is { IsUser: true } user)
        {
            var delete = new Button
            {
                Content = "削除",
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(-8, 0, 12, 0),
                Focusable = false,
                ToolTip = $"登録したプリセット「{user.Name}」を一覧から削除する (図面に置いたものは残る)",
            };
            delete.Click += (_, _) =>
            {
                if (MessageBox.Show(Window.GetWindow(this), $"プリセット「{user.Name}」を削除しますか？\n(図面に置いたものはそのまま残ります)",
                        "プリセットの削除", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                SymbolLibrary.RemoveUser(user.Kind);
                Settings.UserPresetStore.Save();
                var first = SymbolLibrary.BuiltIn[0];
                PresetSettings.Kind = first.Kind;
                PresetSettings.SizeMm = first.DefaultSizeMm;
                _canvas?.Tool.RefreshPrompt();
                Dispatcher.BeginInvoke(Rebuild);
                Committed?.Invoke(this, EventArgs.Empty);
            };
            _panel.Children.Add(delete);
        }
    }

    /// <summary>方位記号の北: 真北 (子午線収差を補正) か 座標北 (図面の上) か。図面の方位記号すべてに効く。</summary>
    private void AddNorthReference(CadDocument doc)
    {
        AddLabel("北");
        var box = new ComboBox
        {
            Width = 80,
            Margin = new Thickness(0, 0, 8, 0),
            ItemsSource = new[] { "真北", "座標北" },
            SelectedIndex = doc.UseTrueNorth ? 0 : 1,
            ToolTip = "方位記号の N の向き。置いた後も、移動・回転しても常にこの北を指す (図面の方位記号すべてに効く)\n" +
                      "真北 = 平面直角座標の系から子午線収差を補正した北 / 座標北 = 図面の上 (X 軸)",
        };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex < 0) return;
            doc.UseTrueNorth = box.SelectedIndex == 0;
            _canvas?.Tool.RefreshPrompt();
            _canvas?.RedrawOverlay();
            Dispatcher.BeginInvoke(Rebuild);
            Committed?.Invoke(this, EventArgs.Empty);
        };
        _panel.Children.Add(box);
        if (doc.UseTrueNorth && !doc.CanUseTrueNorth)
            _panel.Children.Add(new TextBlock
            {
                Text = "系が未設定のため図面の上 (座標系の系番号 ZN で設定)",
                Foreground = System.Windows.Media.Brushes.DarkOrange,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
            });
        else if (doc.UseTrueNorth)
            _panel.Children.Add(new TextBlock { Text = $"{doc.PlaneZone}系", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
    }

    private void AddHatchStyle()
    {
        AddLabel("種類");
        var box = new ComboBox
        {
            Width = 90,
            Margin = new Thickness(0, 0, 12, 0),
            ItemsSource = Enum.GetValues<HatchStyle>().Select(HatchSettings.StyleName).ToList(),
            SelectedIndex = (int)HatchSettings.Style,
            ToolTip = "斜線 = 1 方向の平行線、クロス = 直交する 2 方向、塗りつぶし = 中を塗る",
        };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex < 0) return;
            HatchSettings.Style = (HatchStyle)box.SelectedIndex;
            _canvas?.Tool.RefreshPrompt();
            _canvas?.RedrawOverlay();
            Committed?.Invoke(this, EventArgs.Empty);
        };
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
