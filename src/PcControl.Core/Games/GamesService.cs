using PcControl.Core.Apps;

namespace PcControl.Core.Games;

/// <summary>A game the PC can start. Only <paramref name="Id"/>, title and serial ever leave the PC.</summary>
/// <param name="Id">A short hash of <paramref name="LaunchPath"/>; the only handle a client gets.</param>
/// <param name="Serial">The game's product code, for example BLUS30457.</param>
/// <param name="LaunchPath">The game's folder on the PC. Never sent to a client.</param>
/// <param name="Source">Which emulator the game belongs to: "rpcs3" or "pcsx2".</param>
public sealed record GameEntry(string Id, string Title, string Serial, string LaunchPath, string Source = GameSources.Rpcs3)
{
    public static GameEntry Create(string title, string serial, string launchPath, string source = GameSources.Rpcs3) =>
        new(AppId.From(source + ":" + launchPath), title, serial, launchPath, source);
}

/// <summary>The emulators games come from. The text is what clients see in the game list.</summary>
public static class GameSources
{
    public const string Rpcs3 = "rpcs3";
    public const string Pcsx2 = "pcsx2";

    /// <summary>The order the sources are shown in, and their display names.</summary>
    public static readonly IReadOnlyList<(string Id, string Name)> All = [(Rpcs3, "RPCS3"), (Pcsx2, "PCSX2")];
}

public enum GameImage
{
    /// <summary>The small cover (ICON0).</summary>
    Cover,

    /// <summary>The big background picture (PIC1), when the game has one.</summary>
    Background,
}

/// <summary>Finds games and starts them. The real one reads RPCS3's game list; tests use a fake.</summary>
public interface IGameLibrary
{
    /// <summary>Which emulator this reads games for (see <see cref="GameSources"/>).</summary>
    string Source { get; }

    /// <summary>True when the emulator was found, so there is something to list and launch.</summary>
    bool Available { get; }

    /// <summary>Why <see cref="Available"/> is false, for the log and the settings screen.</summary>
    string? UnavailableReason { get; }

    IReadOnlyList<GameEntry> GetGames();

    byte[]? GetImage(GameEntry game, GameImage image);

    bool Launch(GameEntry game);

    /// <summary>
    /// A sentence saying why this game cannot start right now (for example the emulator has no BIOS to run
    /// it with), or null when nothing is known to be wrong.
    /// </summary>
    string? LaunchProblem(GameEntry game);
}

/// <summary>Which games were played lately, by launch path. Survives restarts.</summary>
public interface IGamesStateStore
{
    IReadOnlyList<string> LoadRecent();

    void SaveRecent(IReadOnlyList<string> launchPaths);
}

public sealed record GameListItem(string Id, string Title, string Serial, string Source);

public sealed record GameList(IReadOnlyList<GameListItem> Games, IReadOnlyList<string> Recent);

/// <summary>
/// The game list for the controller UI: alphabetical, with what was played last. Clients start a game
/// only by an id from this list, never by a path.
/// </summary>
public sealed class GamesService
{
    public const int MaxRecent = 12;

    private readonly IGameLibrary _library;
    private readonly IGamesStateStore _store;
    private readonly TimeProvider _time;
    private readonly TimeSpan _cacheLifetime;
    private readonly object _gate = new();

    private IReadOnlyList<GameEntry>? _games;
    private Dictionary<string, GameEntry> _byId = [];
    private DateTimeOffset _loadedAt;
    private List<string> _recent;

    public GamesService(IGameLibrary library, IGamesStateStore store, TimeProvider time, TimeSpan? cacheLifetime = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(time);

        _library = library;
        _store = store;
        _time = time;
        _cacheLifetime = cacheLifetime ?? TimeSpan.FromSeconds(3);
        _recent = [.. store.LoadRecent()];
    }

    public bool Available => _library.Available;

    public string? UnavailableReason => _library.UnavailableReason;

    /// <summary>Forget the cached game list, so the next look reads it again (RPCS3 was moved or changed).</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _games = null;
        }
    }

    public GameList GetList()
    {
        lock (_gate)
        {
            var games = Load();
            var items = games
                .OrderBy(g => TextSort.Key(g.Title), StringComparer.Ordinal)
                .ThenBy(g => g.Title, StringComparer.Ordinal)
                .ThenBy(g => g.Id, StringComparer.Ordinal)
                .Select(g => new GameListItem(g.Id, g.Title, g.Serial, g.Source))
                .ToList();

            var recent = _recent
                .Select(path => games.FirstOrDefault(g => g.LaunchPath == path))
                .OfType<GameEntry>()
                .Select(g => g.Id)
                .ToList();

            return new GameList(items, recent);
        }
    }

    public GameEntry? Find(string? id)
    {
        if (!AppId.LooksValid(id))
        {
            return null;
        }

        lock (_gate)
        {
            Load();
            return _byId.GetValueOrDefault(id!);
        }
    }

    public byte[]? GetImage(string? id, GameImage image)
    {
        var game = Find(id);
        return game is null ? null : _library.GetImage(game, image);
    }

    /// <summary>Why this game cannot be started, if the emulator is known to be unable to (see <see cref="IGameLibrary.LaunchProblem"/>).</summary>
    public string? ProblemFor(string? id) => Find(id) is { } game ? _library.LaunchProblem(game) : null;

    public LaunchResult Launch(string? id)
    {
        var game = Find(id);
        if (game is null)
        {
            return new LaunchResult(LaunchOutcome.UnknownApp, null);
        }

        var entry = AppEntry.Create(game.Title, game.LaunchPath);

        // Starting would only open the emulator on an error, so do not.
        if (_library.LaunchProblem(game) is not null || !_library.Launch(game))
        {
            return new LaunchResult(LaunchOutcome.Failed, entry);
        }

        lock (_gate)
        {
            _recent = [.. _recent.Where(p => p != game.LaunchPath).Prepend(game.LaunchPath).Take(MaxRecent)];
            _store.SaveRecent(_recent);
        }

        return new LaunchResult(LaunchOutcome.Ok, entry);
    }

    private IReadOnlyList<GameEntry> Load()
    {
        var now = _time.GetUtcNow();
        if (_games is null || now - _loadedAt >= _cacheLifetime)
        {
            var games = _library.GetGames()
                .Where(g => !string.IsNullOrWhiteSpace(g.Title))
                .GroupBy(g => g.LaunchPath, StringComparer.Ordinal)
                .Select(g => g.First())
                .ToList();

            _games = games;
            _byId = games.GroupBy(g => g.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            _loadedAt = now;
        }

        return _games;
    }
}
