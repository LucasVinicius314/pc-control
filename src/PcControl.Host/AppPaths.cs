namespace PcControl.Host;

/// <summary>Where the app keeps its files: %APPDATA%\PcControl.</summary>
internal static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PcControl");

    public static string LogsDirectory { get; } = Path.Combine(Root, "logs");

    public static string ConfigFile { get; } = Path.Combine(Root, "config.json");

    public static string DevicesFile { get; } = Path.Combine(Root, "devices.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDirectory);
    }
}
