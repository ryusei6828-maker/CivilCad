using System.Runtime.ExceptionServices;
using CivilCad.App.Settings;

namespace CivilCad.Tests;

/// <summary>画面 (XAML) が例外なく組み立てられるかの確認。WPF は STA スレッドが必要。</summary>
public class WindowLoadTests
{
    [Fact]
    public void MainAndSettingsWindows_Construct()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new CivilCad.App.App();
                app.InitializeComponent(); // アイコン等のリソースを読み込む
                var main = new CivilCad.App.MainWindow();
                {
                    // 文字コマンド: クリックした位置に入力欄が出て、打った文字が見本・確定に使われる
                    var textCanvas = (CivilCad.App.Views.CadCanvas)main.FindName("Canvas");
                    var inline = (System.Windows.Controls.TextBox)main.FindName("InlineTextBox");
                    var textTool = new CivilCad.App.Tools.TextTool();
                    textCanvas.SetTool(textTool);
                    Assert.Equal(System.Windows.Visibility.Collapsed, inline.Visibility);
                    textTool.OnPoint(new CivilCad.Core.Geometry.Vec2(5, 5));
                    Assert.Equal(System.Windows.Visibility.Visible, inline.Visibility);
                    inline.Text = "その場で入力";
                    Assert.Equal("その場で入力", textTool.DraftText);
                    textTool.SubmitText(inline.Text);
                    Assert.Equal(System.Windows.Visibility.Collapsed, inline.Visibility);
                    Assert.Equal("", inline.Text);
                    Assert.Contains(textCanvas.Document.Entities.OfType<CivilCad.Core.Model.TextEntity>(), t => t.Text == "その場で入力");
                    textCanvas.SetTool(new CivilCad.App.Tools.SelectTool());
                    textCanvas.Document.Entities.Clear();
                    // 文字を置いたのでシートが「変更あり」になっている。閉じるときの「保存していないシートがあります」の
                    // 確認 (見えないダイアログ) でテストが止まらないよう、保存済みの扱いに戻す
                    var active = typeof(CivilCad.App.MainWindow).GetField("_active", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(main)!;
                    active.GetType().GetMethod("MarkSaved")!.Invoke(active, null);
                }
                var settings = new SettingsWindow(new AppSettings());
                var frame = new CivilCad.App.Drawing.FrameWindow(new CivilCad.App.Drawing.DrawingFrameOptions(), new CivilCad.Core.Geometry.Box2(new(0, 0), new(8, 4)));
                Assert.NotNull(main.Content);
                // レイヤパネルは既定で閉じ、プロパティは開いている
                Assert.Equal(System.Windows.Visibility.Collapsed, ((System.Windows.FrameworkElement)main.FindName("LayerHost")).Visibility);
                Assert.Equal(System.Windows.Visibility.Visible, ((System.Windows.FrameworkElement)main.FindName("PropsHost")).Visibility);
                // ツールバーのタブ: 最初は「描画」
                var tabs = (System.Windows.Controls.TabControl)main.FindName("RibbonTabs");
                var draw = (System.Windows.Controls.TabItem)tabs.Items[0];
                Assert.Equal("描画", draw.Header);
                Assert.Equal(0, tabs.SelectedIndex);
                Assert.Equal(["L", "PL", "C", "A", "REC", "PO", "PE", "DIM", "H", "FI", "PS", "PSR"],
                    ((System.Windows.Controls.Panel)draw.Content).Children.OfType<System.Windows.Controls.Button>().Select(b => (string)b.Tag));
                // 「文字」タブに文字・文字変更・引き出し線・旗揚げ、「修正」タブに移動〜結合 (削除は左のいつも出すボタン)
                var text = (System.Windows.Controls.TabItem)tabs.Items[1];
                Assert.Equal("文字", text.Header);
                Assert.Equal(["T", "ED", "LD", "HA"],
                    ((System.Windows.Controls.Panel)text.Content).Children.OfType<System.Windows.Controls.Button>().Select(b => (string)b.Tag));
                var modify = (System.Windows.Controls.TabItem)tabs.Items[2];
                Assert.Equal("修正", modify.Header);
                Assert.Equal(["M", "CO", "RO", "SCL", "AL", "O", "TR", "EX", "J"],
                    ((System.Windows.Controls.Panel)modify.Content).Children.OfType<System.Windows.Controls.Button>().Select(b => (string)b.Tag));
                Assert.NotNull(settings.Content);
                Assert.NotNull(frame.Content);
                frame.Close();
                var zoneWin = new CivilCad.App.Drawing.ZoneWindow(2, false, new CivilCad.Core.Geometry.Vec2(-60760, -95537));
                Assert.NotNull(zoneWin.Content);
                zoneWin.Close();
                var pdfWin = new CivilCad.App.Views.PdfWindow(new CivilCad.Core.Model.CadDocument());
                Assert.NotNull(pdfWin.Content);
                pdfWin.Close();
                // 選択していれば、印刷範囲は「選択した図形だけ」が最初に選ばれる
                var pdfDoc = new CivilCad.Core.Model.CadDocument();
                var sel = new CivilCad.Core.Model.LineEntity(pdfDoc.CurrentLayer, new(0, 0), new(10, 0));
                pdfDoc.Entities.Add(sel);
                var pdfSel = new CivilCad.App.Views.PdfWindow(pdfDoc, new(new(new(-5, -5), new(5, 5)), [sel], null));
                var rangeBox = (System.Windows.Controls.ComboBox)typeof(CivilCad.App.Views.PdfWindow)
                    .GetField("_range", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(pdfSel)!;
                Assert.Equal((int)CivilCad.App.IO.PdfRange.Selection, rangeBox.SelectedIndex);
                // 「範囲を指定」でまだ範囲が無ければ、ボタンは範囲の指定になる
                rangeBox.SelectedIndex = (int)CivilCad.App.IO.PdfRange.Window;
                var okButton = (System.Windows.Controls.Button)typeof(CivilCad.App.Views.PdfWindow)
                    .GetField("_ok", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(pdfSel)!;
                Assert.Equal("範囲を指定する", okButton.Content);
                pdfSel.Close();
                var importWin = new CivilCad.App.Views.ImportScaleWindow("a.dxf", new CivilCad.App.IO.DrawingFiles.OpenResult("", null, 100, CivilCad.App.IO.ImportUnit.Millimeter),
                    new CivilCad.Core.Geometry.Box2(new(0, 0), new(38000, 25700)), 500);
                Assert.NotNull(importWin.Content);
                importWin.Close();
                var textEdit = new CivilCad.App.Views.TextEditWindow("No.1");
                Assert.NotNull(textEdit.Content);
                textEdit.Close();
                // 文字を選ぶと、閉じていたプロパティ (右側) が開く
                var canvas = (CivilCad.App.Views.CadCanvas)main.FindName("Canvas");
                // 選択モード (SEL): 実行中のコマンドを終えて選ぶ状態に戻り、ボタンが色付きになる
                var run = typeof(CivilCad.App.MainWindow).GetMethod("RunCommand", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                run.Invoke(main, ["L"]);
                Assert.IsType<CivilCad.App.Tools.LineTool>(canvas.Tool);
                run.Invoke(main, ["SEL"]);
                Assert.IsType<CivilCad.App.Tools.SelectTool>(canvas.Tool);
                var selButton = ((System.Windows.Controls.Panel)((System.Windows.Controls.DockPanel)main.FindName("RibbonArea")).Children[0]).Children.OfType<System.Windows.Controls.Button>().First();
                Assert.Equal("E", ((System.Windows.Controls.Panel)((System.Windows.Controls.DockPanel)main.FindName("RibbonArea")).Children[0]).Children.OfType<System.Windows.Controls.Button>().ElementAt(1).Tag);
                Assert.Equal("SEL", selButton.Tag);
                typeof(CivilCad.App.MainWindow).GetMethod("UpdateToolbarHighlight", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(main, []);
                Assert.NotNull(selButton.ReadLocalValue(System.Windows.Controls.Control.BackgroundProperty) as System.Windows.Media.Brush);
                // 右クリックメニュー: コマンド中は一番上で選択モードに、選択モード中は範囲選択の方法を選べる
                var build = typeof(CivilCad.App.MainWindow).GetMethod("BuildCanvasMenu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                run.Invoke(main, ["L"]);
                var toolMenu = (System.Windows.Controls.ContextMenu)build.Invoke(main, [])!;
                var toSelect = (System.Windows.Controls.MenuItem)toolMenu.Items[0];
                Assert.StartsWith("選択モード", (string)toSelect.Header);
                toSelect.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
                Assert.IsType<CivilCad.App.Tools.SelectTool>(canvas.Tool);
                var selectMenu = (System.Windows.Controls.ContextMenu)build.Invoke(main, [])!;
                var items = selectMenu.Items.OfType<System.Windows.Controls.MenuItem>().ToList();
                Assert.True(items[0].IsChecked);
                Assert.Equal("選択モード", items[0].Header);
                var wasInside = CivilCad.App.Tools.BoxDrag.Mode == CivilCad.App.Tools.BoxSelectMode.Inside;
                (wasInside ? items[2] : items[1]).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
                Assert.Equal(wasInside ? CivilCad.App.Tools.BoxSelectMode.Crossing : CivilCad.App.Tools.BoxSelectMode.Inside, CivilCad.App.Tools.BoxDrag.Mode);
                (wasInside ? items[1] : items[2]).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent)); // 元に戻す
                var propsHost = (System.Windows.FrameworkElement)main.FindName("PropsHost");
                typeof(CivilCad.App.MainWindow).GetMethod("RunCommand", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .Invoke(main, ["PR"]);
                Assert.Equal(System.Windows.Visibility.Collapsed, propsHost.Visibility);
                var line = new CivilCad.Core.Model.LineEntity(canvas.Document.CurrentLayer, new(0, 0), new(1, 0));
                var label = new CivilCad.Core.Model.TextEntity(canvas.Document.CurrentLayer, new(0, 5), "No.1", 1);
                canvas.Document.Entities.AddRange([line, label]);
                canvas.SetSelection([line]);
                Assert.Equal(System.Windows.Visibility.Collapsed, propsHost.Visibility); // 線では開かない
                canvas.SetSelection([label]);
                Assert.Equal(System.Windows.Visibility.Visible, propsHost.Visibility);
                // コピーして新しいシートに貼り付け: 同じ位置に入り、選択される
                canvas.SetSelection([line]);
                run.Invoke(main, ["CB"]);
                var firstDoc = canvas.Document;
                typeof(CivilCad.App.MainWindow).GetMethod("New_Executed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(main, [main, null]);
                Assert.NotSame(firstDoc, canvas.Document);
                run.Invoke(main, ["PA"]);
                var pastedLine = Assert.IsType<CivilCad.Core.Model.LineEntity>(Assert.Single(canvas.Document.Entities));
                Assert.Equal(line.Start, pastedLine.Start);
                Assert.Equal([pastedLine], canvas.Selection);
                CivilCad.App.Tools.EntityClipboard.Clear();
                foreach (var s in (System.Collections.IEnumerable)typeof(CivilCad.App.MainWindow).GetField("_sheets", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(main)!)
                    s.GetType().GetMethod("MarkSaved")!.Invoke(s, null);
                // ドラッグ＆ドロップ: プロジェクトを落とすとシートとして開く (開けない種類はメッセージだけ)
                Assert.True(CivilCad.App.MainWindow.CanOpenByDrop(@"C:\a\本線.SFC"));
                Assert.True(CivilCad.App.MainWindow.CanOpenByDrop(@"C:\a\p.ccp"));
                Assert.False(CivilCad.App.MainWindow.CanOpenByDrop(@"C:\a\memo.txt"));
                var projDoc = new CivilCad.Core.Model.CadDocument();
                projDoc.Entities.Add(new CivilCad.Core.Model.PointEntity(projDoc.CurrentLayer, new CivilCad.Core.Geometry.Vec2(1, 2)) { Name = "落とした点" });
                var projPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}.ccp");
                CivilCad.App.IO.ProjectFile.Save(projPath, [new CivilCad.App.IO.ProjectSheet("ドロップ", projDoc, CivilCad.App.Alignment.AlignmentInput.Sample(), false)], 0);
                try
                {
                    main.OpenDroppedFiles([projPath, @"C:\a\memo.txt"]);
                    Assert.Contains(canvas.Document.Entities.OfType<CivilCad.Core.Model.PointEntity>(), p => p.Name == "落とした点");
                    Assert.Contains("ドロップ", main.Title);
                }
                finally
                {
                    System.IO.File.Delete(projPath);
                }
                // プロジェクト: シートを 1 枚ずつ別のファイルに保存し、タブを閉じたシートはプロジェクトから外す
                {
                    var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}");
                    System.IO.Directory.CreateDirectory(dir);
                    try
                    {
                        var project = System.IO.Path.Combine(dir, "工事.ccp");
                        typeof(CivilCad.App.MainWindow).GetField("_projectPath", bf)!.SetValue(main, project);
                        var sheets = (System.Collections.IList)typeof(CivilCad.App.MainWindow).GetField("_sheets", bf)!.GetValue(main)!;
                        Assert.True((bool)typeof(CivilCad.App.MainWindow).GetMethod("SaveProject", bf)!.Invoke(main, [false])!);
                        var files = System.IO.Directory.GetFiles(CivilCad.App.IO.ProjectFile.SheetFolder(project), "*.ccs");
                        Assert.Equal(sheets.Count, files.Length);
                        Assert.Equal(sheets.Count, CivilCad.App.IO.ProjectFile.Load(project).Sheets.Count);
                        var first = (CivilCad.App.Sheet)sheets[0]!;
                        typeof(CivilCad.App.MainWindow).GetMethod("CloseSheet", bf)!.Invoke(main, [first]); // 保存済みなので確認は出ない
                        var after = CivilCad.App.IO.ProjectFile.Load(project);
                        Assert.Equal(sheets.Count, after.Sheets.Count);
                        Assert.DoesNotContain(after.Sheets, s => s.SheetFile == first.ProjectSheetPath);
                        Assert.True(System.IO.File.Exists(first.ProjectSheetPath)); // ファイルは消さない
                        typeof(CivilCad.App.MainWindow).GetField("_projectPath", bf)!.SetValue(main, null);
                    }
                    finally
                    {
                        System.IO.Directory.Delete(dir, true);
                    }
                }
                // 「座標管理」タブを開くと座標の一覧も開く
                var coordHost = (System.Windows.FrameworkElement)main.FindName("CoordHost");
                Assert.Equal(System.Windows.Visibility.Collapsed, coordHost.Visibility);
                tabs.SelectedItem = tabs.Items.OfType<System.Windows.Controls.TabItem>().Single(t => (string)t.Header == "座標管理");
                Assert.Equal(System.Windows.Visibility.Visible, coordHost.Visibility);
                tabs.SelectedIndex = 0;
                var scale = new CivilCad.App.Drawing.ScaleWindow(500);
                Assert.NotNull(scale.Content);
                scale.Close();
                main.Close();
                settings.Close();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
