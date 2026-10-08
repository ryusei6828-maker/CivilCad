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
                // ツールバーのタブ: 最初は「描画」で、線・ポリライン・円・円弧・文字が入っている
                var tabs = (System.Windows.Controls.TabControl)main.FindName("RibbonTabs");
                var draw = (System.Windows.Controls.TabItem)tabs.Items[0];
                Assert.Equal("描画", draw.Header);
                Assert.Equal(0, tabs.SelectedIndex);
                Assert.Equal(["L", "PL", "C", "A", "REC", "PO", "PE", "T", "DIM", "LD", "HA", "H", "FI", "PS", "PSR"],
                    ((System.Windows.Controls.Panel)draw.Content).Children.OfType<System.Windows.Controls.Button>().Select(b => (string)b.Tag));
                // 「修正」タブ: 移動・複写・回転・拡大縮小・2 点指示・オフセット・トリム・延長・文字変更・結合・削除
                var modify = (System.Windows.Controls.TabItem)tabs.Items[1];
                Assert.Equal("修正", modify.Header);
                Assert.Equal(["M", "CO", "RO", "SCL", "AL", "O", "TR", "EX", "ED", "J", "E"],
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
