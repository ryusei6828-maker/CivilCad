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
                var settings = new SettingsWindow(new AppSettings());
                var frame = new CivilCad.App.Drawing.FrameWindow(new CivilCad.App.Drawing.DrawingFrameOptions());
                Assert.NotNull(main.Content);
                // レイヤパネルは既定で閉じ、プロパティは開いている
                Assert.Equal(System.Windows.Visibility.Collapsed, ((System.Windows.FrameworkElement)main.FindName("LayerHost")).Visibility);
                Assert.Equal(System.Windows.Visibility.Visible, ((System.Windows.FrameworkElement)main.FindName("PropsHost")).Visibility);
                // ツールバーのタブ: 最初は「描画」で、線・ポリライン・円・円弧・文字が入っている
                var tabs = (System.Windows.Controls.TabControl)main.FindName("RibbonTabs");
                var draw = (System.Windows.Controls.TabItem)tabs.Items[0];
                Assert.Equal("描画", draw.Header);
                Assert.Equal(0, tabs.SelectedIndex);
                Assert.Equal(["L", "PL", "C", "A", "PE", "T", "H", "FI"],
                    ((System.Windows.Controls.Panel)draw.Content).Children.OfType<System.Windows.Controls.Button>().Select(b => (string)b.Tag));
                // 「修正」タブ: 移動・複写・回転・オフセット・トリム・延長・連結・削除
                var modify = (System.Windows.Controls.TabItem)tabs.Items[1];
                Assert.Equal("修正", modify.Header);
                Assert.Equal(["M", "CO", "RO", "O", "TR", "EX", "J", "E"],
                    ((System.Windows.Controls.Panel)modify.Content).Children.OfType<System.Windows.Controls.Button>().Select(b => (string)b.Tag));
                Assert.NotNull(settings.Content);
                Assert.NotNull(frame.Content);
                frame.Close();
                var zoneWin = new CivilCad.App.Drawing.ZoneWindow(2, false, new CivilCad.Core.Geometry.Vec2(-60760, -95537));
                Assert.NotNull(zoneWin.Content);
                zoneWin.Close();
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
