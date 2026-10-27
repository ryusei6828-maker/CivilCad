using System.IO;
using System.Runtime.CompilerServices;
using CivilCad.App.Settings;

namespace CivilCad.Tests;

/// <summary>
/// テストが、使う人の本物の設定 (%APPDATA%\CivilCad の settings.json・presets.json・error.log) を読んだり書き換えたりしないようにする。
/// 本体のウィンドウを作るテストは設定を読み (範囲選択の方法などがテストの前提と食い違う)、設定を変える操作は保存までするため。
/// テストのプログラムが読み込まれたとき (どのテストよりも前) に、設定の置き場所を一時フォルダにして、終わったら消す。
/// </summary>
internal static class TestEnvironment
{
    /// <summary>この実行で使う設定の置き場所 (一時フォルダ)。</summary>
    public static string DataDir { get; } =
        Path.Combine(Path.GetTempPath(), "CivilCadTests_" + Environment.ProcessId);

    [ModuleInitializer]
    internal static void IsolateUserData()
    {
        Directory.CreateDirectory(DataDir);
        Environment.SetEnvironmentVariable(AppDataFolder.OverrideVariable, DataDir);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(DataDir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        };
    }
}

public class TestEnvironmentTests
{
    [Fact]
    public void UserDataFolder_IsATemporaryFolder_NotTheRealAppData()
    {
        var real = AppDataFolder.Resolve(null);
        Assert.EndsWith(Path.Combine("AppData", "Roaming", "CivilCad"), real, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(TestEnvironment.DataDir, AppDataFolder.Folder);
        Assert.NotEqual(real, AppDataFolder.Folder);
        Assert.StartsWith(TestEnvironment.DataDir, AppSettings.DefaultPath);
        Assert.StartsWith(TestEnvironment.DataDir, UserPresetStore.DefaultPath);
        Assert.StartsWith(TestEnvironment.DataDir, CivilCad.App.App.ErrorLogPath);
    }

    [Fact]
    public void Resolve_UsesTheOverrideWhenGiven()
    {
        Assert.Equal(@"D:\x", AppDataFolder.Resolve(@"D:\x"));
        Assert.Equal(AppDataFolder.Resolve(null), AppDataFolder.Resolve(""));
        Assert.Equal(AppDataFolder.Resolve(null), AppDataFolder.Resolve("  "));
    }
}
