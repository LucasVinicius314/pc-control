namespace PcControl.Host;

/// <summary>
/// Where the app keeps its files: %APPDATA%\PcControl, or the folder in the PCCONTROL_DATA_DIR
/// environment variable when it is set (for tests and portable use).
/// </summary>
internal static class AppPaths
{
    public static string Root { get; } = ResolveRoot();

    public static string LogsDirectory { get; } = Path.Combine(Root, "logs");

    public static string ConfigFile { get; } = Path.Combine(Root, "config.json");

    public static string DevicesFile { get; } = Path.Combine(Root, "devices.json");

    /// <summary>Favorites and recently launched apps.</summary>
    public static string AppsFile { get; } = Path.Combine(Root, "apps.json");

    public static string GamesFile { get; } = Path.Combine(Root, "games.json");

    /// <summary>App icons already converted to PNG.</summary>
    public static string IconsDirectory { get; } = Path.Combine(Root, "icons");

    /// <summary>PowerToys keeps its Run settings here; the PowerToys Run button reads its shortcut from it.</summary>
    public static string PowerToysRunSettingsFile { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft",
        "PowerToys",
        "PowerToys Run",
        "settings.json");

    private static string ResolveRoot()
    {
        var custom = Environment.GetEnvironmentVariable("PCCONTROL_DATA_DIR");
        return string.IsNullOrWhiteSpace(custom)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PcControl")
            : Path.GetFullPath(custom);
    }

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDirectory);
    }
}
