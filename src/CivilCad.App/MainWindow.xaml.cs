using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CivilCad.Alignment;
using HorizontalAlignment = CivilCad.Alignment.HorizontalAlignment;
using CivilCad.App.Alignment;
using CivilCad.App.Drawing;
using CivilCad.App.IO;
using CivilCad.App.Settings;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;
using Microsoft.Win32;

namespace CivilCad.App;

public partial class MainWindow : Window
{
    private readonly List<string> _messages = [];
    private readonly AppSettings _settings = AppSettings.Load();
    private Dictionary<KeyChord, string> _keyMap = [];

    // ---- シート (タブ) ----
    // 以下の _doc などは「表示中のシート」のもの。タブを切り替えると中身が替わる。
    private readonly System.Collections.ObjectModel.ObservableCollection<Sheet> _sheets = [];
    private Sheet _active = null!;
    private CadDocument _doc => _active.Doc;
    private string? _filePath { get => _active.FilePath; set => _active.FilePath = value; }
    private AlignmentInput _alignmentInput { get => _active.AlignmentInput; set => _active.AlignmentInput = value; }
    private HorizontalAlignment? _alignment { get => _active.Alignment; set => _active.Alignment = value; }

    /// <summary>方位記号の真北: 平面直角座標 (図面の x = 東, y = 北) から子午線収差を求める。</summary>
    public static void RegisterTrueNorth() =>
        CadDocument.TrueNorthProvider ??= (zone, p) =>
            PlaneRectangular.Find(zone) is { } z ? PlaneRectangular.TrueNorthRotation(z, p.Y, p.X) : null;

