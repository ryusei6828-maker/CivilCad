using System.IO;
using System.Text.Json;
using CivilCad.App.Drawing;
using CivilCad.App.Views;

namespace CivilCad.App.Settings;

/// <summary>ユーザー設定。%APPDATA%\CivilCad\settings.json に保存する。</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CivilCad", "settings.json");

    /// <summary>コマンド名 → キー ("" は割り当てなし)。</summary>
    public Dictionary<string, string> KeyBindings { get; set; } = new(CommandRegistry.DefaultBindings);

    /// <summary>有効にするスナップの種類 (SnapKind の名前)。null なら既定。</summary>
    public List<string>? SnapKinds { get; set; }

    /// <summary>SnapKinds を保存したときの種類の版。0 = 「頂点」が無かった頃 (ポリラインの頂点は端点に含まれていた)。</summary>
    public int SnapKindsVersion { get; set; }

    /// <summary>範囲選択の方法 ("Inside" = 全体選択 / "Crossing" = 一部選択)。</summary>
    public string? BoxSelectMode { get; set; }

    /// <summary>下部のメッセージ欄の高さ (px)。null なら既定。</summary>
    public double? MessageLogHeight { get; set; }

    /// <summary>新しい図面 (系の入っていない図面) に使う平面直角座標系の系番号。</summary>
    public int? DefaultPlaneZone { get; set; }

    /// <summary>プロパティパネルのフロート状態と位置。</summary>
    public PanelState PropertiesPanel { get; set; } = new();

    /// <summary>レイヤパネルのフロート状態と位置。</summary>
    public PanelState LayerPanel { get; set; } = new();

    /// <summary>座標読込の前回の設定。</summary>
    public CivilCad.App.IO.PointImportOptions PointImport { get; set; } = new();

    /// <summary>図枠ダイアログの前回の入力。</summary>
    public DrawingFrameOptions FrameDefaults { get; set; } = new();

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        var settings = new AppSettings();
        try
        {
            if (File.Exists(path))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 壊れた設定ファイルは無視して初期値で起動する
        }
        settings.KeyBindings ??= [];
        settings.FrameDefaults ??= new();
        settings.PointImport ??= new();
        settings.PropertiesPanel ??= new();
        settings.LayerPanel ??= new();
        // 後から増えたコマンドには初期値を入れる (ユーザーが消したものは "" で残っている)
        foreach (var (code, key) in CommandRegistry.DefaultBindings)
            settings.KeyBindings.TryAdd(code, key);
        return settings;
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>キー → コマンド名 の表 (不正な値・重複は無視)。</summary>
    public Dictionary<KeyChord, string> BuildKeyMap()
    {
        var map = new Dictionary<KeyChord, string>();
        foreach (var (code, text) in KeyBindings)
        {
            if (CommandRegistry.Find(code) == null || !KeyChord.TryParse(text, out var chord) || chord.IsReserved) continue;
            map.TryAdd(chord, code);
        }
        return map;
    }

    public string? KeyFor(string code) =>
        KeyBindings.TryGetValue(code, out var k) && KeyChord.TryParse(k, out var chord) ? chord.ToString() : null;
}
