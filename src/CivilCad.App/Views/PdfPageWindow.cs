using System.Windows;
using System.Windows.Controls;
using CivilCad.App.IO;

namespace CivilCad.App.Views;

/// <summary>複数ページの PDF から、読み込むページを選ぶ。</summary>
public sealed class PdfPageWindow : Window
{
    public PdfPageInfo? Result { get; private set; }

    public PdfPageWindow(string fileName, IReadOnlyList<PdfPageInfo> pages)
    {
        Title = "PDF のページを選択";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = $"{fileName} ({pages.Count} ページ) のどのページを読み込みますか？", Margin = new Thickness(0, 0, 0, 6) });
        var list = new ListBox
        {
            Width = 340,
            MaxHeight = 320,
            ItemsSource = pages.Select(p => p.Label).ToList(),
            SelectedIndex = 0,
        };
        panel.Children.Add(list);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "読み込む", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "キャンセル", Width = 80, IsCancel = true };
        void Accept()
        {
            if (list.SelectedIndex < 0) return;
            Result = pages[list.SelectedIndex];
            DialogResult = true;
        }
        ok.Click += (_, _) => Accept();
        list.MouseDoubleClick += (_, _) => Accept();
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => list.Focus();
    }
}
