using PcControl.Core.Games;
using PcControl.Windows;

namespace PcControl.Host;

/// <summary>What the desktop window shows about one emulator.</summary>
internal sealed record EmulatorStatus(string? ExePath, bool Automatic, string Setting, string? Problem, int GameCount, string? Warning);

/// <summary>
/// Lets the user see and set where an emulator (RPCS3, PCSX2) is from the desktop window. A change takes
/// effect at once (the game list is read again) and is written to <c>config.json</c>, so it is still there
/// next time and the file stays the one place all settings live.
/// </summary>
/// <param name="configKey">The name of the setting inside "games" in config.json, for example "rpcs3Path".</param>
internal sealed class EmulatorSettings(IEmulatorLocator locator, GamesService games, string configFile, string configKey)
{
    public const string Automatic = "auto";

    public string ExeName => locator.ExeName;

    public EmulatorStatus GetStatus()
    {
        var exe = locator.FindExe();
        var count = exe is null ? 0 : games.GetList().Games.Count(g => g.Source == locator.Source);
        return new EmulatorStatus(
            exe,
            locator.Setting.Equals(Automatic, StringComparison.OrdinalIgnoreCase),
            locator.Setting,
            exe is null ? NotFoundSentence() : null,
            count,
            exe is null ? null : locator.Warning);
    }

    /// <summary>Looks for the emulator again, for example after it was installed or moved.</summary>
    public EmulatorStatus Rescan()
    {
        locator.Rescan();
        games.Invalidate();
        return GetStatus();
    }

    /// <summary>
    /// Points the app at the emulator: the path of its program or of the folder it is in, or "auto" to find it
    /// by itself. Returns a sentence about what is wrong, or null on success; nothing changes on a problem.
    /// </summary>
    public string? Apply(string? setting)
    {
        var value = string.IsNullOrWhiteSpace(setting) ? Automatic : setting.Trim();
        if (value.Length > 260)
        {
            return "That path is too long.";
        }

        if (!value.Equals(Automatic, StringComparison.OrdinalIgnoreCase) && !locator.IsLocation(value))
        {
            return $"There is no {locator.ExeName} there. Pick {locator.ExeName}, or the folder it is in.";
        }

        ConfigLoader.SetGamesValue(configFile, configKey, value);
        locator.Configure(value);
        games.Invalidate();
        return null;
    }

    private string NotFoundSentence() =>
        locator.Setting.Equals(Automatic, StringComparison.OrdinalIgnoreCase)
            ? $"{locator.ExeName} was not found."
            : $"There is no {locator.ExeName} at {locator.Setting}.";
}
