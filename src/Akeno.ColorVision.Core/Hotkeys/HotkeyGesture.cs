using System.Diagnostics.CodeAnalysis;

namespace Akeno.ColorVision.Core.Hotkeys;

/// <summary>Modifier flags. Values match the Win32 <c>MOD_*</c> constants used by RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
}

/// <summary>A global keyboard shortcut such as "Ctrl + Alt + Shift + F".</summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    /// <summary>Win32 virtual-key code for <see cref="Key"/>.</summary>
    public int VirtualKey => KeyTable.TryGetValue(Key, out var vk) ? vk : 0;

    public static IReadOnlyDictionary<string, int> KeyTable { get; } = BuildKeyTable();

    public static bool TryParse(string? text, [NotNullWhen(true)] out HotkeyGesture? gesture, out string? error)
    {
        gesture = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "No shortcut entered.";
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        string? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                case "strg":
                    modifiers |= HotkeyModifiers.Control;
                    continue;
                case "alt":
                    modifiers |= HotkeyModifiers.Alt;
                    continue;
                case "shift":
                    modifiers |= HotkeyModifiers.Shift;
                    continue;
                case "win":
                case "windows":
                case "meta":
                    modifiers |= HotkeyModifiers.Windows;
                    continue;
            }

            if (key is not null)
            {
                error = "A shortcut can only contain one non-modifier key.";
                return false;
            }

            var normalized = NormalizeKeyName(raw);
            if (normalized is null)
            {
                error = $"'{raw}' is not a supported key.";
                return false;
            }

            key = normalized;
        }

        if (key is null)
        {
            error = "The shortcut needs a key in addition to the modifiers (for example Ctrl + Alt + F).";
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    /// <summary>
    /// Checks that the shortcut is safe to register globally: it must include Ctrl, Alt or Win (so it
    /// does not swallow normal typing) and must not collide with reserved Windows shortcuts.
    /// </summary>
    public string? Validate()
    {
        if (VirtualKey == 0)
        {
            return $"'{Key}' is not a supported key.";
        }

        if ((Modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Windows)) == 0)
        {
            return "Use at least one of Ctrl, Alt or Win so the shortcut does not interfere with typing.";
        }

        var reserved = Reserved.FirstOrDefault(r => r.Modifiers == Modifiers && string.Equals(r.Key, Key, StringComparison.OrdinalIgnoreCase));
        if (reserved is not null)
        {
            return $"{this} is reserved by Windows.";
        }

        if ((Modifiers & HotkeyModifiers.Windows) != 0 && Modifiers == HotkeyModifiers.Windows)
        {
            return "Win + single key shortcuts are mostly reserved by Windows; add Ctrl, Alt or Shift.";
        }

        return null;
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(Key);
        return string.Join(" + ", parts);
    }

    private static readonly HotkeyGesture[] Reserved =
    [
        new(HotkeyModifiers.Control | HotkeyModifiers.Alt, "Delete"),
        new(HotkeyModifiers.Alt, "Tab"),
        new(HotkeyModifiers.Alt, "F4"),
        new(HotkeyModifiers.Alt, "Escape"),
        new(HotkeyModifiers.Control, "Escape"),
        new(HotkeyModifiers.Control | HotkeyModifiers.Shift, "Escape"),
        new(HotkeyModifiers.Windows, "L"),
        new(HotkeyModifiers.Windows | HotkeyModifiers.Control, "C"), // Windows color filters toggle
        new(HotkeyModifiers.Windows | HotkeyModifiers.Control, "D"),
        new(HotkeyModifiers.Windows | HotkeyModifiers.Control, "F4"),
        new(HotkeyModifiers.Windows | HotkeyModifiers.Control, "Left"),
        new(HotkeyModifiers.Windows | HotkeyModifiers.Control, "Right"),
        new(HotkeyModifiers.Windows | HotkeyModifiers.Shift, "S"),
        new(HotkeyModifiers.Windows | HotkeyModifiers.Alt, "R"),
        new(HotkeyModifiers.Windows | HotkeyModifiers.Alt, "G"),
        new(HotkeyModifiers.Control | HotkeyModifiers.Alt, "I"), // Windows Magnifier: invert colors
        new(HotkeyModifiers.Control | HotkeyModifiers.Alt, "F"), // Windows Magnifier: full-screen view
        new(HotkeyModifiers.Control | HotkeyModifiers.Alt, "L"), // Windows Magnifier: lens view
        new(HotkeyModifiers.Control | HotkeyModifiers.Alt, "D"), // Windows Magnifier: docked view
        new(HotkeyModifiers.Control | HotkeyModifiers.Alt, "M"), // Windows Magnifier: cycle views
        new(HotkeyModifiers.Control | HotkeyModifiers.Alt, "R"), // Windows Magnifier: resize lens
    ];

    private static string? NormalizeKeyName(string raw)
    {
        foreach (var name in KeyTable.Keys)
        {
            if (string.Equals(name, raw, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return raw.ToLowerInvariant() switch
        {
            "esc" => "Escape",
            "del" => "Delete",
            "ins" => "Insert",
            "back" or "bksp" => "Backspace",
            "return" => "Enter",
            "pgup" => "PageUp",
            "pgdn" => "PageDown",
            _ => null,
        };
    }

    private static Dictionary<string, int> BuildKeyTable()
    {
        var table = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var c = 'A'; c <= 'Z'; c++)
        {
            table[c.ToString()] = c;
        }

        for (var d = 0; d <= 9; d++)
        {
            table[d.ToString(System.Globalization.CultureInfo.InvariantCulture)] = 0x30 + d;
        }

        for (var f = 1; f <= 24; f++)
        {
            table["F" + f.ToString(System.Globalization.CultureInfo.InvariantCulture)] = 0x6F + f;
        }

        for (var n = 0; n <= 9; n++)
        {
            table["NumPad" + n.ToString(System.Globalization.CultureInfo.InvariantCulture)] = 0x60 + n;
        }

        table["Backspace"] = 0x08;
        table["Tab"] = 0x09;
        table["Enter"] = 0x0D;
        table["Pause"] = 0x13;
        table["Escape"] = 0x1B;
        table["Space"] = 0x20;
        table["PageUp"] = 0x21;
        table["PageDown"] = 0x22;
        table["End"] = 0x23;
        table["Home"] = 0x24;
        table["Left"] = 0x25;
        table["Up"] = 0x26;
        table["Right"] = 0x27;
        table["Down"] = 0x28;
        table["Insert"] = 0x2D;
        table["Delete"] = 0x2E;
        table["Multiply"] = 0x6A;
        table["Add"] = 0x6B;
        table["Subtract"] = 0x6D;
        table["Decimal"] = 0x6E;
        table["Divide"] = 0x6F;
        return table;
    }
}

/// <summary>The configurable global shortcuts and their safe defaults.</summary>
public sealed class HotkeySettings
{
    public const string DefaultToggleFilter = "Ctrl + Alt + Shift + F";
    public const string DefaultEmergencyReset = "Ctrl + Alt + Shift + Backspace";
    public const string DefaultIdentifyColor = "Ctrl + Alt + Shift + C";

    public string ToggleFilter { get; set; } = DefaultToggleFilter;

    public string EmergencyReset { get; set; } = DefaultEmergencyReset;

    public string IdentifyColor { get; set; } = DefaultIdentifyColor;

    /// <summary>Validates all shortcuts, falling back to defaults for invalid or duplicate entries.</summary>
    public IReadOnlyList<string> Normalize()
    {
        var notes = new List<string>();
        ToggleFilter = Fix(ToggleFilter, DefaultToggleFilter, "Toggle filter", notes);
        EmergencyReset = Fix(EmergencyReset, DefaultEmergencyReset, "Emergency reset", notes);
        IdentifyColor = Fix(IdentifyColor, DefaultIdentifyColor, "Identify color", notes);

        if (Same(EmergencyReset, ToggleFilter) || Same(EmergencyReset, IdentifyColor))
        {
            EmergencyReset = DefaultEmergencyReset;
            notes.Add("Emergency reset shortcut duplicated another shortcut; restored the default.");
        }

        if (Same(ToggleFilter, IdentifyColor))
        {
            IdentifyColor = DefaultIdentifyColor;
            notes.Add("Identify color shortcut duplicated the toggle shortcut; restored the default.");
        }

        return notes;
    }

    private static bool Same(string a, string b) =>
        HotkeyGesture.TryParse(a, out var ga, out _) && HotkeyGesture.TryParse(b, out var gb, out _) && ga == gb;

    private static string Fix(string? value, string fallback, string label, List<string> notes)
    {
        if (HotkeyGesture.TryParse(value, out var gesture, out _) && gesture.Validate() is null)
        {
            return gesture.ToString();
        }

        notes.Add($"{label} shortcut '{value}' was invalid; restored the default ({fallback}).");
        return fallback;
    }
}
