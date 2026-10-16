using PcControl.Core.Apps;

namespace PcControl.Core.Actions;

/// <summary>A program with a window, as the PC sees it. The pid and start time identify it exactly.</summary>
public sealed record RunningApp(int Pid, long StartTicks, string ProcessName, string Title);

public enum CloseOutcome
{
    Ok,
    Gone,
    Failed,
}

public interface IProcessCatalog
{
    /// <summary>Programs with a visible window in the signed-in user's session.</summary>
    IReadOnlyList<RunningApp> List();

    /// <summary>
    /// Asks the program to close (or, with <paramref name="force"/>, ends it). Does nothing if the
    /// process with that pid did not start at <see cref="RunningApp.StartTicks"/> (the pid was reused).
    /// </summary>
    CloseOutcome Close(RunningApp app, bool force);

    /// <summary>Brings the program's window to the front (restoring it if minimised), like Alt+Tab. Same checks as <see cref="Close"/>.</summary>
    CloseOutcome Focus(RunningApp app);
}

/// <summary>What the phone sees of a program: an opaque id, never a process id.</summary>
public sealed record ProcessItem(string Id, string Name, string Title);

public enum ProcessOutcome
{
    Ok,
    Unknown,
    Gone,
    Failed,
}

/// <summary>
/// The close-a-program list. The phone only gets programs that have a window, never the Windows
/// system processes, and acts on them by an id from the latest list, so it cannot name a process
/// number of its own choosing.
/// </summary>
public sealed class ProcessService(IProcessCatalog catalog, int ownPid)
{
    public const int MaxListed = 100;

    /// <summary>Never offered, even if they have a window. Ending these breaks the session or the shell.</summary>
    public static readonly IReadOnlySet<string> Protected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "csrss", "winlogon", "wininit", "services", "lsass", "smss", "svchost", "dwm", "system",
        "registry", "fontdrvhost", "sihost", "taskhostw", "ctfmon", "runtimebroker", "shellexperiencehost",
        "startmenuexperiencehost", "searchhost", "textinputhost", "applicationframehost", "securityhealthsystray",
        "pccontrol",
    };

    private readonly object _gate = new();
    private Dictionary<string, RunningApp> _known = [];

    public static string IdOf(RunningApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return AppId.From($"{app.Pid}:{app.StartTicks}");
    }

    public IReadOnlyList<ProcessItem> GetList()
    {
        var apps = catalog.List()
            .Where(a => a.Pid != ownPid && !Protected.Contains(a.ProcessName) && !string.IsNullOrWhiteSpace(a.Title))
            .OrderBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxListed)
            .ToList();

        lock (_gate)
        {
            _known = apps.ToDictionary(IdOf, a => a, StringComparer.Ordinal);
        }

        return apps.Select(a => new ProcessItem(IdOf(a), a.ProcessName, a.Title)).ToList();
    }

    public (ProcessOutcome Outcome, string? Title) Switch(string id) => Act(id, app => catalog.Focus(app));

    public (ProcessOutcome Outcome, string? Title) Close(string id, bool force) => Act(id, app => catalog.Close(app, force));

    private (ProcessOutcome Outcome, string? Title) Act(string id, Func<RunningApp, CloseOutcome> action)
    {
        RunningApp? app;
        lock (_gate)
        {
            _known.TryGetValue(id, out app);
        }

        if (app is null)
        {
            return (ProcessOutcome.Unknown, null);
        }

        var outcome = action(app) switch
        {
            CloseOutcome.Ok => ProcessOutcome.Ok,
            CloseOutcome.Gone => ProcessOutcome.Gone,
            _ => ProcessOutcome.Failed,
        };
        return (outcome, app.Title);
    }
}
