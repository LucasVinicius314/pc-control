using PcControl.Core.Config;

namespace PcControl.Core.Input;

/// <summary>The shortcut to press, and whether it was taken from PowerToys' own settings.</summary>
public sealed record ResolvedShortcut(Shortcut Shortcut, bool FromPowerToys);

/// <summary>
/// Works out which key combination opens PowerToys Run. Set explicitly in the config it is used as
/// written. With "auto" (the default) it is read from PowerToys' settings each time, so changing the
/// shortcut in PowerToys takes effect without restarting this app, with Alt+Space (PowerToys' own
/// default) as the fallback.
/// </summary>
public sealed class PowerToysShortcutResolver(LauncherConfig config, Func<string?> readPowerToysSettings)
{
    /// <summary>Alt+Space, the shortcut PowerToys Run ships with. Matches <see cref="PowerToysSettings.DefaultShortcut"/>.</summary>
    private static readonly Shortcut DefaultShortcut = new(["AltLeft"], "Space");

    public ResolvedShortcut Resolve()
    {
        var configured = config.PowerToysRunShortcut?.Trim();
        if (!string.Equals(configured, "auto", StringComparison.OrdinalIgnoreCase)
            && Shortcut.TryParse(configured, out var explicitShortcut))
        {
            return new ResolvedShortcut(explicitShortcut!, FromPowerToys: false);
        }

        var json = SafeRead();
        if (json is not null && PowerToysSettings.TryReadShortcut(json, out var detected))
        {
            return new ResolvedShortcut(detected!, FromPowerToys: true);
        }

        return new ResolvedShortcut(DefaultShortcut, FromPowerToys: false);
    }

    private string? SafeRead()
    {
        try
        {
            return readPowerToysSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
