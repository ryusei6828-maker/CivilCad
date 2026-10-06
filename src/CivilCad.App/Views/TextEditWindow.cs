using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CivilCad.App.Views;

/// <summary>文字の中身を入力する小さな窓 (Enter で確定、Esc でやめる)。</summary>
public sealed class TextEditWindow : Window
{
    private readonly TextBox _box;

    public string? Result { get; private set; }

    public TextEditWindow(string current)
    {
        Title = "文字の変更";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _box = new TextBox { Text = current, Margin = new Thickness(0, 4, 0, 10), FontSize = 14 };
        var ok = new Button { Content = "変更", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "キャンセル", Width = 80, IsCancel = true };
        ok.Click += (_, _) =>
        {
            Result = _box.Text;
            DialogResult = true;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "新しい文字 (Enter で変更、Esc でやめる)" });
        panel.Children.Add(_box);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
            Keyboard.Focus(_box);
        };
    }

    /// <summary>窓を出して新しい文字列を返す (やめたら null)。</summary>
    public static string? Ask(Window? owner, string current)
    {
        var w = new TextEditWindow(current);
        if (owner != null && owner.IsVisible) w.Owner = owner;
        return w.ShowDialog() == true ? w.Result : null;
    }
}
