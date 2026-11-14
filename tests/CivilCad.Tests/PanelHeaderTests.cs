using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CivilCad.App.Views;

namespace CivilCad.Tests;

public class PanelHeaderTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    /// <summary>閉じるボタンは、ふだんは背景なしで、カーソルを乗せると Windows の閉じるボタンと同じ赤になる。</summary>
    [Fact]
    public void CloseButton_TurnsRedOnHover() => RunSta(() =>
    {
        var button = new Button { Content = "×", Padding = PanelHeader.ButtonPadding, FontSize = PanelHeader.ButtonFontSize };
        PanelHeader.MakeCloseButton(button);
        button.Measure(new Size(100, 100));
        button.Arrange(new Rect(0, 0, 40, 30));
        button.ApplyTemplate();

        var border = Assert.IsType<Border>(button.Template.FindName("Bd", button));
        Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(border.Background).Color);
        Assert.Equal(PanelHeader.ButtonPadding, border.Padding);

        // カーソルを乗せたとき・押したときの色は、ひな形の切り替え (Trigger) に入っている
        var triggers = button.Template.Triggers.OfType<Trigger>().ToList();
        var hover = Assert.Single(triggers, t => t.Property == UIElement.IsMouseOverProperty);
        var background = hover.Setters.OfType<Setter>().Single(s => s.Property == Border.BackgroundProperty);
        Assert.Equal(PanelHeader.CloseHoverColor, Assert.IsType<SolidColorBrush>(background.Value).Color);
        var foreground = hover.Setters.OfType<Setter>().Single(s => s.Property == System.Windows.Documents.TextElement.ForegroundProperty);
        Assert.Same(Brushes.White, foreground.Value);
        Assert.Contains(triggers, t => t.Property == System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty);
    });
}
