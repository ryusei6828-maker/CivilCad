using System.Windows.Input;

namespace CivilCad.App.Settings;

/// <summary>ショートカットキー (修飾キー + キー)。"Ctrl+Shift+L" / "F2" / "E" のような文字列で保存する。</summary>
public readonly record struct KeyChord(Key Key, ModifierKeys Modifiers)
{
    private static readonly HashSet<Key> ModifierOnlyKeys =
    [
        Key.LeftCtrl, Key.RightCtrl, Key.LeftShift, Key.RightShift, Key.LeftAlt, Key.RightAlt,
        Key.LWin, Key.RWin, Key.System, Key.ImeProcessed, Key.DeadCharProcessed, Key.None,
    ];

    /// <summary>
    /// 修飾キーなし (または Shift のみ) の文字キー。コマンド欄への入力とぶつかるため、
    /// コマンド欄が空のときだけショートカットとして働く。
    /// </summary>
    public bool IsCharacterKey =>
        (Modifiers & ~ModifierKeys.Shift) == ModifierKeys.None &&
        (Key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.Divide
             or >= Key.Oem1 and <= Key.Oem102 or Key.Space);

    /// <summary>アプリ側で固定の役割を持つキー (割り当て不可)。</summary>
    public bool IsReserved =>
        Modifiers == ModifierKeys.None && Key is Key.Enter or Key.Escape or Key.Tab or Key.Back or Key.Delete ||
        Modifiers == ModifierKeys.Control && Key is Key.Z or Key.Y or Key.A or Key.C or Key.V or Key.X or Key.N or Key.O or Key.S ||
        Modifiers == ModifierKeys.Alt && Key == Key.F4;

    public static KeyChord? FromEvent(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (ModifierOnlyKeys.Contains(key)) return null;
        return new KeyChord(key, Keyboard.Modifiers & ~ModifierKeys.Windows);
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        var mods = ModifierKeys.None;
        foreach (var p in parts[..^1])
        {
            switch (p.ToUpperInvariant())
            {
                case "CTRL": mods |= ModifierKeys.Control; break;
                case "SHIFT": mods |= ModifierKeys.Shift; break;
                case "ALT": mods |= ModifierKeys.Alt; break;
                default: return false;
            }
        }
        if (!TryParseKey(parts[^1], out var key)) return false;
        chord = new KeyChord(key, mods);
        return true;
    }

    private static string KeyName(Key k) => k switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (k - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (k - Key.NumPad0),
        _ => k.ToString(),
    };

    private static bool TryParseKey(string s, out Key key)
    {
        key = Key.None;
        if (s.Length == 1 && char.IsDigit(s[0]))
        {
            key = Key.D0 + (s[0] - '0');
            return true;
        }
        if (s.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && s.Length == 4 && char.IsDigit(s[3]))
        {
            key = Key.NumPad0 + (s[3] - '0');
            return true;
        }
        return Enum.TryParse(s, true, out key) && key != Key.None && !int.TryParse(s, out _);
    }
}
