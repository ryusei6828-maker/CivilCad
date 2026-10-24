using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CivilCad.App.Views;

/// <summary>終了時の保存の確認で押したボタン。</summary>
public enum SaveChangesAnswer
{
    Save,
    DontSave,
    Cancel,
}

/// <summary>
/// 終了時・シートを閉じるときの確認 (Excel (Microsoft 365) と同じボタン):
/// 「このファイルの変更内容をプロジェクトファイルに保存しますか？」 [保存] [保存しない] [キャンセル]。Enter は保存、Esc と × はキャンセル。
/// 下に保存していないシートの名前を出す。
/// </summary>
public sealed class SaveChangesWindow : Window
{
    public const string Message = "このファイルの変更内容をプロジェクトファイルに保存しますか？";

    public SaveChangesAnswer Answer { get; private set; } = SaveChangesAnswer.Cancel;

    public SaveChangesWindow(IReadOnlyList<string> unsavedSheets)
    {
        Title = "CivilCad";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        Button Make(string text, SaveChangesAnswer answer)
        {
            var b = new Button { Content = text, MinWidth = 88, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0) };
            b.Click += (_, _) =>
            {
                Answer = answer;
                Close();
            };
            return b;
        }
        SaveButton = Make("保存", SaveChangesAnswer.Save);
        SaveButton.IsDefault = true;
        DontSaveButton = Make("保存しない", SaveChangesAnswer.DontSave);
        CancelButton = Make("キャンセル", SaveChangesAnswer.Cancel);
        CancelButton.IsCancel = true;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(SaveButton);
        buttons.Children.Add(DontSaveButton);
        buttons.Children.Add(CancelButton);

        var panel = new StackPanel { Margin = new Thickness(16, 14, 16, 12) };
        panel.Children.Add(new TextBlock { Text = Message, FontSize = 14, TextWrapping = TextWrapping.Wrap });
        if (unsavedSheets.Count > 0)
            panel.Children.Add(new TextBlock
            {
                Text = "保存していないシート: " + string.Join("、", unsavedSheets),
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            });
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => SaveButton.Focus();
    }

    public Button SaveButton { get; }
    public Button DontSaveButton { get; }
    public Button CancelButton { get; }

    /// <summary>確認の窓を出して、押したボタンを返す (× で閉じたらキャンセル)。</summary>
    public static SaveChangesAnswer Ask(Window? owner, IReadOnlyList<string> unsavedSheets)
    {
        var w = new SaveChangesWindow(unsavedSheets);
        if (owner != null && owner.IsVisible) w.Owner = owner;
        w.ShowDialog();
        return w.Answer;
    }
}
