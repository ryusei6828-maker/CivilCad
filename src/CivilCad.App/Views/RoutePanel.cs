using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CivilCad.Alignment;
using CivilCad.App.Alignment;
using CivilCad.App.Tools;
using CivilCad.Core.Geometry;
using HorizontalAlignment = CivilCad.Alignment.HorizontalAlignment;

namespace CivilCad.App.Views;

/// <summary>
/// 路線パネル (路線作成 RL): 始点 → IP → … → 終点 を表で入れて計算し、通過点 (No.杭・中間杭・主要点) を選んで作図する。
/// ・表の点は図面でクリックして入れるか、直接入力する。Excel などとコピー・貼り付けできる (Ctrl+C / Ctrl+V)
/// ・列: 属性 (始点 / IP / クロソイド / 終点)・点名・X (北)・Y (東)・R・A1・A2
/// ・計算のあと、No.杭の間隔 (20m など)・中間杭 (10m ごとなど)・主要点・幅杭を選ぶと通過点の一覧が変わる
/// </summary>
public sealed class RoutePanel : DockPanel
{
    private readonly ObservableCollection<RouteRow> _rows = [];
    private readonly ObservableCollection<PassingRow> _passing = [];
    private readonly DataGrid _grid;
    private readonly DataGrid _passGrid;
    private readonly TextBox _name = new() { Width = 110, Text = "路線1", Margin = new Thickness(0, 0, 10, 0) };
    private readonly TextBox _start = new() { Width = 70, Text = "0", ToolTip = "始点の追加距離 (m)。0 なら始点が No.0" };
    private readonly ComboBox _pitch = new() { Width = 70, IsEditable = true, Text = "20", ToolTip = "No.杭の間隔 (m)。ふつうは 20m" };
    private readonly ComboBox _sub = new() { Width = 70, IsEditable = true, Text = "なし", ToolTip = "No.杭の間に入れる中間杭 (プラス杭) の間隔 (m)。例: 10 → No.1+10.00" };
    private readonly CheckBox _main = new() { Content = "主要点 (BC・EC・KA・KE など)", IsChecked = true, Margin = new Thickness(0, 0, 12, 0) };
    private readonly CheckBox _register = new() { Content = "通過点を座標管理に登録", IsChecked = true, ToolTip = "通過点を点名付きの点にして座標管理の一覧に出す (タグ: 中心杭・中間杭・主要点)" };
    private readonly TextBox _left = new() { Width = 80, Margin = new Thickness(0, 0, 8, 0), ToolTip = "中心線から左の幅 (m)。カンマ区切りで複数 (例: 3.0, 5.5)。空なら作図しない" };
    private readonly TextBox _right = new() { Width = 80, Margin = new Thickness(0, 0, 8, 0), ToolTip = "中心線から右の幅 (m)。カンマ区切りで複数" };
    private readonly CheckBox _shoulder = new() { Content = "路肩線", IsChecked = true, Margin = new Thickness(0, 0, 8, 0) };
    private readonly CheckBox _stake = new() { Content = "幅杭", IsChecked = true };
    private readonly TextBox _textHeight = new() { Width = 50, Text = "3.5" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    private CadCanvas? _canvas;
    private HorizontalAlignment? _result;
    private AlignmentInput _resultInput = new();
    private bool _loading;

    public event EventHandler? CloseRequested;
    public event EventHandler? FloatToggleRequested;

    /// <summary>作図した (測点照会に使えるよう、メイン画面が覚える)。</summary>
    public event Action<AlignmentInput, HorizontalAlignment>? Drawn;

    /// <summary>メッセージ欄に出す。</summary>
    public Action<string>? Log { get; set; }

    public Button FloatButton { get; } = new() { Content = "⧉", Padding = new Thickness(6, 0, 6, 0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Focusable = false, ToolTip = "フロート (別ウィンドウに切り離す)" };

    public ObservableCollection<RouteRow> Rows => _rows;
    public IReadOnlyList<PassingRow> Passing => _passing;
    public HorizontalAlignment? Result => _result;
    public DataGrid Grid => _grid;

    /// <summary>通過点の一覧の 1 行。</summary>
    public sealed record PassingRow(string 点名, string 種類, string 追加距離, string X, string Y);

    public RoutePanel()
    {
        Width = 560;
        Margin = new Thickness(4);
        Background = Brushes.White;

        // 見出し
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var close = new Button { Content = "×", Padding = new Thickness(6, 0, 6, 0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Focusable = false, ToolTip = "閉じる" };
        close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        FloatButton.Click += (_, _) => FloatToggleRequested?.Invoke(this, EventArgs.Empty);
        SetDock(close, Dock.Right);
        SetDock(FloatButton, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(FloatButton);
        header.Children.Add(new TextBlock { Text = "路線", FontWeight = FontWeights.Bold, VerticalAlignment = System.Windows.VerticalAlignment.Center });
        SetDock(header, Dock.Top);
        Children.Add(header);

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 路線名
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // ボタン
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star), MinHeight = 120 }); // 表
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 計算
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 通過点の選択肢
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star), MinHeight = 90 });  // 通過点一覧
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 作図
        Children.Add(body);
        void Put(UIElement e, int row)
        {
            System.Windows.Controls.Grid.SetRow(e, row);
            body.Children.Add(e);
        }

        // 路線名・開始追加距離
        var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        top.Children.Add(Label("路線名"));
        top.Children.Add(_name);
        top.Children.Add(Label("開始追加距離 (m)"));
        top.Children.Add(_start);
        Put(top, 0);

        // 表の操作
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        Button B(string text, string tip, Action run)
        {
            var b = new Button { Content = text, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 4, 4), ToolTip = tip };
            b.Click += (_, _) => run();
            buttons.Children.Add(b);
            return b;
        }
        B("図面から追加", "図面で 始点 → IP → … → 終点 を順にクリックして表の最後に足す (Enter で終わる、最後の点が終点)", PickAppend);
        B("選んだ行を図面で指定", "表で選んだ行の座標を、図面でクリックした点にする", PickSelected);
        B("行を挿入", "選んだ行の前に空の行を入れる", InsertRow);
        B("行を削除", "選んだ行を消す", DeleteRows);
        B("貼り付け", "Excel などでコピーした表を、選んだセルから貼り付ける (Ctrl+V と同じ)", () => PasteText(Clipboard.GetText()));
        B("全部消す", "表を空にする", () => { _rows.Clear(); Invalidate(); });
        Put(buttons, 1);

        // 表: 属性・点名・X・Y・R・A1・A2
        _grid = new DataGrid
        {
            ItemsSource = _rows,
            AutoGenerateColumns = false,
            CanUserAddRows = true,
            CanUserDeleteRows = false,
            CanUserSortColumns = false,
            SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
            SelectionMode = DataGridSelectionMode.Extended,
            ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader,
            HeadersVisibility = DataGridHeadersVisibility.All,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            RowHeaderWidth = 18,
        };
        _grid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "属性",
            ItemsSource = RouteTable.Kinds,
            SelectedItemBinding = new Binding(nameof(RouteRow.Kind)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = 80,
        });
        DataGridTextColumn Col(string header, string path, double width) => new()
        {
            Header = header,
            Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = width > 0 ? new DataGridLength(width) : new DataGridLength(1, DataGridLengthUnitType.Star),
        };
        _grid.Columns.Add(Col("点名", nameof(RouteRow.Name), 60));
        _grid.Columns.Add(Col("X (北)", nameof(RouteRow.X), 100));
        _grid.Columns.Add(Col("Y (東)", nameof(RouteRow.Y), 100));
        _grid.Columns.Add(Col("R", nameof(RouteRow.R), 55));
        _grid.Columns.Add(Col("A1", nameof(RouteRow.A1), 50));
        _grid.Columns.Add(Col("A2", nameof(RouteRow.A2), 50));
        _grid.PreviewKeyDown += Grid_PreviewKeyDown;
        _grid.CellEditEnding += (_, _) => Invalidate();
        var tableBox = new DockPanel();
        var note = new TextBlock
        {
            Text = "座標は測量座標 (X = 北、Y = 東)。IP の R が空・0 なら折点。クロソイドは R と A1 (A2 が空なら A1 と同じ)。" +
                   "Excel などの表は選んだセルから Ctrl+V で貼り付け、Ctrl+C でコピー、Delete でセルを空に。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 3),
        };
        SetDock(note, Dock.Top);
        tableBox.Children.Add(note);
        tableBox.Children.Add(_grid);
        Put(new GroupBox { Header = "始点 → IP → 終点", Content = tableBox }, 2);

        // 計算
        var calc = new StackPanel();
        var calcButton = new Button { Content = "計算", Width = 90, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        calcButton.Click += (_, _) => Compute();
        calc.Children.Add(calcButton);
        calc.Children.Add(_status);
        Put(calc, 3);

        // 通過点の選択肢
        foreach (var p in new[] { "10", "20", "25", "50", "100" }) _pitch.Items.Add(p);
        foreach (var p in new[] { "なし", "5", "10", "25" }) _sub.Items.Add(p);
        var options = new StackPanel();
        var o1 = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        o1.Children.Add(Label("No.杭の間隔 (m)"));
        o1.Children.Add(_pitch);
        o1.Children.Add(Label("  中間杭 (m)"));
        o1.Children.Add(_sub);
        options.Children.Add(o1);
        var o2 = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        o2.Children.Add(_main);
        o2.Children.Add(_register);
        options.Children.Add(o2);
        var o3 = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        o3.Children.Add(Label("幅 左 (m)"));
        o3.Children.Add(_left);
        o3.Children.Add(Label("右 (m)"));
        o3.Children.Add(_right);
        o3.Children.Add(_shoulder);
        o3.Children.Add(_stake);
        o3.Children.Add(Label("   文字高 (mm)"));
        o3.Children.Add(_textHeight);
        options.Children.Add(o3);
        Put(new GroupBox { Header = "通過点 (計算のあと選ぶと一覧が変わる)", Content = options }, 4);
        foreach (var c in new[] { _pitch, _sub })
        {
            c.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(OptionsChanged);
            c.LostKeyboardFocus += (_, _) => OptionsChanged();
            c.KeyDown += (_, e) => { if (e.Key == Key.Enter) OptionsChanged(); };
        }
        foreach (var c in new[] { _main, _register, _shoulder, _stake }) c.Click += (_, _) => OptionsChanged();
        foreach (var t in new[] { _left, _right, _textHeight, _name, _start }) t.LostKeyboardFocus += (_, _) => OptionsChanged();

        // 通過点一覧
        _passGrid = new DataGrid
        {
            ItemsSource = _passing,
            IsReadOnly = true,
            AutoGenerateColumns = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader,
            SelectionMode = DataGridSelectionMode.Extended,
        };
        Put(new GroupBox { Header = "通過点一覧 (Ctrl+C でコピーできる)", Content = _passGrid }, 5);

        // 作図
        var draw = new Button { Content = "作図", Width = 120, Margin = new Thickness(0, 6, 0, 0), ToolTip = "中心線・No.杭・中間杭・主要点・幅を作図する (同じ路線名の作図は置き換える)" };
        draw.Click += (_, _) => Draw();
        var xml = new Button { Content = "XML 出力...", Width = 110, Margin = new Thickness(0, 6, 8, 0), ToolTip = "路線 (平面線形・IP・測点間隔) を LandXML (J-LandXML) に書き出す" };
        xml.Click += (_, _) => ExportXml();
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        bottom.Children.Add(xml);
        bottom.Children.Add(draw);
        Put(bottom, 6);
    }

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };

    public void Attach(CadCanvas canvas) => _canvas = canvas;

    /// <summary>今の路線 (路線計算・XML 読込などで作ったもの) を表と選択肢に入れる。</summary>
    public void Load(AlignmentInput input)
    {
        _loading = true;
        _name.Text = input.Name;
        _start.Text = input.StartStation.ToString(CultureInfo.InvariantCulture);
        _pitch.Text = input.Pitch.ToString(CultureInfo.InvariantCulture);
        _sub.Text = input.SubPitch > 0 ? input.SubPitch.ToString(CultureInfo.InvariantCulture) : "なし";
        _main.IsChecked = input.IncludeMainPoints;
        _left.Text = input.LeftWidths;
        _right.Text = input.RightWidths;
        _shoulder.IsChecked = input.DrawShoulders;
        _stake.IsChecked = input.DrawStakes;
        _textHeight.Text = input.TextHeightMm.ToString(CultureInfo.InvariantCulture);
        _rows.Clear();
        foreach (var r in RouteTable.FromInput(input)) _rows.Add(r);
        _loading = false;
        Invalidate();
    }

    // ---- 表の操作 ----

    private int CurrentRowIndex()
    {
        var item = _grid.CurrentCell.Item ?? _grid.SelectedCells.FirstOrDefault().Item;
        return item is RouteRow r ? _rows.IndexOf(r) : _rows.Count;
    }

    private int CurrentColumnIndex() => _grid.CurrentCell.Column?.DisplayIndex ?? 0;

    /// <summary>Excel などの表 (タブ区切り) を、選んだセルから貼る。</summary>
    public void PasteText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _grid.CommitEdit();
        _grid.CommitEdit();
        var n = RouteTable.Paste(_rows, CurrentRowIndex(), CurrentColumnIndex(), text);
        Invalidate();
        Log?.Invoke($"路線: {n} 行を貼り付けました。");
    }

    private void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        var editingText = Keyboard.FocusedElement is TextBox;
        if (ctrl && e.Key == Key.V)
        {
            var text = Clipboard.GetText();
            // セルの中で 1 つの値を貼るときは、ふつうの貼り付け
            if (editingText && !text.Contains('\t') && !text.TrimEnd('\r', '\n').Contains('\n')) return;
            PasteText(text);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && !editingText)
        {
            // 選んだセルを空にする (Excel と同じ。行は消さない)
            foreach (var cell in _grid.SelectedCells)
                if (cell.Item is RouteRow r && cell.Column.DisplayIndex > 0) r[cell.Column.DisplayIndex] = "";
            Invalidate();
            e.Handled = true;
        }
    }

    private void InsertRow()
    {
        var i = Math.Min(CurrentRowIndex(), _rows.Count);
        _rows.Insert(i, new RouteRow());
        Invalidate();
    }

    private void DeleteRows()
    {
        var targets = _grid.SelectedCells.Select(c => c.Item).OfType<RouteRow>().Distinct().ToList();
        foreach (var r in targets) _rows.Remove(r);
        RouteTable.FixKinds(_rows);
        Invalidate();
    }

    // ---- 図面から点を入れる ----

    private Vec2? LastRowPoint()
    {
        if (_canvas == null) return null;
        var last = _rows.LastOrDefault(r => !r.IsEmpty);
        return last != null && double.TryParse(last.X, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && double.TryParse(last.Y, NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            ? _canvas.Document.FromSurvey(x, y) : null;
    }

    private (string X, string Y) Survey(Vec2 p)
    {
        var (x, y) = _canvas!.Document.ToSurvey(p);
        return (x.ToString("0.000", CultureInfo.InvariantCulture), y.ToString("0.000", CultureInfo.InvariantCulture));
    }

    public void PickAppend()
    {
        if (_canvas == null) return;
        _grid.CommitEdit();
        // 最後の空の行は消してから足す
        while (_rows.Count > 0 && _rows[^1].IsEmpty) _rows.RemoveAt(_rows.Count - 1);
        _canvas.SetTool(new RoutePickTool(p =>
        {
            var (x, y) = Survey(p);
            var used = _rows.Count(r => !r.IsEmpty);
            _rows.Add(new RouteRow { Kind = used == 0 ? RouteTable.KindStart : RouteTable.KindIp, X = x, Y = y });
            RouteTable.FixKinds(_rows);
            Invalidate();
        }, () => { RouteTable.FixKinds(_rows); Invalidate(); }, LastRowPoint, single: false));
    }

    public void PickSelected()
    {
        if (_canvas == null) return;
        _grid.CommitEdit();
        var i = CurrentRowIndex();
        if (i >= _rows.Count)
        {
            Log?.Invoke("路線: 座標を入れる行を表で選んでください。");
            return;
        }
        var row = _rows[i];
        _canvas.SetTool(new RoutePickTool(p =>
        {
            (row.X, row.Y) = Survey(p);
            RouteTable.FixKinds(_rows);
            Invalidate();
        }, () => { }, () => null, single: true));
    }

    // ---- 計算・通過点・作図 ----

    /// <summary>表や設定が変わった: 計算結果を無効にする (計算し直すまで作図できない)。</summary>
    private void Invalidate()
    {
        if (_loading) return;
        _result = null;
        _passing.Clear();
        _status.Foreground = Brushes.Gray;
        _status.Text = "表を入れたら「計算」を押してください。";
    }

    private static double Num(string text, string label)
    {
        var t = text.Trim().TrimEnd('m', 'ｍ').Trim();
        if (t is "" or "なし") return 0;
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : throw new AlignmentException($"{label} が数値ではありません。");
    }

    /// <summary>表と選択肢から路線の入力を作る。</summary>
    private AlignmentInput BuildInput()
    {
        _grid.CommitEdit();
        _grid.CommitEdit();
        var settings = new AlignmentInput
        {
            Name = string.IsNullOrWhiteSpace(_name.Text) ? "路線1" : _name.Text.Trim(),
            StartStation = Num(_start.Text, "開始追加距離"),
            Pitch = Num(_pitch.Text, "No.杭の間隔"),
            SubPitch = Num(_sub.Text, "中間杭の間隔"),
            IncludeMainPoints = _main.IsChecked == true,
            RegisterPoints = _register.IsChecked == true,
            LeftWidths = _left.Text.Trim(),
            RightWidths = _right.Text.Trim(),
            DrawShoulders = _shoulder.IsChecked == true,
            DrawStakes = _stake.IsChecked == true,
            TextHeightMm = Num(_textHeight.Text, "文字高"),
        };
        if (!(settings.Pitch > 0)) throw new AlignmentException("No.杭の間隔は 0 より大きい数値にしてください。");
        if (settings.SubPitch < 0 || settings.SubPitch >= settings.Pitch && settings.SubPitch > 0)
            throw new AlignmentException("中間杭の間隔は No.杭の間隔より小さい数値にしてください (なしも可)。");
        if (!(settings.TextHeightMm > 0)) throw new AlignmentException("文字高は 0 より大きい数値にしてください。");
        AlignmentInput.ParseWidths(settings.LeftWidths, "左の幅");
        AlignmentInput.ParseWidths(settings.RightWidths, "右の幅");
        return RouteTable.ToInput(_rows, settings);
    }

    /// <summary>計算する。成功すれば通過点の一覧を出す。</summary>
    public bool Compute()
    {
        try
        {
            RouteTable.FixKinds(_rows);
            _resultInput = BuildInput();
            _result = _resultInput.Compute();
            ShowPassing();
            _status.Foreground = Brushes.DarkGreen;
            _status.Text = $"延長 {_result.Length:F3} m ({Station.Format(_result.StartStation, _resultInput.Pitch)} 〜 {Station.Format(_result.EndStation, _resultInput.Pitch)})、" +
                           $"曲線 {_result.Curves.Count(c => c.Radius > 0)} 箇所、通過点 {_passing.Count} 点。通過点を選んで「作図」を押してください。";
            return true;
        }
        catch (AlignmentException ex)
        {
            _result = null;
            _passing.Clear();
            _status.Foreground = Brushes.Firebrick;
            _status.Text = ex.Message;
            return false;
        }
    }

    /// <summary>通過点の選択肢が変わった: 計算済みなら一覧を作り直す。</summary>
    private void OptionsChanged()
    {
        if (_loading || _result == null) return;
        Compute();
    }

    private void ShowPassing()
    {
        _passing.Clear();
        if (_result == null) return;
        foreach (var p in _resultInput.PassingPoints(_result))
        {
            var (x, y) = p.Point.ToSurvey();
            _passing.Add(new PassingRow(p.Name, p.Kind, p.Station.ToString("F3", CultureInfo.InvariantCulture),
                x.ToString("F3", CultureInfo.InvariantCulture), y.ToString("F3", CultureInfo.InvariantCulture)));
        }
    }

    /// <summary>計算して、路線を LandXML に書き出す (保存先を選ぶ)。</summary>
    public bool ExportXml()
    {
        if (!Compute() || _result == null) return false;
        if (LandXmlExport.Run(Window.GetWindow(this), _resultInput, _canvas?.Document.PlaneZone) is not { } path) return false;
        Log?.Invoke($"路線「{_resultInput.Name}」を XML (LandXML) に書き出しました: {path}");
        return true;
    }

    /// <summary>作図する (計算していなければ計算してから)。同じ路線名の作図は置き換える。</summary>
    public bool Draw()
    {
        if (_canvas == null || !Compute() || _result == null) return false;
        var doc = _canvas.Document;
        doc.History.Execute(AlignmentDrawer.CreateDrawCommand(doc, _result, _resultInput));
        Drawn?.Invoke(_resultInput, _result);
        Log?.Invoke($"路線「{_resultInput.Name}」を作図しました。延長 {_result.Length:F3} m、通過点 {_passing.Count} 点" +
                    (_resultInput.RegisterPoints ? " (座標管理に登録)" : "") + "。");
        return true;
    }
}
