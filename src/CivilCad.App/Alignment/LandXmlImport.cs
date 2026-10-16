using System.IO;
using System.Windows;
using System.Windows.Controls;
using CivilCad.Alignment;
using Microsoft.Win32;

namespace CivilCad.App.Alignment;

/// <summary>路線の LandXML (J-LandXML) を選んで読み込む。路線が複数あれば選ばせる。</summary>
public static class LandXmlImport
{
    /// <summary>ファイルを選んで読み込む。キャンセル・失敗なら null (失敗はメッセージを出す)。</summary>
    public static AlignmentInput? Run(Window owner, double textHeightMm)
    {
        var dlg = new OpenFileDialog
        {
            Title = "路線の XML (LandXML) を開く",
            Filter = "LandXML (*.xml)|*.xml|すべてのファイル (*.*)|*.*",
        };
        if (dlg.ShowDialog(owner) != true) return null;
        return Load(owner, dlg.FileName, textHeightMm);
    }

    public static AlignmentInput? Load(Window owner, string path, double textHeightMm)
    {
        List<LandXmlAlignment> list;
        try
        {
            list = LandXmlReader.Read(path);
        }
        catch (Exception ex) when (ex is AlignmentException or IOException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            MessageBox.Show(owner, $"{Path.GetFileName(path)} を読み込めませんでした。\n{ex.Message}", "路線 XML の読み込み",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        if (list.Count == 1) return AlignmentInput.FromLandXml(list[0], textHeightMm);
        var index = Pick(owner, "このファイルには路線が複数あります。読み込む路線を選んでください。",
            list.Select(a => $"{a.Name}    (IP {a.Ips.Count - 2} 点、延長 {a.FileLength:F3} m)").ToList());
        return index < 0 ? null : AlignmentInput.FromLandXml(list[index], textHeightMm);
    }

    /// <summary>一覧から 1 つ選ばせる。選んだ番号、キャンセルなら -1。</summary>
    public static int Pick(Window owner, string message, IReadOnlyList<string> items)
    {
        var box = new ListBox { Margin = new Thickness(0, 6, 0, 8), MinHeight = 160, SelectedIndex = 0 };
        foreach (var item in items) box.Items.Add(item);
        box.SelectedIndex = 0;

        var ok = new Button { Content = "読み込む", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "キャンセル", Width = 90, IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new DockPanel { Margin = new Thickness(10) };
        var label = new TextBlock { Text = message };
        DockPanel.SetDock(label, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(label);
        panel.Children.Add(buttons);
        panel.Children.Add(box);

        var win = new Window
        {
            Title = "路線を選択",
            Owner = owner,
            Width = 460,
            Height = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = panel,
        };
        ok.Click += (_, _) => win.DialogResult = true;
        box.MouseDoubleClick += (_, _) => { if (box.SelectedIndex >= 0) win.DialogResult = true; };
        return win.ShowDialog() == true ? box.SelectedIndex : -1;
    }
}
