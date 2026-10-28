using System.Text.Json;
using System.Text.Json.Nodes;
using PcControl.Core.Config;

namespace PcControl.Host;

internal sealed class ConfigException(IReadOnlyList<string> problems)
    : Exception("Invalid configuration:" + Environment.NewLine + string.Join(Environment.NewLine, problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

internal static class ConfigLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    /// <summary>Reads and validates the config, writing a default file on first run.</summary>
    public static AppConfig LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            var defaults = new AppConfig();
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, Options));
            return defaults;
        }

        AppConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Options);
        }
        catch (JsonException ex)
        {
            throw new ConfigException([$"{path}: {ex.Message}"]);
        }

        if (config is null)
        {
            throw new ConfigException([$"{path}: file is empty."]);
        }

        var problems = ConfigValidator.Validate(config);
        if (problems.Count > 0)
        {
            throw new ConfigException(problems);
        }

        return config;
    }

    /// <summary>
    /// Sets one value under <c>games</c> (for example <c>rpcs3Path</c>) in the config file and leaves everything else as it is. (Comments in the
    /// file are not kept.) Written to a temporary file first, so a crash cannot leave half a config.
    /// </summary>
    public static void SetGamesValue(string path, string key, string value)
    {
        JsonObject root = new();
        if (File.Exists(path))
        {
            var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            root = JsonNode.Parse(File.ReadAllText(path), documentOptions: options) as JsonObject ?? new JsonObject();
        }

        var games = root["games"] as JsonObject ?? new JsonObject();
        root["games"] = games;
        games[key] = value;

        var temp = path + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, overwrite: true);
    }
}
