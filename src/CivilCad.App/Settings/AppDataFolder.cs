namespace CivilCad.App.Settings;

/// <summary>
/// 設定 (settings.json)・登録プリセット (presets.json)・エラーログ (error.log) を置くフォルダ。ふつうは %APPDATA%\CivilCad。
/// 環境変数 CIVILCAD_DATA_DIR があればそのフォルダにする (テストが、使う人の本物の設定を読んだり書き換えたりしないため)。
/// 最初に使うときに 1 度だけ決まるので、変えるならアプリを起動する前に。
/// </summary>
public static class AppDataFolder
{
    public const string OverrideVariable = "CIVILCAD_DATA_DIR";

    public static string Folder { get; } = Resolve(Environment.GetEnvironmentVariable(OverrideVariable));

    /// <summary>overrideDir (空でなければ) か、%APPDATA%\CivilCad。</summary>
    public static string Resolve(string? overrideDir) =>
        !string.IsNullOrWhiteSpace(overrideDir)
            ? overrideDir
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CivilCad");
}
