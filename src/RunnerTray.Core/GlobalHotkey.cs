using System.Globalization;

namespace RunnerTray;

/// <summary>Modifier keys of a global shortcut, with the values RegisterHotKey uses.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// A global shortcut such as "Win+Shift+R": one or more modifiers and one key (A-Z, 0-9 or F1-F24).
/// VirtualKey is the Windows virtual-key code that RegisterHotKey needs.
/// </summary>
public readonly record struct GlobalHotkey(HotkeyModifiers Modifiers, int VirtualKey)
{
    public const string Default = "Win+Shift+R";

    private const int F1 = 0x70;

    /// <summary>Reads text like "Win+Shift+R" or "ctrl + alt + f12". Case and spaces do not matter.</summary>
    public static bool TryParse(string? text, out GlobalHotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        var key = 0;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "WIN" or "WINDOWS":
                    modifiers |= HotkeyModifiers.Win;
                    continue;
                case "CTRL" or "CONTROL":
                    modifiers |= HotkeyModifiers.Control;
                    continue;
                case "ALT":
                    modifiers |= HotkeyModifiers.Alt;
                    continue;
                case "SHIFT":
                    modifiers |= HotkeyModifiers.Shift;
                    continue;
            }

            if (key != 0)
            {
                return false; // more than one key
            }

            key = KeyCode(part.ToUpperInvariant());
            if (key == 0)
            {
                return false;
            }
        }

        if (modifiers == HotkeyModifiers.None || key == 0)
        {
            return false;
        }

        hotkey = new GlobalHotkey(modifiers, key);
        return true;
    }

    /// <summary>The shortcut as people write it, modifiers first: "Win+Ctrl+Alt+Shift+Key".</summary>
    public override string ToString()
    {
        var parts = new List<string>(5);
        if ((Modifiers & HotkeyModifiers.Win) != 0) parts.Add("Win");
        if ((Modifiers & HotkeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & HotkeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((Modifiers & HotkeyModifiers.Shift) != 0) parts.Add("Shift");
        parts.Add(VirtualKey >= F1 ? "F" + (VirtualKey - F1 + 1).ToString(CultureInfo.InvariantCulture) : ((char)VirtualKey).ToString());
        return string.Join('+', parts);
    }

    /// <summary>Virtual-key code of A-Z, 0-9 or F1-F24 (upper case), or 0.</summary>
    private static int KeyCode(string key)
    {
        if (key.Length == 1 && key[0] is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
        {
            return key[0];
        }

        if (key.Length is 2 or 3 && key[0] == 'F'
            && int.TryParse(key.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 24)
        {
            return F1 + n - 1;
        }

        return 0;
    }
}
