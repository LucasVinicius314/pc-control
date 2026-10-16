using System.Text.Json;
using PcControl.Core.Games;
using Serilog;

namespace PcControl.Host;

/// <summary>Keeps the recently played games in a JSON file, written atomically.</summary>
internal sealed class JsonGamesStateStore(string path) : IGamesStateStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private sealed record FileShape(List<string>? Recent);

    public IReadOnlyList<string> LoadRecent()
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var shape = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(path), Options);
            return [.. (shape?.Recent ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal)];
        }
        catch (JsonException ex)
        {
            var backup = path + ".corrupt";
            File.Move(path, backup, overwrite: true);
            Log.Warning(ex, "games.json was unreadable; moved to {Backup}. Recently played starts empty.", backup);
            return [];
        }
    }

    public void SaveRecent(IReadOnlyList<string> launchPaths)
    {
        ArgumentNullException.ThrowIfNull(launchPaths);

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new FileShape([.. launchPaths]), Options));
        File.Move(temp, path, overwrite: true);
    }
}
