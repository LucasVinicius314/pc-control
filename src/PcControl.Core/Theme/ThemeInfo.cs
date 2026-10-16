using System.Globalization;

namespace PcControl.Core.Theme;

/// <summary>The PC's colour theme as far as the phone page cares: the accent colour and dark or light mode.</summary>
public sealed record ThemeInfo(string Accent, bool Dark)
{
    /// <summary>What the page shows when the PC's theme cannot be read.</summary>
    public static ThemeInfo Default { get; } = new("#5aa9ff", true);

    /// <summary>"#rrggbb", always lower case, so the phone has one format to parse.</summary>
    public static string FormatHex(byte red, byte green, byte blue) =>
        string.Create(CultureInfo.InvariantCulture, $"#{red:x2}{green:x2}{blue:x2}");
}

public interface IThemeProvider
{
    /// <summary>Reads the theme fresh each time (the user can change it any moment). Never throws.</summary>
    ThemeInfo Read();
}
