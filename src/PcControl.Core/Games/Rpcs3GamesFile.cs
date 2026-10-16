namespace PcControl.Core.Games;

/// <summary>
/// Reads RPCS3's <c>config/games.yml</c>: one line per game, "SERIAL: folder". It is plain enough that
/// a full YAML parser is not needed, and an unreadable line is skipped instead of failing the list.
/// </summary>
public static class Rpcs3GamesFile
{
    public static IReadOnlyList<(string Serial, string Path)> Parse(string text)
    {
        var games = new List<(string, string)>();
        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var colon = line.IndexOf(": ", StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var serial = Unquote(line[..colon].Trim());
            var path = Unquote(line[(colon + 2)..].Trim());
            if (serial.Length == 0 || path.Length == 0 || !serial.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            {
                continue;
            }

            games.Add((serial, path));
        }

        return games;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1]
            : value;
}
