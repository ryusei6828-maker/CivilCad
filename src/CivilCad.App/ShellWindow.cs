using System.Windows;

namespace CivilCad.App;

/// <summary>
/// CivilCad.exe の窓。画面 (<see cref="MainWindow"/>、部品) を載せ、窓に属すること
/// (タイトル・アイコン・閉じるときの確認・アプリの終了・窓に落ちたキー入力) を受け持つ。
/// </summary>
public sealed class ShellWindow : Window
{
    public MainWindow Editor { get; }

    public ShellWindow()
    {
        Editor = new MainWindow();
        Content = Editor;
        Width = 1400;
        Height = 900;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/CivilCad;component/civilcad.ico"));
        Title = Editor.Title;
        Editor.TitleChanged += (_, _) => Title = Editor.Title;
        // 窓にフォーカスがあるとき (画面の中の要素にフォーカスが無いとき) のキーと元に戻す等のコマンドも、画面で扱う
        CommandBindings.AddRange(Editor.CommandBindings);
        PreviewKeyDown += (s, e) => { if (!Editor.IsKeyboardFocusWithin) Editor.HandleWindowKey(s, e); };
        Closing += (_, e) => { if (!Editor.RequestClose()) e.Cancel = true; };
        // 本体を閉じたらアプリを終える (隠れているフロートのパネルなどが残ってプロセスが終わらないことがあった)
        Closed += (_, _) => Editor.NotifyClosed();
    }
}
