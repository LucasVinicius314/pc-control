using PcControl.Core.Config;

namespace PcControl.Core.Protocol;

/// <summary>Which features a client may show. Every phone UI adapts to this list.</summary>
public static class Capabilities
{
    public static readonly IReadOnlyList<string> AllFeatures =
    [
        "trackpad", "keyboard", "appLauncher", "search", "powerToysRun", "dpiToggle",
        "magnifier", "screenshot", "gameLauncher", "gameMode", "media", "sendUrl",
        "power", "processKiller", "controllerOverlay",
    ];

    /// <summary>Features whose code exists in this build. Grows milestone by milestone.</summary>
    public static readonly IReadOnlySet<string> Implemented =
        new HashSet<string>(StringComparer.Ordinal) { "trackpad", "keyboard" };

    public static IReadOnlyDictionary<string, bool> Build(AppConfig config) =>
        Build(config, Implemented);

    public static IReadOnlyDictionary<string, bool> Build(AppConfig config, IReadOnlySet<string> implemented)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(implemented);

        return AllFeatures.ToDictionary(
            name => name,
            name => implemented.Contains(name) && IsToggledOn(config.Features, name),
            StringComparer.Ordinal);
    }

    private static bool IsToggledOn(FeatureToggles toggles, string name) => name switch
    {
        "power" => toggles.Power,
        "processKiller" => toggles.ProcessKiller,
        "sendUrl" => toggles.UrlOpen,
        "screenshot" => toggles.Screenshot,
        _ => true,
    };
}
