using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CivilCad.App.IO;
using CivilCad.App.Tools;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;
using Microsoft.Win32;

namespace CivilCad.App.Views;

/// <summary>座標管理の一覧の 1 行 (編集用)。X = 北, Y = 東。Z は空なら標高なし。</summary>
public sealed class CoordinateItem
{
    public required PointEntity Point { get; init; }
    public string Name { get; set; } = "";
    public string X { get; set; } = "";
    public string Y { get; set; } = "";
    public string Z { get; set; } = "";
}

/// <summary>
/// 座標管理: 登録された座標 (点名のある点。座標読込の点・幅杭・点コマンドの点) を 点名・X・Y・Z の一覧で表示する。
/// 一覧で点名・座標・標高を直す、削除する、図面で点を追加する、CSV / SIMA に書き出す・読み込む。
/// 一覧で選んだ点は図面でも選択する。
/// </summary>
public sealed class CoordinatePanel : DockPanel
{
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserSortColumns = true,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        SelectionMode = DataGridSelectionMode.Extended,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
    };
    private readonly TextBlock _title = new() { Text = "座標管理", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
    private readonly ObservableCollection<CoordinateItem> _items = [];
    private CadCanvas? _canvas;
    private CadDocument? _watched;
    private bool _refreshQueued;
    private bool _editing;
    private bool _syncing;

    /// <summary>メイン画面のコマンドを実行する (座標読込など)。</summary>
    public Action<string>? RunCommand { get; set; }

    public event EventHandler? CloseRequested;
    public event EventHandler? FloatToggleRequested;

    public Button FloatButton { get; } = new() { Content = "⧉", Padding = new Thickness(6, 0, 6, 0), BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, Focusable = false, ToolTip = "フロート (別ウィンドウに切り離す)" };

    public IReadOnlyList<CoordinateItem> Items => _items;

    public CoordinatePanel()
    {
        Width = 380;
        Margin = new Thickness(4);
        Background = System.Windows.Media.Brushes.White;

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var close = new Button { Content = "×", Padding = new Thickness(6, 0, 6, 0), BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, Focusable = false, ToolTip = "閉じる" };
        close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        FloatButton.Click += (_, _) => FloatToggleRequested?.Invoke(this, EventArgs.Empty);
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(FloatButton, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(FloatButton);
        header.Children.Add(_title);
        SetDock(header, Dock.Top);
        Children.Add(header);

        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        Button B(string text, string tip, Action run)
        {
            var b = new Button { Content = text, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 4, 4), ToolTip = tip };
            b.Click += (_, _) => run();
            buttons.Children.Add(b);
            return b;
        }
        B("図面で追加", "点 (PO) で図面をクリックして点を登録する (点名は P1, P2 …)", () => _canvas?.SetTool(new PointTool()));
        B("削除", "一覧で選んだ点を図面から消す", DeleteSelected);
        B("表示", "一覧で選んだ点が見えるように拡大する", ZoomToSelected);
        B("CSV 書き出し", "点名,X,Y,Z の CSV に書き出す (選んでいれば選んだ点だけ)", () => Export(csv: true));
        B("SIMA 書き出し", "SIMA (A01) に書き出す (選んでいれば選んだ点だけ)", () => Export(csv: false));
        B("読込", "SIMA / CSV の座標を読み込んで登録する", () => RunCommand?.Invoke("PI"));
        SetDock(buttons, Dock.Top);
        Children.Add(buttons);

        var hint = new TextBlock
        {
            Text = "X = 北, Y = 東 (測量座標。座標設定の向きを含む)。点名・X・Y・Z はここで直せます (Z は空で標高なし)。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 4),
        };
        SetDock(hint, Dock.Top);
        Children.Add(hint);

        DataGridTextColumn Col(string header, string path, double width) => new()
        {
            Header = header,
            Binding = new System.Windows.Data.Binding(path) { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus },
            Width = new DataGridLength(width, DataGridLengthUnitType.Star),
        };
        _grid.Columns.Add(Col("点名", nameof(CoordinateItem.Name), 1.4));
        _grid.Columns.Add(Col("X (北)", nameof(CoordinateItem.X), 1.2));
        _grid.Columns.Add(Col("Y (東)", nameof(CoordinateItem.Y), 1.2));
        _grid.Columns.Add(Col("Z", nameof(CoordinateItem.Z), 0.8));
        _grid.ItemsSource = _items;
        _grid.BeginningEdit += (_, _) => _editing = true;
        _grid.RowEditEnding += (_, e) =>
        {
            _editing = false;
            if (e.EditAction == DataGridEditAction.Commit && e.Row.Item is CoordinateItem item)
                Dispatcher.BeginInvoke(() => Apply(item)); // バインドの書き戻しの後で
        };
        _grid.SelectionChanged += (_, _) =>
        {
            if (_syncing || _canvas == null) return;
            _syncing = true;
            _canvas.SetSelection(_grid.SelectedItems.OfType<CoordinateItem>().Select(i => i.Point));
            _syncing = false;
        };
        _grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && !_editing)
            {
                DeleteSelected();
                e.Handled = true;
            }
        };
        Children.Add(_grid);
    }

    public void Attach(CadCanvas canvas)
    {
        _canvas = canvas;
        canvas.DocumentReplaced += (_, _) => Watch(canvas.Document);
        canvas.SelectionChanged += (_, _) => SyncFromCanvas();
        Watch(canvas.Document);
    }

    private void Watch(CadDocument doc)
    {
        if (_watched != null) _watched.Changed -= OnDocumentChanged;
        _watched = doc;
        doc.Changed += OnDocumentChanged;
        Refresh();
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _refreshQueued = false;
            if (!_editing) Refresh();
        });
    }

    private static string F(double v) => v.ToString("F3", CultureInfo.InvariantCulture);

    /// <summary>一覧を図面の今の点から作り直す。</summary>
    public void Refresh()
    {
        if (_watched is not { } doc) return;
        var selected = _grid.SelectedItems.OfType<CoordinateItem>().Select(i => i.Point).ToHashSet();
        _syncing = true;
        _items.Clear();
        foreach (var r in CoordinateRegistry.Rows(doc))
            _items.Add(new CoordinateItem { Point = r.Point, Name = r.Name, X = F(r.X), Y = F(r.Y), Z = r.Z is double z ? F(z) : "" });
        foreach (var i in _items.Where(i => selected.Contains(i.Point))) _grid.SelectedItems.Add(i);
        _syncing = false;
        _title.Text = $"座標管理  ({_items.Count} 点)";
    }

    private void SyncFromCanvas()
    {
        if (_syncing || _canvas == null) return;
        _syncing = true;
        _grid.SelectedItems.Clear();
        foreach (var i in _items.Where(i => _canvas.Selection.Contains(i.Point))) _grid.SelectedItems.Add(i);
        _syncing = false;
    }

    private static bool TryNum(string s, out double v) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && double.IsFinite(v);

    /// <summary>一覧で直した値を図面の点に反映する (元に戻せる)。</summary>
    public bool Apply(CoordinateItem item)
    {
        if (_watched is not { } doc || _canvas == null) return false;
        double? z = null;
        if (item.Name.Trim().Length == 0 || !TryNum(item.X, out var x) || !TryNum(item.Y, out var y) ||
            (item.Z.Trim().Length > 0 && !(TryNum(item.Z, out var zv) && (z = zv) != null)))
        {
            _canvas.ShowMessage("座標管理: 点名は空にできません。X・Y・Z は数で入れてください (Z は空で標高なし)。");
            Refresh();
            return false;
        }
        var p = item.Point;
        var pos = doc.FromSurvey(x, y);
        if (p.Name == item.Name.Trim() && p.Position.DistanceTo(pos) < 1e-9 && p.Z == z) return true;
        var made = (PointEntity)p.Clone();
        made.Name = item.Name.Trim();
        made.Position = pos;
        made.Z = z;
        doc.History.Execute(new ReplaceEntitiesCommand([(p, made)], "座標の変更"));
        _canvas.SetSelection([made]);
        Refresh();
        return true;
    }

    private List<CoordinateItem> Selected() => [.. _grid.SelectedItems.OfType<CoordinateItem>()];

    private void DeleteSelected()
    {
        if (_watched is not { } doc || Selected() is not { Count: > 0 } sel) return;
        doc.History.Execute(new RemoveEntitiesCommand([.. sel.Select(i => i.Point)], $"座標の削除 ({sel.Count} 点)"));
        _canvas?.ClearSelection();
        _canvas?.ShowMessage($"座標管理: {sel.Count} 点を削除しました (Ctrl+Z で戻せます)。");
    }

    private void ZoomToSelected()
    {
        if (_canvas == null) return;
        var pts = (Selected() is { Count: > 0 } sel ? sel : [.. _items]).Select(i => i.Point.Position).ToList();
        if (pts.Count == 0) return;
        var box = Box2.FromPoints(pts);
        var pad = Math.Max(Math.Max(box.Width, box.Height) * 0.1, _watched!.PaperMmToMeters(20));
        _canvas.ZoomTo(box.Inflate(pad));
    }

    public void ExportCsv() => Export(csv: true);
    public void ExportSima() => Export(csv: false);

    private void Export(bool csv)
    {
        if (_watched is not { } doc || _canvas == null) return;
        var rows = CoordinateRegistry.Rows(doc);
        var chosen = Selected().Select(i => i.Point).ToHashSet();
        if (chosen.Count > 0) rows = [.. rows.Where(r => chosen.Contains(r.Point))];
        if (rows.Count == 0)
        {
            _canvas.ShowMessage("座標管理: 書き出す座標がありません。");
            return;
        }
        var dlg = new SaveFileDialog
        {
            Filter = csv ? "CSV (*.csv)|*.csv" : "SIMA (*.sim)|*.sim",
            FileName = "座標" + (csv ? ".csv" : ".sim"),
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            if (csv) CoordinateRegistry.ExportCsv(rows, dlg.FileName);
            else CoordinateRegistry.ExportSima(rows, dlg.FileName);
            _canvas.ShowMessage($"座標管理: {rows.Count} 点を {(csv ? "CSV" : "SIMA")} に書き出しました: {dlg.FileName}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "書き出しエラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
