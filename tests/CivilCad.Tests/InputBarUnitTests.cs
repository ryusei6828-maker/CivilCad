using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

[Collection("SharedToolSettings")] // 共有の設定を書き換える
public class InputBarUnitTests
{
    [Fact]
    public void NumberStaysUnitChanges()
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var doc = new CadDocument();
                var canvas = new CadCanvas { Document = doc };
                var bar = new DrawInputBar();
                var dock = new DockPanel();
                DockPanel.SetDock(bar, Dock.Top);
                dock.Children.Add(bar); // キャンバスは画面に出さない (他のテストのスレッドで作られた描画用の静的リソースを避ける)
                var command = new TextBox();
                DockPanel.SetDock(command, Dock.Bottom);
                dock.Children.Insert(0, command);
                var win = new Window { Content = dock, Width = 1100, Height = 300, Left = -3000, Top = -3000, WindowStyle = WindowStyle.None };
                win.Show();
                bar.Attach(canvas);
                bar.Committed += (_, _) => Keyboard.Focus(command);

                DrawInputMemory.Unit = LengthUnit.Mm;
                canvas.SetTool(new CircleTool());
                win.UpdateLayout();
                Assert.True(bar.FocusFirstField());
                var box = (TextBox)Keyboard.FocusedElement;
                box.Text = "500";
                // 入力中に切替
                bar.CycleUnit();
                Assert.Equal("500", box.Text);
                Assert.Equal(5, DrawInputMemory.Radius!.Value, 9);       // 500 cm
                bar.CycleUnit();
                Assert.Equal("500", box.Text);
                Assert.Equal(500, DrawInputMemory.Radius!.Value, 9);     // 500 m

                // 確定して作図に戻ったあとに切替 (欄にフォーカスなし)
                bar.CycleUnit(); // → mm
                box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box), 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Assert.True(command.IsKeyboardFocused);
                Assert.Equal(0.5, DrawInputMemory.Radius!.Value, 9);     // 500 mm
                bar.CycleUnit(); // → cm
                Assert.Equal("500", box.Text);
                Assert.Equal(5, DrawInputMemory.Radius!.Value, 9);
                bar.CycleUnit(); // → m
                Assert.Equal("500", box.Text);
                Assert.Equal(500, DrawInputMemory.Radius!.Value, 9);
                win.Close();
            }
            catch (Exception ex) { error = ex; }
            finally
            {
                DrawInputMemory.Radius = null;
                DrawInputMemory.Unit = LengthUnit.M;
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
