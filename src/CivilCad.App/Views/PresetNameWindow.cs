using System.Windows;
using System.Windows.Controls;
using CivilCad.App.Tools;

namespace CivilCad.App.Views;

/// <summary>プリセット登録の名前入力。</summary>
public sealed class PresetNameWindow : Window
{
    private readonly TextBox _name;
    private readonly CheckBox _replace;

    public PresetRegisterInput? Result { get; private set; }

    public PresetNameWindow(string defaultName)
    {
        Title = "プリセット登録";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = "プリセットの名前", Margin = new Thickness(0, 0, 0, 4) });
        _name = new TextBox { Text = defaultName, Width = 280, Padding = new Thickness(2) };
        panel.Children.Add(_name);
        _replace = new CheckBox
        {
            Content = "選択した図形をこのプリセット 1 つに置き換える",
            Margin = new Thickness(0, 10, 0, 0),
            ToolTip = "オフなら、選択した図形はそのまま残る",
        };
        panel.Children.Add(_replace);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var ok = new Button { Content = "登録", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "キャンセル", Width = 80, IsCancel = true };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_name.Text))
            {
                _name.Focus();
                return;
            }
            Result = new PresetRegisterInput(_name.Text.Trim(), _replace.IsChecked == true);
            DialogResult = true;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;

        Loaded += (_, _) =>
        {
            _name.Focus();
            _name.SelectAll();
        };
    }
}
