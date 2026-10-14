using System.Text.Json;
using PcControl.Core.Pairing;
using Serilog;

namespace PcControl.Host;

/// <summary>Stores paired devices (token hashes only) in a JSON file, written atomically.</summary>
internal sealed class JsonDeviceStore(string path) : IDeviceStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public IReadOnlyList<PairedDevice> Load()
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<PairedDevice>>(File.ReadAllText(path), Options) ?? [];
        }
        catch (JsonException ex)
        {
            // Better to start unpaired than to refuse to start. The bad file is kept for inspection.
            var backup = path + ".corrupt";
            File.Move(path, backup, overwrite: true);
            Log.Warning(ex, "devices.json was unreadable; moved to {Backup}. All phones must pair again.", backup);
            return [];
        }
    }

    public void Save(IReadOnlyList<PairedDevice> devices)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(devices, Options));
        File.Move(temp, path, overwrite: true);
    }
}
