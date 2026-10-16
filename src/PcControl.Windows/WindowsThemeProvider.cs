using Microsoft.Win32;
using PcControl.Core.Theme;

namespace PcControl.Windows;

/// <summary>Reads the accent colour and the dark / light choice from Windows' own settings.</summary>
public sealed class WindowsThemeProvider : IThemeProvider
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public ThemeInfo Read()
    {
        var accent = ThemeInfo.Default.Accent;
        try
        {
            var color = new global::Windows.UI.ViewManagement.UISettings().GetColorValue(global::Windows.UI.ViewManagement.UIColorType.Accent);
            accent = ThemeInfo.FormatHex(color.R, color.G, color.B);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or TypeInitializationException or PlatformNotSupportedException)
        {
            // Keep the default accent; the page still works.
        }

        return new ThemeInfo(accent, IsDarkApps());
    }

    private static bool IsDarkApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            // 0 means "dark"; a missing value means an older Windows, which was light.
            return key?.GetValue("AppsUseLightTheme") is int light ? light == 0 : ThemeInfo.Default.Dark;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return ThemeInfo.Default.Dark;
        }
    }
}
