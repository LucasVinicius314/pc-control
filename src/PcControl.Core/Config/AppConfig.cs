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

    public InputConfig Input { get; init; } = new();

    public DpiConfig Dpi { get; init; } = new();

    public LauncherConfig Launcher { get; init; } = new();

    public CaptureConfig Capture { get; init; } = new();
}

public sealed record CaptureConfig
{
    /// <summary>
    /// How the screen is read for the previews: "auto" (the default) uses Desktop Duplication and falls
    /// back to the older GDI capture if that is not possible; "gdi" always uses the older method. The
    /// older method re-reads the whole screen every time and, on some systems, makes the mouse cursor
    /// flicker, so "gdi" is mainly for comparing.
    /// </summary>
    public string Method { get; init; } = "auto";
}

public sealed record LauncherConfig
{
    /// <summary>
    /// The key combination that opens PowerToys Run on the PC, for example "Win+Shift+Space". "auto"
    /// (the default) reads it from PowerToys' own settings and falls back to Alt+Space, PowerToys' default.
    /// </summary>
    public string PowerToysRunShortcut { get; init; } = "auto";

    /// <summary>How many recently launched apps are remembered.</summary>
    public int RecentCount { get; init; } = 20;
}

public sealed record DpiConfig
{
    /// <summary>
    /// Which monitor the toggle changes: "primary" (the default), "cursor" (the one the mouse is on),
    /// or a monitor number from 0. Monitors differ in the scales they allow, so the choice matters.
    /// </summary>
    public string Monitor { get; init; } = "primary";

    public int LowPercent { get; init; } = 100;

    public int HighPercent { get; init; } = 200;

    /// <summary>
    /// A script or program that flips the scale, used only if Windows' own display-scale calls stop
    /// working (they are not officially documented). Never run with anything from the phone.
    /// </summary>
    public string? FallbackScriptPath { get; init; }
}

public sealed record InputConfig
{
    /// <summary>Base cursor speed: PC pixels moved per phone pixel of finger movement at low speed.</summary>
    public double PointerGain { get; init; } = 2.0;
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
