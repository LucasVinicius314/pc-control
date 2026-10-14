using System.Text.Json;
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
}
