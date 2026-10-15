namespace PcControl.Core.Input;

/// <summary>
/// The keys a phone may press, named like the browser's <c>KeyboardEvent.code</c>. Anything not
/// listed here is refused, so the phone cannot send arbitrary virtual-key codes.
/// </summary>
public static class KeyCatalog
{
    private static readonly Dictionary<string, KeyInfo> Keys = Build();

    /// <summary>Every key name in the catalog.</summary>
    public static IEnumerable<string> Names => Keys.Keys;

    private static readonly Lazy<Dictionary<ushort, string>> ByVirtualKey = new(BuildReverse);

    /// <summary>The catalog name for a Windows virtual-key code, preferring the left-hand modifier keys.</summary>
    public static bool TryGetByVirtualKey(ushort virtualKey, out string code)
    {
        if (ByVirtualKey.Value.TryGetValue(virtualKey, out var name))
        {
            code = name;
            return true;
        }

        code = string.Empty;
        return false;
    }

    public static bool TryGet(string? code, out KeyInfo key)
    {
        if (code is not null && Keys.TryGetValue(code, out key))
        {
            return true;
        }

        key = default;
        return false;
    }

    public static bool IsModifier(string code) =>
        code.StartsWith("Control", StringComparison.Ordinal)
        || code.StartsWith("Shift", StringComparison.Ordinal)
        || code.StartsWith("Alt", StringComparison.Ordinal)
        || code.StartsWith("Meta", StringComparison.Ordinal);

    private static Dictionary<ushort, string> BuildReverse()
    {
        var reverse = new Dictionary<ushort, string>();
        foreach (var (name, key) in Keys.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            // Names like ShiftLeft sort before ShiftRight, so the left-hand key wins when both map to one code.
            reverse.TryAdd(key.VirtualKey, name);
        }

        // The generic modifier codes PowerToys and other tools use.
        reverse.TryAdd(0x10, "ShiftLeft");
        reverse.TryAdd(0x11, "ControlLeft");
        reverse.TryAdd(0x12, "AltLeft");
        return reverse;
    }

    private static Dictionary<string, KeyInfo> Build()
    {
        var map = new Dictionary<string, KeyInfo>(StringComparer.Ordinal)
        {
            ["Escape"] = new(0x1B, false),
            ["Tab"] = new(0x09, false),
            ["Enter"] = new(0x0D, false),
            ["Backspace"] = new(0x08, false),
            ["Space"] = new(0x20, false),
            ["Insert"] = new(0x2D, true),
            ["Delete"] = new(0x2E, true),
            ["Home"] = new(0x24, true),
            ["End"] = new(0x23, true),
            ["PageUp"] = new(0x21, true),
            ["PageDown"] = new(0x22, true),
            ["ArrowLeft"] = new(0x25, true),
            ["ArrowUp"] = new(0x26, true),
            ["ArrowRight"] = new(0x27, true),
            ["ArrowDown"] = new(0x28, true),
            ["ContextMenu"] = new(0x5D, true),
            ["ShiftLeft"] = new(0xA0, false),
            ["ShiftRight"] = new(0xA1, false),
            ["ControlLeft"] = new(0xA2, false),
            ["ControlRight"] = new(0xA3, true),
            ["AltLeft"] = new(0xA4, false),
            ["AltRight"] = new(0xA5, true),
            ["MetaLeft"] = new(0x5B, true),
            ["MetaRight"] = new(0x5C, true),
            ["Minus"] = new(0xBD, false),
            ["Equal"] = new(0xBB, false),
            ["BracketLeft"] = new(0xDB, false),
            ["BracketRight"] = new(0xDD, false),
            ["Semicolon"] = new(0xBA, false),
            ["Quote"] = new(0xDE, false),
            ["Backquote"] = new(0xC0, false),
            ["Backslash"] = new(0xDC, false),
            ["Comma"] = new(0xBC, false),
            ["Period"] = new(0xBE, false),
            ["Slash"] = new(0xBF, false),
        };

        for (var i = 0; i < 26; i++)
        {
            map["Key" + (char)('A' + i)] = new((ushort)('A' + i), false);
        }

        for (var i = 0; i < 10; i++)
        {
            map["Digit" + (char)('0' + i)] = new((ushort)('0' + i), false);
        }

        for (var i = 1; i <= 12; i++)
        {
            map["F" + i] = new((ushort)(0x70 + i - 1), false);
        }

        return map;
    }
}
