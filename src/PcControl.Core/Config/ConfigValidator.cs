namespace PcControl.Core.Config;

public static class ConfigValidator
{
    /// <summary>Returns a human-readable message per problem; empty when the config is valid.</summary>
    public static IReadOnlyList<string> Validate(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();

        if (config.Port is < 1024 or > 65535)
        {
            errors.Add($"port must be between 1024 and 65535 (was {config.Port}).");
        }

        var pairing = config.Pairing;
        if (pairing.PinLifetimeMinutes is < 1 or > 30)
        {
            errors.Add($"pairing.pinLifetimeMinutes must be between 1 and 30 (was {pairing.PinLifetimeMinutes}).");
        }

        if (pairing.MaxFailuresPerPin is < 1 or > 20)
        {
            errors.Add($"pairing.maxFailuresPerPin must be between 1 and 20 (was {pairing.MaxFailuresPerPin}).");
        }

        if (pairing.MaxFailedAttempts is < 1 or > 20)
        {
            errors.Add($"pairing.maxFailedAttempts must be between 1 and 20 (was {pairing.MaxFailedAttempts}).");
        }

        if (pairing.AttemptWindowMinutes is < 1 or > 120)
        {
            errors.Add($"pairing.attemptWindowMinutes must be between 1 and 120 (was {pairing.AttemptWindowMinutes}).");
        }

        if (pairing.LockoutMinutes is < 1 or > 1440)
        {
            errors.Add($"pairing.lockoutMinutes must be between 1 and 1440 (was {pairing.LockoutMinutes}).");
        }

        if (double.IsNaN(config.Input.PointerGain) || config.Input.PointerGain is < 0.1 or > 20)
        {
            errors.Add($"input.pointerGain must be between 0.1 and 20 (was {config.Input.PointerGain}).");
        }

        if (config.Capture.Method?.Trim().ToLowerInvariant() is not ("auto" or "gdi"))
        {
            errors.Add($"capture.method must be \"auto\" or \"gdi\" (was \"{config.Capture.Method}\").");
        }

        var launcher = config.Launcher;
        if (!string.Equals(launcher.PowerToysRunShortcut?.Trim(), "auto", StringComparison.OrdinalIgnoreCase)
            && !Input.Shortcut.TryParse(launcher.PowerToysRunShortcut, out _))
        {
            errors.Add($"launcher.powerToysRunShortcut must be \"auto\" or a key combination such as \"Alt+Space\" or \"Win+Shift+Space\" (was \"{launcher.PowerToysRunShortcut}\").");
        }

        if (launcher.RecentCount is < 1 or > Apps.AppsService.MaxRecent)
        {
            errors.Add($"launcher.recentCount must be between 1 and {Apps.AppsService.MaxRecent} (was {launcher.RecentCount}).");
        }

        var dpi = config.Dpi;
        if (!Display.DpiScaleTable.IsKnownScale(dpi.LowPercent) || !Display.DpiScaleTable.IsKnownScale(dpi.HighPercent))
        {
            errors.Add($"dpi.lowPercent and dpi.highPercent must each be one of {string.Join(", ", Display.DpiScaleTable.Percents)} (were {dpi.LowPercent} and {dpi.HighPercent}).");
        }
        else if (dpi.LowPercent >= dpi.HighPercent)
        {
            errors.Add($"dpi.lowPercent must be smaller than dpi.highPercent (were {dpi.LowPercent} and {dpi.HighPercent}).");
        }

        var monitor = dpi.Monitor?.Trim().ToLowerInvariant();
        if (monitor is not ("cursor" or "primary") && !(int.TryParse(monitor, System.Globalization.CultureInfo.InvariantCulture, out var index) && index is >= 0 and <= 15))
        {
            errors.Add($"dpi.monitor must be \"cursor\", \"primary\" or a monitor number from 0 to 15 (was \"{dpi.Monitor}\").");
        }

        if (dpi.FallbackScriptPath is { } script && (string.IsNullOrWhiteSpace(script) || script.Length > 260))
        {
            errors.Add("dpi.fallbackScriptPath must be a file path of at most 260 characters, or left out.");
        }

        return errors;
    }
}
