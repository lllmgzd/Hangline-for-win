using System;

namespace Hangline;

/// 全局快捷键的读写：配置里写 "Ctrl+Alt+T" 这种形式。
internal static class HotkeySpec
{
    public static bool TryParse(string? text, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var raw in text!.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Trim();
            if (part.Length == 0) continue;

            if (Is(part, "Ctrl", "Control")) modifiers |= Native.MOD_CONTROL;
            else if (Is(part, "Alt")) modifiers |= Native.MOD_ALT;
            else if (Is(part, "Shift")) modifiers |= Native.MOD_SHIFT;
            else if (Is(part, "Win", "Windows")) modifiers |= Native.MOD_WIN;
            else if (!TryKey(part, out virtualKey)) return false;
        }

        return virtualKey != 0 && modifiers != 0;
    }

    public static string Describe(uint modifiers, uint virtualKey)
    {
        var text = string.Empty;
        if ((modifiers & Native.MOD_CONTROL) != 0) text += "Ctrl+";
        if ((modifiers & Native.MOD_ALT) != 0) text += "Alt+";
        if ((modifiers & Native.MOD_SHIFT) != 0) text += "Shift+";
        if ((modifiers & Native.MOD_WIN) != 0) text += "Win+";
        return text + KeyName(virtualKey);
    }

    private static bool TryKey(string name, out uint virtualKey)
    {
        virtualKey = 0;
        if (name.Length == 1)
        {
            var c = char.ToUpperInvariant(name[0]);
            if (c >= 'A' && c <= 'Z') { virtualKey = c; return true; }
            if (c >= '0' && c <= '9') { virtualKey = c; return true; }
            return false;
        }

        if (name.Length is 2 or 3 && (name[0] == 'F' || name[0] == 'f')
            && int.TryParse(name.AsSpan(1), out var number)
            && number >= 1 && number <= 24)
        {
            virtualKey = (uint)(0x70 + number - 1);
            return true;
        }

        return false;
    }

    private static string KeyName(uint virtualKey)
    {
        if (virtualKey is >= 'A' and <= 'Z') return ((char)virtualKey).ToString();
        if (virtualKey is >= '0' and <= '9') return ((char)virtualKey).ToString();
        if (virtualKey is >= 0x70 and <= 0x87) return "F" + (virtualKey - 0x70 + 1);
        return "?";
    }

    private static bool Is(string value, params string[] names)
    {
        foreach (var name in names)
        {
            if (string.Equals(value, name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
