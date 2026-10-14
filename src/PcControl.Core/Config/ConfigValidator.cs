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

        return errors;
    }
}
