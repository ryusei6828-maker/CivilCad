using System.IO;
using System.Windows.Input;
using CivilCad.App.Settings;

namespace CivilCad.Tests;

public class SettingsTests
{
    [Theory]
    [InlineData(Key.E, ModifierKeys.None, "E")]
    [InlineData(Key.L, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+L")]
    [InlineData(Key.F2, ModifierKeys.None, "F2")]
    [InlineData(Key.D5, ModifierKeys.Alt, "Alt+5")]
    [InlineData(Key.NumPad3, ModifierKeys.None, "Num3")]
    public void KeyChord_FormatAndParse(Key key, ModifierKeys mods, string text)
    {
        var chord = new KeyChord(key, mods);
        Assert.Equal(text, chord.ToString());
        Assert.True(KeyChord.TryParse(text, out var parsed));
        Assert.Equal(chord, parsed);
    }

    [Fact]
    public void KeyChord_Classification()
    {
        Assert.True(new KeyChord(Key.E, ModifierKeys.None).IsCharacterKey);
        Assert.True(new KeyChord(Key.E, ModifierKeys.Shift).IsCharacterKey);
        Assert.False(new KeyChord(Key.E, ModifierKeys.Control).IsCharacterKey);
        Assert.False(new KeyChord(Key.F5, ModifierKeys.None).IsCharacterKey);

        Assert.True(new KeyChord(Key.Enter, ModifierKeys.None).IsReserved);
        Assert.True(new KeyChord(Key.Z, ModifierKeys.Control).IsReserved);
        Assert.False(new KeyChord(Key.Z, ModifierKeys.None).IsReserved);
        Assert.False(KeyChord.TryParse("Hyper+Q", out _));
        Assert.False(KeyChord.TryParse("", out _));
    }

    [Fact]
    public void AppSettings_SaveLoad_AndKeyMap()
    {
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_settings_{Guid.NewGuid():N}.json");
        try
        {
            // ファイルが無ければ初期値 (F3 / F8)
            var s = AppSettings.Load(path);
            Assert.Equal("F3", s.KeyFor("SNAP"));

            s.KeyBindings["L"] = "Q";
            s.KeyBindings["E"] = "Ctrl+Shift+D";
            s.KeyBindings["SNAP"] = "";          // ユーザーが解除
            s.KeyBindings["M"] = "Ctrl+Z";       // 予約キーは無視される
            s.Save(path);

            var loaded = AppSettings.Load(path);
            var map = loaded.BuildKeyMap();
            Assert.Equal("L", map[new KeyChord(Key.Q, ModifierKeys.None)]);
            Assert.Equal("E", map[new KeyChord(Key.D, ModifierKeys.Control | ModifierKeys.Shift)]);
            Assert.Null(loaded.KeyFor("SNAP"));    // 解除したものは初期値で復活しない
            Assert.Equal("F8", loaded.KeyFor("ORTHO"));
            Assert.DoesNotContain("M", map.Values);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AppSettings_BrokenFile_FallsBackToDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_settings_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ これは壊れた JSON");
        try
        {
            Assert.Equal("F3", AppSettings.Load(path).KeyFor("SNAP"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
