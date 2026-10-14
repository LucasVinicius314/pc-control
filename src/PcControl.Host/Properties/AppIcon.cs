namespace PcControl.Host.Properties;

internal static class AppIcon
{
    /// <summary>The tray and window icon, read once from the embedded app.ico.</summary>
    public static Icon Value { get; } = Load();

    private static Icon Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("app.ico")
            ?? throw new InvalidOperationException("app.ico is not embedded.");
        return new Icon(stream);
    }
}