    public MainWindow()
    {
        RegisterTrueNorth();
        InitializeComponent();
        SheetTabs.ItemsSource = _sheets;
        ActivateSheet(AddSheet());
        LayerGrid.CellEditEnding += LayerGrid_CellEditEnding;

        Canvas.ToolChanged += (_, _) => UpdateToolbarHighlight();
        Canvas.PromptChanged += (_, _) => PromptText.Text = $"[{Canvas.Tool.Name}] {Canvas.Tool.Prompt}:";
        Canvas.Message += (_, msg) => Log(msg);
        Canvas.CursorMoved += (_, _) => UpdateCoordinates();
        Canvas.SelectionChanged += (_, _) =>
        {
            SelectionText.Text = Canvas.Selection.Count > 0 ? $"{Canvas.Selection.Count} 個選択中" : "";
            // 文字を選んだら、閉じているプロパティ (右側) を開いて文字の内容・大きさを見せる
            if (_propsDock != null && !_propsDock.IsOpen && Canvas.Selection.Any(x => x is TextEntity)) SetPanelOpen(_propsDock, true);
        };
        Canvas.PreviewMouseDown += (_, _) => CommandBox.Focus();
        Canvas.ContextRequested += (_, _) => ShowCanvasMenu();
        PreviewKeyDown += Window_PreviewKeyDown;
        Loaded += (_, _) =>
        {
            Canvas.SetTool(new SelectTool());
            CommandBox.Focus();
            Log("CivilCad へようこそ。コマンド例: L=線  PL=ポリライン  C=円  A=円弧  T=文字  M=移動  CO=複写  DI=距離  AA=面積  RC=路線計算  ST=測点照会  ZE=全体表示");
            Log("座標入力: x,y (絶対)  @dx,dy (相対)  @距離<角度 (極座標)  sX,Y (測量座標 X=北, Y=東)。図面の座標は x=東, y=北 (m)。");
        };
        ApplyKeyBindings();
        LoadSnapKinds();
        BuildSnapBar();
        LoadBoxSelectMode();
        if (_settings.MessageLogHeight is double h) MessageLog.Height = Math.Clamp(h, MessageLogMin, 1000);

        // レイヤパネルは既定で閉じておき、現在レイヤはステータスバーに出す
        PropsPanel.Attach(Canvas);
        PropsPanel.Committed += (_, _) => FocusCommandBox();
        InputBar.Attach(Canvas);
        InputBar.Committed += (_, _) => FocusCommandBox();

        // パネル: 右 = プロパティ (既定で表示)、左 = レイヤ (既定で閉じる)。どちらもフロート可能
        _propsDock = new DockablePanel(this, PropsHost, PropsPanel, "プロパティ", _settings.PropertiesPanel, open: true,
            new Size(300, 560), () => DockablePanel.OwnerPointToScreen(this, new Point(ActualWidth - 320, 90)));
        _layerDock = new DockablePanel(this, LayerHost, LayerPanel, "レイヤ", _settings.LayerPanel, open: false,
            new Size(360, 520), () => DockablePanel.OwnerPointToScreen(this, new Point(20, 90)));
        PropsPanel.CloseRequested += (_, _) => SetPanelOpen(_propsDock, false);
        PropsPanel.FloatToggleRequested += (_, _) => SetPanelFloating(_propsDock, !_propsDock.IsFloating);
        foreach (var d in new[] { _propsDock, _layerDock })
        {
            d.StateChanged += (_, _) => OnPanelStateChanged();
            d.FloatingKeyDown += Floating_PreviewKeyDown;
        }
        Loaded += (_, _) =>
        {
            // フロートのウィンドウは本体が表示されてから出す
            _propsDock.Update();
            _layerDock.Update();
        };
        Closing += (_, e) =>
        {
            // 保存していないシートがあれば確認する
            var unsaved = _sheets.Where(s => s.IsModified).ToList();
            if (unsaved.Count > 0 && MessageBox.Show(this,
                    $"保存していないシートがあります:\n{string.Join("\n", unsaved.Select(s => "・" + s.FileName))}\n\n保存せずに終了しますか?",
                    "確認", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            {
                e.Cancel = true;
                return;
            }
            _propsDock.ShuttingDown = _layerDock.ShuttingDown = true;
            SaveSettingsQuietly();
        };
        OnPanelStateChanged();
    }

    private readonly DockablePanel _propsDock;
    private readonly DockablePanel _layerDock;

    // ---- コマンド ----

    private void RunCommand(string code)
    {
        var c = code.Trim().ToUpperInvariant();
        switch (c)
        {
            case "L": StartTool(new LineTool(), c); break;
            case "PL": StartTool(new PolylineTool(), c); break;
            case "C": StartTool(new CircleTool(), c); break;
            case "A": StartTool(new ArcTool(), c); break;
            case "T": StartTool(new TextTool(), c); break;
            case "PO": StartTool(new PointTool(), c); break;
            case "PE": StartTool(new PerpendicularTool(), c); break;
            case "FI": StartTool(new HatchTool(fill: true), c); break;
            case "PS": StartTool(new PresetTool(), c); break;
            case "PSR": StartTool(new PresetRegisterTool(AskPresetName), c); break;
            case "NM": StartTool(new NorthManualTool(), c); break;
            case "NA":
                var count = NorthArrows.SetAuto(_doc, Canvas.Selection);
                Log(count > 0 ? $"方位記号 {count} 個を北に合わせました (自動: 移動・回転しても北を指します)。"
                              : "北に合わせる方位記号がありません (手動にした方位記号は無く、方位記号も選択されていません)。");
                break;
            case "J": JoinSelection(); break;
            case "H": StartTool(new HatchTool(), c); break;
            case "PI": ImportPoints(); break;
            case "IM": ImportImage(); break;
            case "PDF": PrintPdf(); break;
            case "M": StartTool(new MoveCopyTool(copy: false), c); break;
            case "SCL": StartTool(new ScaleTool(), c); break;
            case "AL": StartTool(new AlignTool(), c); break;
            case "CO": StartTool(new MoveCopyTool(copy: true), c); break;
            case "RO": StartTool(new RotateTool(), c); break;
            case "O": StartTool(new OffsetTool(), c); break;
            case "TR": StartTool(new TrimTool(), c); break;
            case "EX": StartTool(new ExtendTool(), c); break;
            case "ED": StartTool(new TextEditTool(), c); break;
            case "DI": StartTool(new DistanceTool(), c); break;
            case "AA": StartTool(new AreaTool(), c); break;
            case "E" or "DEL":
                // 先に選択していた図形があればそれを消してから削除モードへ
                DeleteSelection();
                StartTool(new EraseTool(), "E");
                break;
            case "ZE" or "Z": ZoomExtentsWithReport(); break;
            case "LA": SetPanelOpen(_layerDock, !_layerDock.IsOpen); break;
            case "LAF": SetPanelFloating(_layerDock, !_layerDock.IsFloating); break;
            case "PR": SetPanelOpen(_propsDock, !_propsDock.IsOpen); break;
            case "PRF": SetPanelFloating(_propsDock, !_propsDock.IsFloating); break;
            case "U": Undo_Executed(this, null!); break;
            case "REDO": Redo_Executed(this, null!); break;
            case "SNAP":
                SnapToggle.IsChecked = !SnapToggle.IsChecked;
                Toggle_Click(this, new RoutedEventArgs());
                Log($"スナップ {(Canvas.SnapEnabled ? "オン" : "オフ")}");
                break;
            case "ORTHO":
                OrthoToggle.IsChecked = !OrthoToggle.IsChecked;
                Toggle_Click(this, new RoutedEventArgs());
                Log($"直交モード {(Canvas.OrthoEnabled ? "オン" : "オフ")}");
                break;
            case "RC": OpenAlignmentDialog(); break;
            case "RX":
                if (LandXmlImport.Run(this, _alignmentInput.TextHeightMm) is { } loaded) OpenAlignmentDialog(loaded);
                break;
            case "FR": OpenFrameDialog(); break;
            case "SC": OpenScaleDialog(); break;
            case "ZN": OpenZoneDialog(); break;
            case "CS":
                if (_alignment != null)
                {
                    // 図面が動くので、測点照会に使う路線は計算し直してもらう
                    _alignment = null;
                    Log("座標設定後は、測点照会の前に路線計算 (RC) をやり直してください。");
                }
                StartTool(new CoordinateSetTool(), c);
                break;
            case "ST":
                if (_alignment == null) Log("先に 路線計算 (RC) で路線を作図してください。");
                else StartTool(new StationQueryTool(_alignment, _alignmentInput.Pitch), c);
                break;
            default: Log($"不明なコマンドです: {code}"); break;
        }
    }

    private void StartTool(CadTool tool, string code)
    {
        if (tool.RequiresSelection && Canvas.Selection.Count == 0)
        {
            Log($"{tool.Name}: 先に図形を選択してください。");
            return;
        }
        tool.CommandCode = code;
        Canvas.SetTool(tool);
        CommandBox.Focus();
    }

    /// <summary>結合: 選択中の線をつなぎ直す (同じ直線上・同じ円上のものはまとめ、端点がつながるものは 1 本のポリラインに)。</summary>
    private void JoinSelection() => Log(JoinCommand.Run(Canvas));

    // ---- 右クリックメニュー ----

    /// <summary>
    /// 作図領域の右クリックメニュー。コマンド中は 一つ戻す・最初からやり直す・確定・終了、
    /// 選択中は 移動・複写・削除 などを出す。
    /// </summary>
    private void ShowCanvasMenu()
    {
        var tool = Canvas.Tool;
        var menu = new ContextMenu();
        MenuItem Item(string header, Action run, bool enabled = true, string? gesture = null)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled, InputGestureText = gesture ?? "" };
            item.Click += (_, _) =>
            {
                run();
                Canvas.RedrawOverlay();
                FocusCommandBox();
            };
            menu.Items.Add(item);
            return item;
        }

        if (tool is SelectTool)
        {
            var hasSel = Canvas.Selection.Count > 0;
            Item("移動", () => RunCommand("M"), hasSel, "M");
            Item("複写", () => RunCommand("CO"), hasSel, "CO");
            Item("拡大縮小", () => RunCommand("SCL"), hasSel, "SCL");
            Item("2 点指示 (位置合わせ)", () => RunCommand("AL"), hasSel, "AL");
            Item("削除", DeleteSelection, hasSel, "Delete");
            Item("選択を解除", Canvas.ClearSelection, hasSel, "Esc");
            menu.Items.Add(new Separator());
            Item("元に戻す", () => Undo_Executed(this, null!), _doc.History.CanUndo, "Ctrl+Z");
            Item("やり直し", () => Redo_Executed(this, null!), _doc.History.CanRedo, "Ctrl+Y");
            menu.Items.Add(new Separator());
            Item("全体表示", ZoomExtentsWithReport, true, "ZE");
        }
        else
        {
            menu.Items.Add(new MenuItem { Header = $"[{tool.Name}]", IsEnabled = false });
            Item("一つ戻す", tool.StepBack, tool.CanStepBack, "Backspace");
            Item("最初からやり直す", RestartTool);
            if (tool.ConfirmLabel is { } confirm) Item(confirm, tool.OnEnter);
            foreach (var (label, run) in tool.MenuItems) Item(label, run);
            menu.Items.Add(new Separator());
            Item("コマンドを終了", () => Canvas.SetTool(new SelectTool()), true, "Esc");
        }
        menu.PlacementTarget = Canvas;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    /// <summary>実行中のコマンドを最初からやり直す (入力途中の点などを捨てる。作った図形はそのまま)。</summary>
    private void RestartTool()
    {
        var tool = Canvas.Tool;
        if (tool.CommandCode is not { } code) return;
        if (code == "E") StartTool(new EraseTool(), "E"); // 削除モードは選択図形を消さずにやり直す
        else RunCommand(code);
        Log($"{tool.Name}: 最初からやり直します。");
    }

    private static readonly Brush ActiveToolBrush =new SolidColorBrush(Color.FromRgb(0xCC, 0xE4, 0xFF));

    /// <summary>実行中のコマンドのボタンを色付けして、今どのモードかを分かるようにする。</summary>
    private void UpdateToolbarHighlight()
    {
        foreach (var b in ToolbarButtons())
        {
            if (b.Tag is string code && (code == Canvas.Tool.CommandCode || IsPanelOpen(code))) b.Background = ActiveToolBrush;
            else b.ClearValue(BackgroundProperty);
        }
    }
    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string code }) RunCommand(code);
    }

    /// <summary>コマンド欄の入力: ツールへの文字入力 → 座標 → コマンド名 の順に解釈。</summary>
    private void SubmitCommandText()
    {
        var text = CommandBox.Text;
        CommandBox.Clear();
        var tool = Canvas.Tool;
        if (text.Trim().Length == 0)
        {
            tool.OnEnter();
        }
        else if (tool.OnText(text))
        {
        }
        else if (CoordinateInput.TryParse(text, tool.BasePoint, out var p))
        {
            tool.OnPoint(p);
        }
        else if (tool is SelectTool)
        {
            RunCommand(text);
        }
        else
        {
            Log($"入力を解釈できません: {text}");
        }
        Canvas.RedrawOverlay();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+Tab / Ctrl+Shift+Tab でシート切り替え
        if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SwitchSheet(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            e.Handled = true;
            return;
        }

        // 作図中: Tab で作図入力バーの数値の欄へ、Shift+Tab で長さの単位 (mm → cm → m)
        // (入力バーの欄の中での Tab / Shift+Tab は入力バー自身が処理する)
        if (e.Key == Key.Tab && !InputBar.IsKeyboardFocusWithin
            && (CommandBox.IsKeyboardFocused || Canvas.IsKeyboardFocusWithin || Keyboard.FocusedElement is null || Keyboard.FocusedElement == this))
        {
            if (Keyboard.Modifiers == ModifierKeys.None && InputBar.FocusFirstField())
            {
                e.Handled = true;
                return;
            }
            if (Keyboard.Modifiers == ModifierKeys.Shift && InputBar.HasLengthField)
            {
                InputBar.CycleUnit();
                e.Handled = true;
                return;
            }
        }

        // 設定で割り当てたショートカット (F3 スナップ / F8 直交 もここ)
        if (TryRunShortcut(e))
        {
            e.Handled = true;
            return;
        }

        // Esc でコマンドをキャンセル: コマンド欄以外 (作図領域・ツールバー等) にフォーカスがあっても効くようにする。
        // ただし入力欄・一覧 (レイヤ名の編集、入力バーの数値など) の中の Esc はそちらに任せる
        if (e.Key == Key.Escape && !CommandBox.IsKeyboardFocused &&
            Keyboard.FocusedElement is not (System.Windows.Controls.Primitives.TextBoxBase or ComboBox or DataGridCell or ComboBoxItem))
        {
            Canvas.Tool.OnCancel();
            Canvas.RedrawOverlay();
            FocusCommandBox();
            e.Handled = true;
            return;
        }

        // 以下はコマンド欄にフォーカスがあるときだけ (レイヤ名の編集などを邪魔しない)
        if (!CommandBox.IsKeyboardFocused) return;

        // コマンド欄が空なら Backspace で「一つ戻す」(右クリックメニューと同じ)
        if (e.Key == Key.Back && Keyboard.Modifiers == ModifierKeys.None && CommandBox.Text.Length == 0 && Canvas.Tool.CanStepBack)
        {
            Canvas.Tool.StepBack();
            Canvas.RedrawOverlay();
            e.Handled = true;
            return;
        }

        // コマンド欄が空なら Ctrl+Z / Y / A は図面に対する操作にする (TextBox 自身の処理より優先)
        if (Keyboard.Modifiers == ModifierKeys.Control && CommandBox.Text.Length == 0)
        {
            switch (e.Key)
            {
                case Key.Z: Undo_Executed(this, null!); e.Handled = true; return;
                case Key.Y: Redo_Executed(this, null!); e.Handled = true; return;
                case Key.A: Canvas.SetSelection(_doc.Entities.Where(_doc.IsEditable)); e.Handled = true; return;
            }
        }

        switch (e.Key)
        {
            case Key.Enter:
                SubmitCommandText();
                e.Handled = true;
                break;
            case Key.Escape:
                if (CommandBox.Text.Length > 0) CommandBox.Clear();
                else Canvas.Tool.OnCancel();
                Canvas.RedrawOverlay();
                e.Handled = true;
                break;
            case Key.Delete when CommandBox.Text.Length == 0:
                DeleteSelection();
                e.Handled = true;
                break;
        }
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        Canvas.SnapEnabled = SnapToggle.IsChecked == true;
        Canvas.OrthoEnabled = OrthoToggle.IsChecked == true;
        CommandBox.Focus();
    }

    private void DeleteSelection()
    {
        if (Canvas.Selection.Count == 0) return;
        _doc.History.Execute(new RemoveEntitiesCommand(Canvas.Selection.ToList()));
        Canvas.ClearSelection();
    }

    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelection();

    // ---- ファイル・編集 ----

    private void New_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        ActivateSheet(AddSheet());
        Log($"新しいシート「{_active.FileName}」を作りました。");
    }

    private void Open_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = DrawingFiles.OpenFilter,
        };
        if (dlg.ShowDialog(this) != true) return;

        // 同じファイルが開いていればそのタブへ
        if (_sheets.FirstOrDefault(s => string.Equals(s.FilePath, dlg.FileName, StringComparison.OrdinalIgnoreCase)) is { } opened)
        {
            ActivateSheet(opened);
            Log($"既に開いています: {dlg.FileName}");
            return;
        }

        // 空のシートならそこに、そうでなければ新しいタブに読み込む
        var previous = _active;
        var sheetScale = _doc.ScaleDenominator;
        var created = !_active.IsBlank;
        ActivateSheet(created ? AddSheet() : _active);
        try
        {
            var loaded = DrawingFiles.Open(_doc, dlg.FileName);
            var note = loaded.Note;
            // 図面の縮尺 (と DXF / DWG の座標の単位) を選んでもらう
            var extents = _doc.Entities.Where(_doc.IsVisible).Aggregate(Box2.Empty, (acc, x) => acc.Union(x.Bounds));
            if (ImportScaleWindow.Ask(this, dlg.FileName, loaded, extents.IsEmpty ? null : extents, sheetScale) is not var (scale, unit))
            {
                if (created) RemoveSheet(_active, previous);
                else _doc.Clear();
                Log($"読み込みをやめました: {dlg.FileName}");
                return;
            }
            var (factor, sheet) = DrawingFiles.ApplyImportScale(_doc, loaded, scale, unit);
            note = $"  縮尺 {_doc.ScaleText}" + (Math.Abs(factor - 1) > 1e-12 ? $" (ファイルの値を {factor:0.######} 倍)" : "") + " で読み込みました。" + note;
            if (_doc.PlaneZone == null && _settings.DefaultPlaneZone is int defZone)
            {
                _doc.PlaneZone = defZone;
                note += $"  座標系は既定の {PlaneRectangular.Find(defZone)!.Name}にしました。";
            }
            _filePath = dlg.FileName;
            _alignment = null;
            _active.MarkSaved();
            Canvas.SetTool(new SelectTool());
            Log($"読み込みました: {dlg.FileName} ({_doc.Entities.Count} 図形, {_doc.Layers.Count} レイヤ)" + note);
            ZoomAfterOpen(sheet);
            OnActiveSheetInfoChanged();
        }
        catch (Exception ex)
        {
            if (created) RemoveSheet(_active, previous);
            MessageBox.Show(this, ex.Message, "読み込みエラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        // 開いたファイルと同じ形式を初期値にする
        var dlg = new SaveFileDialog
        {
            Filter = DrawingFiles.SaveFilter,
            FilterIndex = DrawingFiles.SaveFilterIndex(_filePath),
            FileName = _filePath != null ? Path.GetFileName(_filePath) : _active.FileName + ".dxf",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            Log(DrawingFiles.Save(_doc, dlg.FileName));
            if (DrawingFiles.ImageNote(_doc, dlg.FileName) is { } imageNote) Log(imageNote);
            _filePath = dlg.FileName;
            _active.MarkSaved();
            UpdateTitle();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>PDF に印刷: 用紙・縮尺などを選び、保存先を決めて書き出す。</summary>
    /// <summary>PDF に印刷する。window は図面上で指定した印刷範囲 (範囲を指定したあとに呼び直すとき)。</summary>
    private void PrintPdf(Box2? window = null)
    {
        var targets = new PdfTargets(Canvas.VisibleWorldBox, [.. Canvas.Selection], window);
        if (PdfWindow.Ask(this, _doc, targets) is not var (options, openAfter, pickWindow)) return;
        if (pickWindow)
        {
            // 図面上で範囲を指定してもらい、指定したら設定の窓をもう一度出す
            StartTool(new PdfRangeTool(box => Dispatcher.BeginInvoke(() => PrintPdf(box))), "PDF");
            return;
        }
        var dlg = new SaveFileDialog
        {
            Filter = "PDF ファイル (*.pdf)|*.pdf",
            FileName = (_filePath != null ? System.IO.Path.GetFileNameWithoutExtension(_filePath) : _active.FileName) + ".pdf",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var l = PdfExport.Export(_doc, dlg.FileName, options);
            Log($"PDF に印刷しました: {dlg.FileName}  (用紙 {l.PaperName} {(l.Width >= l.Height ? "横" : "縦")} {l.Width:0}×{l.Height:0}mm, 縮尺 1:{l.MetersPerMm * 1000:0.##}" +
                (options.Monochrome ? ", 白黒" : "") + (options.FitToPaper ? ", 用紙に合わせる" : "") +
                options.Range switch { PdfRange.Screen => ", 今の画面", PdfRange.Selection => $", 選択した図形 {options.Only?.Count} 個", PdfRange.Window => ", 指定した範囲", _ => "" } + ")");
            if (openAfter) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, ex.Message, "印刷エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---- シートタブ ----

    /// <summary>新しいシートを作ってタブに加える (まだ表示は切り替えない)。</summary>
    private Sheet AddSheet()
    {
        var sheet = new Sheet();
        sheet.Doc.PlaneZone = _settings.DefaultPlaneZone;
        sheet.Doc.History.Clear();
        sheet.MarkSaved();
        _sheets.Add(sheet);
        return sheet;
    }

    /// <summary>表示するシートを切り替える。表示位置はシートごとに覚えておく。</summary>
    private void ActivateSheet(Sheet sheet)
    {
        if (_active == sheet) return;
        if (_active != null)
        {
            _active.ViewCenter = Canvas.ViewCenter;
            _active.ViewScale = Canvas.Scale;
            _active.IsActive = false;
            _active.Doc.PropertyChanged -= ActiveDoc_PropertyChanged;
            _active.Doc.Changed -= ActiveDoc_Changed;
        }

        _active = sheet;
        sheet.IsActive = true;
        sheet.Doc.PropertyChanged += ActiveDoc_PropertyChanged;
        sheet.Doc.Changed += ActiveDoc_Changed;

        Canvas.Document = sheet.Doc;
        Canvas.SetTool(new SelectTool());
        LayerGrid.ItemsSource = sheet.Doc.Layers;
        OnlyCurrentCheck.IsChecked = sheet.Doc.OnlyCurrentLayerEditable;
        if (sheet.ViewCenter is Vec2 c && sheet.ViewScale is double s) Canvas.SetView(c, s);
        else if (IsLoaded) Canvas.ZoomExtents();

        OnActiveSheetInfoChanged();
        UpdateLayerButton();
        if (IsLoaded) Canvas.Dispatcher.BeginInvoke(() => { if (SheetTabs.ItemContainerGenerator.ContainerFromItem(sheet) is FrameworkElement el) el.BringIntoView(); });
    }

    private void ActiveDoc_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CadDocument.ScaleDenominator) or nameof(CadDocument.PlaneZone)) OnActiveSheetInfoChanged();
    }

    private void ActiveDoc_Changed(object? sender, EventArgs e) => UpdateLayerButton();

    /// <summary>表示中のシートの縮尺・座標系・名前を、タブの右端・ステータスバー・タイトルに反映する。</summary>
    private void OnActiveSheetInfoChanged()
    {
        UpdateScaleText();
        UpdateZoneButton();
        TabScaleButton.Content = _active.ScaleText;
        TabZoneButton.Content = _active.ZoneText;
        UpdateTitle();
    }

    /// <summary>シートを閉じる。未保存なら確認する。最後の 1 枚を閉じたら空のシートを作る。</summary>
    private void CloseSheet(Sheet sheet)
    {
        if (sheet.IsModified && MessageBox.Show(this, $"「{sheet.FileName}」は保存していません。閉じますか?", "確認",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        var index = _sheets.IndexOf(sheet);
        var next = _sheets.Count > 1 ? _sheets[index == _sheets.Count - 1 ? index - 1 : index + 1] : null;
        RemoveSheet(sheet, next);
    }

    private void RemoveSheet(Sheet sheet, Sheet? next)
    {
        if (_active == sheet) ActivateSheet(next ?? AddSheet());
        _sheets.Remove(sheet);
        FocusCommandBox();
    }

    private void SwitchSheet(int step)
    {
        if (_sheets.Count < 2) return;
        var i = (_sheets.IndexOf(_active) + step + _sheets.Count) % _sheets.Count;
        ActivateSheet(_sheets[i]);
    }

    private void SheetTab_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Sheet s } && e.OriginalSource is not Button)
        {
            ActivateSheet(s);
            FocusCommandBox();
        }
    }

    /// <summary>中ボタンクリックでも閉じる。</summary>
    private void SheetTab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && sender is FrameworkElement { DataContext: Sheet s })
        {
            CloseSheet(s);
            e.Handled = true;
        }
    }

    private void SheetTabClose_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Sheet s }) CloseSheet(s);
    }

    private void Undo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        Canvas.SetTool(new SelectTool());
        var name = _doc.History.Undo();
        Log(name != null ? $"元に戻す: {name}" : "元に戻す操作はありません。");
    }

    private void Redo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        Canvas.SetTool(new SelectTool());
        var name = _doc.History.Redo();
        Log(name != null ? $"やり直し: {name}" : "やり直す操作はありません。");
    }

    private void SelectAll_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!CommandBox.IsKeyboardFocused && Keyboard.FocusedElement is TextBox) return; // 入力欄の全選択を優先
        Canvas.SetSelection(_doc.Entities.Where(_doc.IsEditable));
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    // ---- 設定・ショートカット ----

    /// <summary>割り当てキーならコマンドを実行する。</summary>
    private bool TryRunShortcut(KeyEventArgs e)
    {
        if (KeyChord.FromEvent(e) is not KeyChord chord || !_keyMap.TryGetValue(chord, out var code)) return false;

        if (chord.IsCharacterKey)
        {
            // 英字キー単独は「文字を入力していないとき」だけショートカットにする
            var typingInCommand = CommandBox.IsKeyboardFocused && (CommandBox.Text.Length > 0 || Canvas.Tool.WantsTextInput);
            var typingElsewhere = !CommandBox.IsKeyboardFocused && Keyboard.FocusedElement is TextBox;
            if (typingInCommand || typingElsewhere) return false;
        }
        RunCommand(code);
        return true;
    }

    private void ApplyKeyBindings()
    {
        _keyMap = _settings.BuildKeyMap();

        // メニューの右側とツールバーのツールチップに割り当てキーを表示
        foreach (var item in AllMenuItems(MainMenu.Items))
        {
            if (item.Tag is string code) item.InputGestureText = _settings.KeyFor(code) ?? "";
        }
        foreach (var b in ToolbarButtons())
        {
            if (b.Tag is not string code || CommandRegistry.Find(code) is not { } info) continue;
            var key = _settings.KeyFor(code);
            b.ToolTip = $"{info.Name}  (入力: {code}{(key != null ? $" / キー: {key}" : "")})" + (info.Note != null ? $"\n{info.Note}" : "");
        }
        SnapToggle.Content = $"スナップ{(_settings.KeyFor("SNAP") is { } s ? $" [{s}]" : "")}";
        OrthoToggle.Content = $"直交{(_settings.KeyFor("ORTHO") is { } o ? $" [{o}]" : "")}";
    }

    /// <summary>ツールバー (タブの中も含む) のボタン。</summary>
    private IEnumerable<Button> ToolbarButtons() => LogicalButtons(RibbonArea);

    private static IEnumerable<Button> LogicalButtons(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Button b) yield return b;
            foreach (var inner in LogicalButtons(child)) yield return inner;
        }
    }

    private static IEnumerable<MenuItem> AllMenuItems(ItemCollection items)
    {
        foreach (var item in items.OfType<MenuItem>())
        {
            yield return item;
            foreach (var child in AllMenuItems(item.Items)) yield return child;
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(_settings) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result == null) return;
        _settings.KeyBindings = dlg.Result;
        try
        {
            _settings.Save();
            Log($"キー割り当てを保存しました ({AppSettings.DefaultPath})");
        }
        catch (Exception ex)
        {
            Log($"設定を保存できませんでした: {ex.Message}");
        }
        ApplyKeyBindings();
        CommandBox.Focus();
    }

    // ---- 縮尺 ----

    private void OpenScaleDialog()
    {
        var dlg = new ScaleWindow(_doc.ScaleDenominator) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result == _doc.ScaleDenominator) return;
        ChangeScale(dlg.Result, dlg.Mode);
        CommandBox.Focus();
    }

    /// <summary>シートの縮尺を変える (元に戻すで一度に戻る)。</summary>
    private void ChangeScale(double scale, ScaleChangeMode mode)
    {
        var k = scale / _doc.ScaleDenominator;
        _doc.History.Execute(PaperRescale.Command(_doc, scale, mode, Canvas.ScaleView)); // 図寸固定は表示も同じだけ広げて見た目を変えない
        if (mode == ScaleChangeMode.KeepPaper)
        {
            Log($"シートの縮尺を {_doc.ScaleText} にしました (図面をそのまま: 図面全体を {k:0.####} 倍。計測の値も {_doc.ScaleText} の値になります)。");
        }
        else
        {
            Log($"シートの縮尺を {_doc.ScaleText} にしました (実寸を保つ: 線などの長さはそのまま、図枠を新しい縮尺の大きさにしました)。");
        }
    }

    private void UpdateScaleText() => ScaleButton.Content = $"シートの縮尺 {_doc.ScaleText}";

    private void ScaleButton_Click(object sender, RoutedEventArgs e) => RunCommand("SC");

    // ---- パネル (レイヤ / プロパティ): ドッキング / フロート ----

    private void SetPanelOpen(DockablePanel panel, bool open)
    {
        panel.SetOpen(open);
        FocusCommandBox();
    }

    private void SetPanelFloating(DockablePanel panel, bool floating)
    {
        panel.SetFloating(floating);
        SaveSettingsQuietly();
        var name = panel == _layerDock ? "レイヤパネル" : "プロパティ";
        var side = panel == _layerDock ? "左側" : "右側";
        Log(floating ? $"{name}を別ウィンドウにしました (⇲ で{side}に戻せます)。" : $"{name}を{side}に戻しました。");
    }

    private bool IsPanelOpen(string code) => code switch
    {
        "LA" => _layerDock.IsOpen,
        "LAF" => _layerDock.IsFloating,
        "PR" => _propsDock.IsOpen,
        "PRF" => _propsDock.IsFloating,
        _ => false,
    };

    private void OnPanelStateChanged()
    {
        PropsPanel.IsFloating = _propsDock.IsFloating;
        // レイヤパネルは左に付いているときは幅 320 固定、フロート中はウィンドウに合わせる
        LayerPanel.Width = _layerDock.IsFloating ? double.NaN : 320;
        LayerFloatButton.Content = _layerDock.IsFloating ? "⇲" : "⧉";
        LayerFloatButton.ToolTip = _layerDock.IsFloating ? "ドッキング (メイン画面の左側に戻す)" : "フロート (別ウィンドウに切り離す)";
        UpdatePanelIndicators();
    }

    /// <summary>メニューのチェックとツールバーの色で、パネルが開いているかを示す。</summary>
    private void UpdatePanelIndicators()
    {
        foreach (var item in AllMenuItems(MainMenu.Items))
            if (item.Tag is "LA" or "LAF" or "PR" or "PRF") item.IsChecked = IsPanelOpen((string)item.Tag);
        UpdateToolbarHighlight();
    }

    private void LayerFloat_Click(object sender, RoutedEventArgs e) => RunCommand("LAF");

    private void LayerHeader_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && e.OriginalSource is not Button) RunCommand("LAF");
    }

    /// <summary>フロートのウィンドウでも元に戻す・やり直しは効くようにする。</summary>
    private void Floating_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || Keyboard.FocusedElement is TextBox) return;
        if (e.Key == Key.Z)
        {
            Undo_Executed(this, null!);
            e.Handled = true;
        }
        else if (e.Key == Key.Y)
        {
            Redo_Executed(this, null!);
            e.Handled = true;
        }
    }

    private void SaveSettingsQuietly()
    {
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 設定が保存できなくても作業は続けられる
        }
    }

    /// <summary>コマンド欄に入力を戻す (フロートのプロパティで確定した後も本体に戻る)。</summary>
    private void FocusCommandBox()
    {
        if (!IsActive && IsVisible) Activate();
        CommandBox.Focus();
    }

    private void UpdateLayerButton() => LayerButton.Content = $"レイヤ: {_doc.CurrentLayer.Name}";

    private void LayerButton_Click(object sender, RoutedEventArgs e) => RunCommand("LA");

    // ---- 図枠 ----

    private void OpenFrameDialog()
    {
        // 縮尺は図面の縮尺を初期値にする
        var defaults = _settings.FrameDefaults.Clone();
        defaults.ScaleDenominator = _doc.ScaleDenominator;
        var dlg = new FrameWindow(defaults, DrawingFrame.DrawingExtents(_doc)) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is not { } options) return;
        if (options.ScaleDenominator != _doc.ScaleDenominator)
        {
            ChangeScale(options.ScaleDenominator, ScaleChangeMode.KeepReal); // 図形が収まる縮尺を選ぶ窓なので、図形の実寸は変えない
        }
        _settings.FrameDefaults = options;
        try
        {
            _settings.Save();
        }
        catch (Exception ex)
        {
            Log($"図枠の入力内容を保存できませんでした: {ex.Message}");
        }
        StartTool(new FrameTool(options.Clone()), "FR");
    }

    // ---- 座標読込 ----

    /// <summary>SIMA (.sim) / CSV の座標を読み、点の印と点名を図面に置く (元に戻すで一度に消せる)。</summary>
    private void ImportPoints()
    {
        var open = new OpenFileDialog
        {
            Title = "座標ファイルを開く",
            Filter = "座標ファイル (*.sim;*.csv;*.txt)|*.sim;*.csv;*.txt|SIMA (*.sim)|*.sim|CSV (*.csv;*.txt)|*.csv;*.txt|すべてのファイル (*.*)|*.*",
        };
        if (open.ShowDialog(this) != true) return;

        PointFile file;
        try
        {
            file = PointImport.Load(open.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"{Path.GetFileName(open.FileName)} を読み込めませんでした。\n{ex.Message}", "座標読込", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (file.Points.Count == 0)
        {
            MessageBox.Show(this, $"{Path.GetFileName(open.FileName)} に座標が見つかりませんでした。\n" +
                                  "SIMA は A01,点番号,点名,X,Y,H の行、CSV は 点名,X,Y[,Z] などの行を読みます。",
                "座標読込", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new PointImportWindow(open.FileName, file, _settings.PointImport, _doc.ScaleText) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is not { } options) return;
        _settings.PointImport = options;
        SaveSettingsQuietly();

        var entities = options.Build(_doc, file);
        _doc.History.Execute(new AddEntitiesCommand(entities, $"座標読込 ({file.Points.Count} 点)"));
        var box = entities.Aggregate(Box2.Empty, (acc, e) => acc.Union(e.Bounds));
        if (!Canvas.VisibleWorldBox.Contains(box)) // 読み込んだ点が画面に収まっていなければ表示を合わせる
            Canvas.ZoomTo(box.Inflate(Math.Max(Math.Max(box.Width, box.Height) * 0.05, _doc.PaperMmToMeters(10))));
        Log($"座標読込: {Path.GetFileName(open.FileName)} から {file.Points.Count} 点" +
            (file.Lots.Count > 0 && options.DrawLots ? $"、画地 {file.Lots.Count} 区画" : "") +
            $" をレイヤ「{entities[0].Layer.Name}」に配置しました。" +
            (file.SkippedLines > 0 ? $" (読めなかった行 {file.SkippedLines})" : ""));
    }

    private PresetRegisterInput? AskPresetName(string defaultName)
    {
        var dlg = new PresetNameWindow(defaultName) { Owner = this };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }

    // ---- 画像読込 ----

    /// <summary>
    /// 画像 (オルソ画像など) を読み込む。ここでは画像の大きさと位置情報だけを読み、画素は表示するときに裏で読む
    /// (大きな画像でも画面が止まらないように)。ワールドファイル / GeoTIFF があればその位置に置き、無ければ位置を指定する。
    /// </summary>
    private void ImportImage()
    {
        var open = new OpenFileDialog
        {
            Title = "画像・PDF ファイルを開く",
            Filter = "画像・PDF (*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp;*.gif;*.pdf)|*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp;*.gif;*.pdf|" +
                     "画像ファイル (*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp;*.gif|PDF (*.pdf)|*.pdf|すべてのファイル (*.*)|*.*",
        };
        if (open.ShowDialog(this) != true) return;
        var path = open.FileName;
        if (PdfRenderer.IsPdf(path))
        {
            ImportPdf(path);
            return;
        }

        ImageInfo info;
        try
        {
            info = ImageCache.ReadInfo(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{Path.GetFileName(path)} を画像として読み込めませんでした。\n{ex.Message}", "画像読込", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ImageCache.Forget(path); // 同じ名前で中身が替わっている場合に備えて読み直す

        if (info.Placement is not { } pl)
        {
            Log($"画像「{Path.GetFileName(path)}」({info.PixelWidth}×{info.PixelHeight}) には位置情報 (ワールドファイル) がありません。配置する位置を指定してください。" +
                "置いた後に 2 点指示 (AL) で、画像の上の 2 点を図面の 2 点に合わせられます。");
            StartTool(new ImagePlaceTool(path, info.PixelWidth, info.PixelHeight), "IM");
            return;
        }
        var img = new ImageEntity(_doc.CurrentLayer, path, info.PixelWidth, info.PixelHeight, pl.Origin, pl.U, pl.V);
        _doc.History.Execute(new AddEntitiesCommand([img], "画像読込"));
        if (!Canvas.VisibleWorldBox.Contains(img.Bounds))
            Canvas.ZoomTo(img.Bounds.Inflate(Math.Max(img.Bounds.Width, img.Bounds.Height) * 0.05));
        Log($"画像読込: {Path.GetFileName(path)} ({info.PixelWidth}×{info.PixelHeight}) を {info.PlacementSource} の位置に配置しました " +
            $"(幅 {img.Width:0.###}m × 高さ {img.Height:0.###}m、1 画素 {img.Width / info.PixelWidth:0.####}m)。");
    }

    /// <summary>PDF のページを画像として置く (ページを選び、位置と幅を指定。Enter で原寸)。</summary>
    private void ImportPdf(string path)
    {
        List<PdfPageInfo> pages;
        try
        {
            pages = PdfRenderer.ReadPages(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{Path.GetFileName(path)} を開けませんでした (パスワード付きの PDF などは読めません)。\n{ex.Message}",
                "PDF 読込", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (pages.Count == 0)
        {
            Log($"PDF 読込: {Path.GetFileName(path)} にページがありません。");
            return;
        }
        var page = pages[0];
        if (pages.Count > 1)
        {
            var dlg = new PdfPageWindow(Path.GetFileName(path), pages) { Owner = this };
            if (dlg.ShowDialog() != true || dlg.Result is not { } chosen) return;
            page = chosen;
        }
        ImageCache.Forget(path);
        var (w, h) = PdfRenderer.PixelSize(page);
        Log($"PDF「{Path.GetFileName(path)}」{page.Label} を読み込みます。左下の位置と幅を指定してください (Enter で原寸 = {_doc.ScaleText} で用紙の大きさ)。" +
            "置いた後に 2 点指示 (AL) で、PDF の上の 2 点を図面の 2 点に合わせられます。");
        StartTool(new ImagePlaceTool(path, w, h, page.Index, page.WidthMm), "IM");
    }

    // ---- 路線 ----

    /// <summary>路線計算ダイアログ。input を渡すとその内容 (XML から読んだ路線など) で開く。</summary>
    private void OpenAlignmentDialog(AlignmentInput? input = null)
    {
        var dlg = new AlignmentWindow(input ?? _alignmentInput) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.ResultInput == null || dlg.ResultAlignment == null) return;
        _alignmentInput = dlg.ResultInput;
        _alignment = dlg.ResultAlignment;
        _doc.History.Execute(AlignmentDrawer.CreateDrawCommand(_doc, _alignment, _alignmentInput));
        Canvas.ZoomExtents();
        Log($"路線「{_alignmentInput.Name}」を作図しました。延長 {_alignment.Length:F3} m " +
            $"({Station.Format(_alignment.StartStation, _alignmentInput.Pitch)} 〜 {Station.Format(_alignment.EndStation, _alignmentInput.Pitch)})");
    }

    // ---- レイヤ ----

    private Layer? SelectedLayer => LayerGrid.SelectedItem as Layer;

    private void LayerAdd_Click(object sender, RoutedEventArgs e)
    {
        var palette = RgbColor.Palette;
        var layer = _doc.GetOrCreateLayer(_doc.NextLayerName(), palette[_doc.Layers.Count % palette.Count]);
        LayerGrid.SelectedItem = layer;
        LayerGrid.ScrollIntoView(layer);
    }

    private void LayerRemove_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLayer is not { } layer) return;
        if (!_doc.CanRemoveLayer(layer))
        {
            Log($"レイヤ「{layer.Name}」は削除できません (\"0\"・現在レイヤ・図形があるレイヤは削除不可)。");
            return;
        }
        _doc.Layers.Remove(layer);
    }

    private void LayerSetCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLayer is { } layer) SetCurrentLayer(layer);
    }

    private void LayerGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 名前セル以外のダブルクリックで現在レイヤにする
        if (e.OriginalSource is DependencyObject d && FindParent<DataGridCell>(d) is { Column: DataGridTextColumn }) return;
        if (SelectedLayer is { } layer) SetCurrentLayer(layer);
    }

    private void SetCurrentLayer(Layer layer)
    {
        _doc.CurrentLayer = layer;
        Log($"現在レイヤ: {layer.Name}");
    }

    private void LayerIsolate_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLayer is not { } target) return;
        SetCurrentLayer(target);
        foreach (var l in _doc.Layers) l.IsVisible = l == target;
    }

    private void LayerShowAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var l in _doc.Layers) l.IsVisible = true;
    }

    private void LayerSelectEntities_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLayer is not { } layer) return;
        var entities = _doc.Entities.Where(x => x.Layer == layer && _doc.IsEditable(x)).ToList();
        Canvas.SetSelection(entities);
        Log($"レイヤ「{layer.Name}」の図形 {entities.Count} 個を選択しました。");
    }

    private void LayerMoveEntities_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLayer is not { } layer) return;
        if (Canvas.Selection.Count == 0)
        {
            Log("移す図形を先に選択してください。");
            return;
        }
        var count = Canvas.Selection.Count;
        _doc.History.Execute(new ChangeLayerCommand(Canvas.Selection.ToList(), layer));
        Log($"{count} 個の図形をレイヤ「{layer.Name}」へ移しました。");
    }

    private void OnlyCurrent_Click(object sender, RoutedEventArgs e) =>
        _doc.OnlyCurrentLayerEditable = OnlyCurrentCheck.IsChecked == true;

    private void LayerGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not Layer layer || e.EditingElement is not TextBox box) return;
        var name = box.Text.Trim();
        if (name == layer.Name) return;
        var error = layer.Name == "0" ? "レイヤ \"0\" の名前は変更できません。"
            : name.Length == 0 ? "レイヤ名が空です。"
            : _doc.Layers.Any(l => l != layer && string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) ? $"レイヤ「{name}」は既にあります。"
            : null;
        if (error != null)
        {
            Log(error);
            box.Text = layer.Name;
        }
        else
        {
            box.Text = name; // 前後の空白を除いた名前にする
        }
    }

    // ---- スナップの種類 ----

    private void LoadSnapKinds()
    {
        if (_settings.SnapKinds == null) return; // 既定のまま
        Canvas.SnapKinds.Clear();
        foreach (var name in _settings.SnapKinds)
            if (Enum.TryParse<SnapKind>(name, out var k)) Canvas.SnapKinds.Add(k);
        // 頂点が端点から分かれる前の設定: 端点がオンなら頂点もオン (今までどおりポリラインの頂点に吸着する)
        if (_settings.SnapKindsVersion < 1 && Canvas.SnapKinds.Contains(SnapKind.Endpoint))
            Canvas.SnapKinds.Add(SnapKind.Vertex);
    }

    private void SaveSnapKinds()
    {
        _settings.SnapKinds = [.. Canvas.SnapKinds.OrderBy(k => k).Select(k => k.ToString())];
        _settings.SnapKindsVersion = 1;
        SaveSettingsQuietly();
    }

    /// <summary>種類ごとのチェック項目と「すべてオン / 既定に戻す」を並べる (開くたびに作り直す)。</summary>
    private void FillSnapMenu(ItemCollection items)
    {
        items.Clear();
        foreach (var kind in Enum.GetValues<SnapKind>())
        {
            var item = new MenuItem
            {
                Header = SnapFinder.Label(kind) + (kind == SnapKind.Nearest ? " (他に候補が無いとき)" : ""),
                IsCheckable = true,
                IsChecked = Canvas.SnapKinds.Contains(kind),
                StaysOpenOnClick = true,
            };
            item.Click += (_, _) =>
            {
                if (item.IsChecked) Canvas.SnapKinds.Add(kind);
                else Canvas.SnapKinds.Remove(kind);
                SaveSnapKinds();
                RefreshSnapBar();
            };
            items.Add(item);
        }
        items.Add(new Separator());
        var all = new MenuItem { Header = "すべてオン" };
        all.Click += (_, _) => SetSnapKinds(Enum.GetValues<SnapKind>());
        var reset = new MenuItem { Header = "既定に戻す" };
        reset.Click += (_, _) => SetSnapKinds(SnapFinder.DefaultKinds);
        items.Add(all);
        items.Add(reset);
    }

    private void SetSnapKinds(IEnumerable<SnapKind> kinds)
    {
        Canvas.SnapKinds.Clear();
        Canvas.SnapKinds.UnionWith(kinds);
        SaveSnapKinds();
        RefreshSnapBar();
        Log("スナップ: " + string.Join("・", Canvas.SnapKinds.OrderBy(k => k).Select(SnapFinder.Label)));
    }

    private void SnapContextMenu_Opened(object sender, RoutedEventArgs e) => FillSnapMenu(SnapContextMenu.Items);

    private void SnapKindsMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource == SnapKindsMenu) FillSnapMenu(SnapKindsMenu.Items);
    }

    /// <summary>右下のスナップツールバーに、種類ごとのオン/オフボタンを並べる。</summary>
    private void BuildSnapBar()
    {
        SnapKindBar.Children.Clear();
        foreach (var kind in Enum.GetValues<SnapKind>())
        {
            var button = new ToggleButton
            {
                Content = SnapShortLabel(kind),
                Tag = kind,
                Focusable = false,
                Padding = new Thickness(5, 0, 5, 0),
                Margin = new Thickness(1, 0, 1, 0),
                ToolTip = $"{SnapFinder.Label(kind)}スナップ (クリックでオン/オフ)" + kind switch
                {
                    SnapKind.Perpendicular or SnapKind.Tangent => "\n直前に指定した点 (線の始点など) が基準",
                    SnapKind.Nearest => "\n他に候補が無いときだけ使う",
                    _ => "",
                },
            };
            button.Click += (_, _) =>
            {
                if (button.IsChecked == true) Canvas.SnapKinds.Add(kind);
                else Canvas.SnapKinds.Remove(kind);
                SaveSnapKinds();
            };
            SnapKindBar.Children.Add(button);
        }
        RefreshSnapBar();
    }

    private static string SnapShortLabel(SnapKind kind) => kind switch
    {
        SnapKind.Quadrant => "四半円",
        SnapKind.Perpendicular => "垂線",
        SnapKind.Nearest => "近接",
        _ => SnapFinder.Label(kind),
    };

    private void RefreshSnapBar()
    {
        foreach (var b in SnapKindBar.Children.OfType<ToggleButton>())
            b.IsChecked = b.Tag is SnapKind k && Canvas.SnapKinds.Contains(k);
    }

    // ---- 範囲選択の方法 (全体 / 一部) ----

    private const double MessageLogMin = 18;

    private void MessageLogSizer_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        // 上へドラッグすると広がる。キャンバスが残るよう、ウィンドウの 7 割までにする
        var max = Math.Max(MessageLogMin, ActualHeight * 0.7);
        MessageLog.Height = Math.Clamp(MessageLog.Height - e.VerticalChange, MessageLogMin, max);
        MessageLog.ScrollToEnd();
    }

    private void MessageLogSizer_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _settings.MessageLogHeight = MessageLog.Height;
        SaveSettingsQuietly();
    }

    private void LoadBoxSelectMode()
    {
        BoxDrag.Mode = Enum.TryParse<BoxSelectMode>(_settings.BoxSelectMode, out var m) ? m : BoxSelectMode.Inside;
        (BoxDrag.Mode == BoxSelectMode.Inside ? BoxInsideButton : BoxCrossingButton).IsChecked = true;
        Canvas.Tool.RefreshPrompt();
    }

    private void BoxMode_Checked(object sender, RoutedEventArgs e)
    {
        var mode = sender == BoxCrossingButton ? BoxSelectMode.Crossing : BoxSelectMode.Inside;
        if (BoxDrag.Mode == mode) return;
        BoxDrag.Mode = mode;
        _settings.BoxSelectMode = mode.ToString();
        SaveSettingsQuietly();
        if (Canvas.Tool is SelectTool or EraseTool) Canvas.Tool.RefreshPrompt(); // プロンプトの説明を更新
        Log(mode == BoxSelectMode.Inside ? "範囲選択: 全体 (範囲に完全に入る図形)" : "範囲選択: 一部 (範囲に少しでもかかる図形)");
        FocusCommandBox();
    }

    // ---- 表示範囲 ----

    /// <summary>
    /// 開いた直後の表示。SFC は用紙の中に図形がある程度 (1 割以上) あれば用紙全体を表示する
    /// (他 CAD の図面には用紙の外の遠くに図形が残っていることがあり、全体表示だと図面が点になるため)。
    /// </summary>
    private void ZoomAfterOpen(Box2? sheet)
    {
        if (sheet is Box2 s)
        {
            var visible = _doc.Entities.Where(_doc.IsVisible).ToList();
            var inside = visible.Count(e => s.Inflate(Math.Max(s.Width, s.Height) * 0.05).Contains(e.Bounds.Center));
            if (visible.Count > 0 && inside >= visible.Count * 0.1)
            {
                Canvas.ZoomTo(s);
                var outside = visible.Count - inside;
                if (outside > 0) Log($"用紙の範囲を表示しています (用紙の外に {outside} 個の図形があります。ZE で全体表示)。");
                return;
            }
        }
        ZoomExtentsWithReport();
    }

    /// <summary>全体表示。遠く離れた図形を除いたときはその数を知らせる。</summary>
    private void ZoomExtentsWithReport()
    {
        var excluded = Canvas.ZoomExtents();
        if (excluded > 0)
            Log($"全体表示: 大きく離れた位置にある {excluded} 個の図形を除いた範囲を表示しました (図形は消していません)。");
    }

    // ---- 平面直角座標系 ----

    private void OpenZoneDialog()
    {
        var ext = _doc.VisibleExtents();
        var dlg = new ZoneWindow(_doc.PlaneZone, _settings.DefaultPlaneZone == _doc.PlaneZone && _doc.PlaneZone != null,
            ext.IsEmpty ? null : ext.Center) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        if (dlg.Result != _doc.PlaneZone) _doc.History.Execute(new SetPlaneZoneCommand(dlg.Result));
        if (dlg.MakeDefault)
        {
            _settings.DefaultPlaneZone = dlg.Result;
            SaveSettingsQuietly();
        }
        Log(dlg.Result is int n
            ? $"平面直角座標系を {PlaneRectangular.Find(n)!.Name}にしました。ステータスバーに緯度・経度を表示します" +
              (dlg.MakeDefault ? " (新しい図面の既定にも設定)。" : "。")
            : "平面直角座標系を未設定にしました。");
        FocusCommandBox();
    }

    private void UpdateZoneButton() =>
        ZoneButton.Content = _doc.PlaneZone is int n ? $"座標系: {PlaneRectangular.Find(n)!.Name}" : "座標系: 未設定";

    private void ZoneButton_Click(object sender, RoutedEventArgs e) => RunCommand("ZN");

    // ---- 表示 ----

    private void UpdateCoordinates()
    {
        var p = Canvas.CursorWorld;
        var snap = Canvas.CurrentSnap is { } s ? $"  [{SnapFinder.Label(s.Kind)}]" : "";
        var latLon = "";
        if (_doc.PlaneZone is int n && PlaneRectangular.Find(n) is { } zone)
        {
            var (lat, lon) = PlaneRectangular.ToLatLon(zone, p.Y, p.X);
            latLon = $"   北緯 {PlaneRectangular.ToDms(lat, 2)}  東経 {PlaneRectangular.ToDms(lon, 2)}";
        }
        CoordText.Text = $"X(北) {p.Y,13:F3}  Y(東) {p.X,13:F3}{latLon}{snap}";
    }

    private void Log(string message)
    {
        _messages.Add(message);
        if (_messages.Count > 200) _messages.RemoveAt(0);
        MessageLog.Text = string.Join(Environment.NewLine, _messages);
        MessageLog.ScrollToEnd();
    }

    private void UpdateTitle() =>
        Title = $"CivilCad - {_active.FileName}  [{_active.Doc.ScaleText} / {_active.ZoneText}]";

    private static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        while (d != null && d is not T) d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        return d as T;
    }
}
