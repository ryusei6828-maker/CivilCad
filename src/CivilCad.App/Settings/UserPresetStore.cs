using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Settings;

/// <summary>登録したプリセット。%APPDATA%\CivilCad\presets.json に保存し、起動時に読み込む。</summary>
public static class UserPresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(), new Vec2Converter() },
    };

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CivilCad", "presets.json");

    /// <summary>読み書きするファイル (テストでは一時ファイルに替える)。</summary>
    public static string FilePath { get; set; } = DefaultPath;

    /// <summary>保存ファイルから登録プリセットを読み、SymbolLibrary に入れる。壊れたファイルは無視する。</summary>
    public static void Load(string? path = null)
    {
        path ??= FilePath;
        try
        {
            if (!File.Exists(path)) return;
            var defs = JsonSerializer.Deserialize<List<SymbolDefinition>>(File.ReadAllText(path), JsonOptions);
            if (defs != null) SymbolLibrary.SetUser(defs.Where(d => !string.IsNullOrEmpty(d.Kind) && d.Shapes != null));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            // 読めなければ登録プリセットなしで起動する
        }
    }

    public static void Save(string? path = null)
    {
        path ??= FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(SymbolLibrary.User, JsonOptions));
    }

    /// <summary>座標は [x, y] で書く (Vec2 の計算用プロパティまで書かないように)。</summary>
    private sealed class Vec2Converter : JsonConverter<Vec2>
    {
        public override Vec2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var v = JsonSerializer.Deserialize<double[]>(ref reader) ?? [];
            if (v.Length < 2) throw new JsonException("座標は [x, y] で書いてください");
            return new Vec2(v[0], v[1]);
        }

        public override void Write(Utf8JsonWriter writer, Vec2 value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y);
            writer.WriteEndArray();
        }
    }
}
