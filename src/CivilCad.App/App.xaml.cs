using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace CivilCad.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static string ErrorLogPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CivilCad", "error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        Settings.UserPresetStore.Load(); // 登録したプリセット
    }

    /// <summary>
    /// 想定外のエラーでソフトが落ちないようにする: 内容を error.log に書き、メッセージを出して作業を続けられるようにする。
    /// </summary>
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ErrorLogPath)!);
            File.AppendAllText(ErrorLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\n\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // ログが書けなくても続ける
        }
        e.Handled = true;
        MessageBox.Show(MainWindow,
            $"エラーが発生しました (ソフトは続けて使えます)。\n{e.Exception.Message}\n\n詳細: {ErrorLogPath}\n念のため、図面を別名で保存しておくことをおすすめします。",
            "CivilCad", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
