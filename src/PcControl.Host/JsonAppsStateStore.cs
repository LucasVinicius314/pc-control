using System.Text.Json;
using PcControl.Core.Apps;
using Serilog;

namespace PcControl.Host;

/// <summary>Keeps favorites and recently used apps in a JSON file, written atomically.</summary>
internal sealed class JsonAppsStateStore(string path) : IAppsStateStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    // A plain shape for the file, so its format does not depend on how the record is declared.
    private sealed record FileShape(List<string>? Favorites, List<string>? Recent);

    public AppsState Load()
    {
        if (!File.Exists(path))
        {
            return AppsState.Empty;
        }

        try
        {
            var shape = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(path), Options);
            return new AppsState(Clean(shape?.Favorites), Clean(shape?.Recent));
        }
        catch (JsonException ex)
        {
            // Losing favorites is better than refusing to start. The bad file is kept for inspection.
            var backup = path + ".corrupt";
            File.Move(path, backup, overwrite: true);
            Log.Warning(ex, "apps.json was unreadable; moved to {Backup}. Favorites and recent apps start empty.", backup);
            return AppsState.Empty;
        }
    }

    public void Save(AppsState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new FileShape([.. state.Favorites], [.. state.Recent]), Options));
        File.Move(temp, path, overwrite: true);
    }

    private static List<string> Clean(List<string>? values) =>
        [.. (values ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal)];
}
