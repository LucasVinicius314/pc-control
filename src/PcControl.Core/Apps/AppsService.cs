namespace PcControl.Core.Apps;

/// <summary>Reads the Start menu and starts apps. The Windows implementation uses the shell; tests use a fake.</summary>
public interface IAppCatalog
{
    /// <summary>Every Start menu entry, in no particular order. May be slow, so callers cache it briefly.</summary>
    IReadOnlyList<AppEntry> GetApps();

    /// <summary>The app's icon as a PNG of about <paramref name="size"/> pixels, or null if it has none.</summary>
    byte[]? GetIconPng(AppEntry app, int size);

    /// <summary>Starts the app. True means Windows accepted the request.</summary>
    bool Launch(AppEntry app);
}

/// <summary>What the user marked and what was used lately, by launch id. Survives restarts.</summary>
public sealed record AppsState(IReadOnlyList<string> Favorites, IReadOnlyList<string> Recent)
{
    public static AppsState Empty { get; } = new([], []);
}

public interface IAppsStateStore
{
    AppsState Load();

    void Save(AppsState state);
}

public sealed record AppListItem(string Id, string Name, bool Favorite);

/// <param name="Apps">Every app, alphabetical.</param>
/// <param name="Recent">Ids of recently launched apps that still exist, newest first.</param>
public sealed record AppList(IReadOnlyList<AppListItem> Apps, IReadOnlyList<string> Recent);

public enum LaunchOutcome
{
    Ok,
    UnknownApp,
    Failed,
}

public sealed record LaunchResult(LaunchOutcome Outcome, AppEntry? App);

/// <summary>
/// The app list the phone shows: the Start menu, plus favorites and recently used apps kept on the
/// PC. The phone can only launch apps by the ids this service handed out, never by command line.
/// </summary>
public sealed class AppsService
{
    public const int MaxRecent = 50;

    private readonly IAppCatalog _catalog;
    private readonly IAppsStateStore _store;
    private readonly int _recentLimit;
    private readonly TimeSpan _cacheLifetime;
    private readonly TimeProvider _time;
    private readonly object _gate = new();

    private IReadOnlyList<AppEntry>? _apps;
    private Dictionary<string, AppEntry> _byId = [];
    private DateTimeOffset _loadedAt;
    private AppsState _state;

    public AppsService(
        IAppCatalog catalog,
        IAppsStateStore store,
        TimeProvider time,
        int recentLimit = 20,
        TimeSpan? cacheLifetime = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfLessThan(recentLimit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(recentLimit, MaxRecent);

        _catalog = catalog;
        _store = store;
        _time = time;
        _recentLimit = recentLimit;
        _cacheLifetime = cacheLifetime ?? TimeSpan.FromSeconds(3);
        _state = store.Load();
    }

    /// <summary>
    /// The list for the launcher screen. The Start menu is read again if the last read is more than a
    /// few seconds old, so newly installed apps show up the next time the launcher opens.
    /// </summary>
    public AppList GetList()
    {
        lock (_gate)
        {
            var apps = LoadApps();
            var favorites = _state.Favorites.ToHashSet(StringComparer.Ordinal);

            var items = apps
                .OrderBy(a => TextSort.Key(a.Name), StringComparer.Ordinal)
                .ThenBy(a => a.Name, StringComparer.Ordinal)
                .ThenBy(a => a.Id, StringComparer.Ordinal)
                .Select(a => new AppListItem(a.Id, a.Name, favorites.Contains(a.LaunchId)))
                .ToList();

            var recent = _state.Recent
                .Select(launchId => apps.FirstOrDefault(a => a.LaunchId == launchId))
                .OfType<AppEntry>()
                .Select(a => a.Id)
                .ToList();

            return new AppList(items, recent);
        }
    }

    public AppEntry? Find(string? id)
    {
        if (!AppId.LooksValid(id))
        {
            return null;
        }

        lock (_gate)
        {
            LoadApps();
            return _byId.GetValueOrDefault(id!);
        }
    }

    public LaunchResult Launch(string? id)
    {
        var app = Find(id);
        if (app is null)
        {
            return new LaunchResult(LaunchOutcome.UnknownApp, null);
        }

        if (!_catalog.Launch(app))
        {
            return new LaunchResult(LaunchOutcome.Failed, app);
        }

        lock (_gate)
        {
            var recent = _state.Recent.Where(r => r != app.LaunchId).Prepend(app.LaunchId).Take(_recentLimit).ToList();
            Save(_state with { Recent = recent });
        }

        return new LaunchResult(LaunchOutcome.Ok, app);
    }

    /// <summary>Marks or unmarks a favorite. False if there is no such app.</summary>
    public bool SetFavorite(string? id, bool favorite)
    {
        var app = Find(id);
        if (app is null)
        {
            return false;
        }

        lock (_gate)
        {
            var favorites = _state.Favorites.Where(f => f != app.LaunchId).ToList();
            if (favorite)
            {
                favorites.Add(app.LaunchId);
            }

            Save(_state with { Favorites = favorites });
        }

        return true;
    }

    public byte[]? GetIcon(string? id, int size)
    {
        var app = Find(id);
        return app is null ? null : _catalog.GetIconPng(app, size);
    }

    private IReadOnlyList<AppEntry> LoadApps()
    {
        var now = _time.GetUtcNow();
        if (_apps is null || now - _loadedAt >= _cacheLifetime)
        {
            // Two entries with the same launch id are the same app; keep the first.
            var apps = _catalog.GetApps()
                .Where(a => !string.IsNullOrWhiteSpace(a.Name))
                .GroupBy(a => a.LaunchId, StringComparer.Ordinal)
                .Select(g => g.First())
                .ToList();

            _apps = apps;
            _byId = apps.GroupBy(a => a.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            _loadedAt = now;
        }

        return _apps;
    }

    private void Save(AppsState state)
    {
        _state = state;
        _store.Save(state);
    }
}
