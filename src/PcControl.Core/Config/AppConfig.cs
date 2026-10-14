namespace PcControl.Core.Config;

/// <summary>User-editable settings, stored as JSON in the user's AppData folder.</summary>
public sealed record AppConfig
{
    public const int DefaultPort = 48123;

    public int Port { get; init; } = DefaultPort;

    /// <summary>Also accept Tailscale's 100.64.0.0/10 range (off by default; LAN only).</summary>
    public bool AllowTailscaleRange { get; init; }

    public FeatureToggles Features { get; init; } = new();

    public PairingConfig Pairing { get; init; } = new();
}

/// <summary>Switches for the risky feature groups. A disabled group is refused by the server.</summary>
public sealed record FeatureToggles
{
    public bool Power { get; init; } = true;

    public bool ProcessKiller { get; init; } = true;

    public bool UrlOpen { get; init; } = true;

    public bool Screenshot { get; init; } = true;
}

public sealed record PairingConfig
{
    public int PinLifetimeMinutes { get; init; } = 5;

    /// <summary>Wrong guesses allowed against one PIN before it is thrown away.</summary>
    public int MaxFailuresPerPin { get; init; } = 5;

    /// <summary>Failed attempts per client IP, inside <see cref="AttemptWindowMinutes"/>, before lockout.</summary>
    public int MaxFailedAttempts { get; init; } = 5;

    public int AttemptWindowMinutes { get; init; } = 10;

    public int LockoutMinutes { get; init; } = 15;
}
