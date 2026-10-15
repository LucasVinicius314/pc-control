using System.Text.Json;

namespace PcControl.Core.Input;

/// <summary>A key combination such as Win+Shift+Space: some modifiers held while one key is pressed.</summary>
public sealed record Shortcut(IReadOnlyList<string> Modifiers, string Key)
{
    /// <summary>Reads text like "Alt+Space", "Ctrl+Shift+P" or "Win+Shift+Space". Names are not case sensitive.</summary>
    public static bool TryParse(string? text, out Shortcut? shortcut)
    {
        shortcut = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 1 || parts.Any(p => p.Length == 0))
        {
            return false;
        }

        var modifiers = new List<string>();
        foreach (var part in parts[..^1])
        {
            if (ModifierCode(part) is not { } modifier || modifiers.Contains(modifier))
            {
                return false;
            }

            modifiers.Add(modifier);
        }

        if (KeyCode(parts[^1]) is not { } key || KeyCatalog.IsModifier(key))
        {
            return false;
        }

        shortcut = new Shortcut(modifiers, key);
        return true;
    }

    /// <summary>The shortcut as text, in a fixed order: Ctrl, Alt, Shift, Win, then the key.</summary>
    public override string ToString()
    {
        var order = new[] { "ControlLeft", "AltLeft", "ShiftLeft", "MetaLeft" };
        var names = order.Where(Modifiers.Contains).Select(DisplayName).Append(DisplayName(Key));
        return string.Join('+', names);
    }

    /// <summary>Presses the modifiers, taps the key, then releases the modifiers in reverse.</summary>
    public void Send(IInputInjector injector)
    {
        ArgumentNullException.ThrowIfNull(injector);

        var modifiers = Modifiers.Select(m => KeyCatalog.TryGet(m, out var k) ? k : (KeyInfo?)null).OfType<KeyInfo>().ToList();
        if (!KeyCatalog.TryGet(Key, out var key))
        {
            return;
        }

        try
        {
            foreach (var modifier in modifiers)
            {
                injector.Key(modifier, down: true);
            }

            injector.Key(key, down: true);
            injector.Key(key, down: false);
        }
        finally
        {
            // Never leave a modifier held down, even if something above failed.
            foreach (var modifier in Enumerable.Reverse(modifiers))
            {
                injector.Key(modifier, down: false);
            }
        }
    }

    private static string? ModifierCode(string name) => name.ToLowerInvariant() switch
    {
        "ctrl" or "control" => "ControlLeft",
        "alt" => "AltLeft",
        "shift" => "ShiftLeft",
        "win" or "windows" or "meta" or "super" or "cmd" => "MetaLeft",
        _ => null,
    };

    private static string? KeyCode(string name)
    {
        if (name.Length == 1)
        {
            var c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z')
            {
                return "Key" + c;
            }

            if (c is >= '0' and <= '9')
            {
                return "Digit" + c;
            }
        }

        var alias = name.ToLowerInvariant() switch
        {
            "esc" => "Escape",
            "return" => "Enter",
            "del" => "Delete",
            "ins" => "Insert",
            "pgup" => "PageUp",
            "pgdn" => "PageDown",
            "up" => "ArrowUp",
            "down" => "ArrowDown",
            "left" => "ArrowLeft",
            "right" => "ArrowRight",
            _ => name,
        };

        // Match the catalog's own spelling regardless of case ("space" -> "Space", "f5" -> "F5").
        return KeyCatalog.Names.FirstOrDefault(n => string.Equals(n, alias, StringComparison.OrdinalIgnoreCase));
    }

    private static string DisplayName(string code) => code switch
    {
        "ControlLeft" => "Ctrl",
        "AltLeft" => "Alt",
        "ShiftLeft" => "Shift",
        "MetaLeft" => "Win",
        _ when code.StartsWith("Key", StringComparison.Ordinal) && code.Length == 4 => code[3..],
        _ when code.StartsWith("Digit", StringComparison.Ordinal) => code[5..],
        _ => code,
    };
}

/// <summary>Reads the Run shortcut out of PowerToys' own settings, so the button follows what the user chose there.</summary>
public static class PowerToysSettings
{
    /// <summary>What PowerToys Run uses out of the box, and the fallback when its settings cannot be read.</summary>
    public const string DefaultShortcut = "Alt+Space";

    /// <summary>
    /// Reads <c>properties.open_powerlauncher</c> from PowerToys Run's settings.json:
    /// <c>{"win":true,"ctrl":false,"alt":false,"shift":true,"code":32}</c>, where <c>code</c> is a
    /// Windows virtual-key code.
    /// </summary>
    public static bool TryReadShortcut(string json, out Shortcut? shortcut)
    {
        shortcut = null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("properties", out var properties)
                || !properties.TryGetProperty("open_powerlauncher", out var hotkey)
                || hotkey.ValueKind != JsonValueKind.Object
                || !hotkey.TryGetProperty("code", out var code)
                || code.ValueKind != JsonValueKind.Number
                || !code.TryGetInt32(out var virtualKey)
                || virtualKey is <= 0 or > 255
                || !KeyCatalog.TryGetByVirtualKey((ushort)virtualKey, out var keyName))
            {
                return false;
            }

            var modifiers = new List<string>();
            AddIf(hotkey, "ctrl", "ControlLeft", modifiers);
            AddIf(hotkey, "alt", "AltLeft", modifiers);
            AddIf(hotkey, "shift", "ShiftLeft", modifiers);
            AddIf(hotkey, "win", "MetaLeft", modifiers);

            if (KeyCatalog.IsModifier(keyName))
            {
                return false; // a modifier on its own is not a shortcut
            }

            shortcut = new Shortcut(modifiers, keyName);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void AddIf(JsonElement hotkey, string property, string code, List<string> into)
    {
        if (hotkey.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True)
        {
            into.Add(code);
        }
    }
}
