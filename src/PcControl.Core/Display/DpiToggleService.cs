using PcControl.Core.Capture;
using PcControl.Core.Config;

namespace PcControl.Core.Display;

/// <summary>One way of reading or changing a monitor's scale. Several can be tried in turn.</summary>
public interface IDisplayScaleBackend
{
    string Name { get; }

    /// <summary>Whether this backend can change the scale at all (some only read it).</summary>
    bool CanSet { get; }

    /// <summary>The monitor's current scale, or null if this backend cannot tell.</summary>
    ScaleInfo? ReadScale(MonitorInfo monitor);

    /// <summary>Asks for a new scale. True means the request was accepted, not that it already took effect.</summary>
    bool ApplyScale(MonitorInfo monitor, int percent);
}

public enum DpiOutcome
{
    Ok,
    Unavailable,
    Busy,
    NotSupported,
    Failed,
}

public sealed record DpiStatus(
    bool Available,
    string? Reason,
    int? Percent,
    MonitorInfo? Monitor,
    string? Backend);

public sealed record DpiToggleResult(
    DpiOutcome Outcome,
    int? Previous,
    int? Current,
    MonitorInfo? Monitor,
    string? Message);

/// <summary>
/// Flips a monitor between two scales (100% and 200% by default). Windows can change this setting
/// only through calls that are not officially documented, so the service tests them at startup, tries
/// the backends in order, checks that a change really took effect, and turns itself off with a
/// readable reason if nothing works.
/// </summary>
public sealed class DpiToggleService : IDisposable
{
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan SettlePoll = TimeSpan.FromMilliseconds(150);

    private readonly IScreenCapturer _screen;
    private readonly IReadOnlyList<IDisplayScaleBackend> _backends;
    private readonly DpiConfig _config;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly SemaphoreSlim _busy = new(1, 1);
    private volatile DpiStatus _status = new(false, "Not checked yet.", null, null, null);

    public DpiToggleService(
        IScreenCapturer screen,
        IReadOnlyList<IDisplayScaleBackend> backends,
        DpiConfig config,
        Func<TimeSpan, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(backends);
        ArgumentNullException.ThrowIfNull(config);

        _screen = screen;
        _backends = backends;
        _config = config;
        _delay = delay ?? (span => Task.Delay(span));
    }

    /// <summary>What the last check found. Cheap; use <see cref="Check"/> to look again.</summary>
    public DpiStatus Status => _status;

    public bool Available => _status.Available;

    public int LowPercent => _config.LowPercent;

    public int HighPercent => _config.HighPercent;

    /// <summary>
    /// Reads the current scale of the target monitor. This is the startup self-test: if Windows no
    /// longer answers, the toggle is switched off and <see cref="DpiStatus.Reason"/> says why.
    /// </summary>
    public DpiStatus Check()
    {
        var monitor = PickMonitor();
        if (monitor is null)
        {
            return _status = new DpiStatus(false, "No monitor was found.", null, null, null);
        }

        var (info, backend) = ReadAny(monitor);
        if (info is null)
        {
            return _status = new DpiStatus(
                false,
                "Windows did not answer the display scale request. A Windows update may have changed it.",
                null,
                monitor,
                null);
        }

        if (!_backends.Any(b => b.CanSet))
        {
            return _status = new DpiStatus(
                false,
                "The scale can be read, but nothing is set up to change it. Set dpi.fallbackScriptPath in config.json.",
                info.Current,
                monitor,
                backend!.Name);
        }

        return _status = new DpiStatus(true, null, info.Current, monitor, backend!.Name);
    }

    /// <summary>The target monitor's scale right now, read fresh.</summary>
    public DpiStatus Read()
    {
        var monitor = PickMonitor();
        if (monitor is null)
        {
            return _status = new DpiStatus(false, "No monitor was found.", null, null, null);
        }

        var (info, backend) = ReadAny(monitor);
        return _status = info is null
            ? new DpiStatus(false, _status.Reason ?? "The scale could not be read.", null, monitor, null)
            : new DpiStatus(_status.Available, _status.Reason, info.Current, monitor, backend!.Name);
    }

    /// <summary>Switches the target monitor between the low and the high scale.</summary>
    public async Task<DpiToggleResult> ToggleAsync()
    {
        if (!await _busy.WaitAsync(0))
        {
            return new DpiToggleResult(DpiOutcome.Busy, null, null, null, "A scale change is already in progress.");
        }

        try
        {
            if (!Available)
            {
                return new DpiToggleResult(DpiOutcome.Unavailable, null, null, null, _status.Reason);
            }

            var monitor = PickMonitor();
            if (monitor is null)
            {
                return new DpiToggleResult(DpiOutcome.Unavailable, null, null, null, "No monitor was found.");
            }

            var (info, _) = ReadAny(monitor);
            if (info is null)
            {
                return new DpiToggleResult(DpiOutcome.Failed, null, null, monitor, "The current scale could not be read.");
            }

            var target = info.Current == _config.LowPercent ? _config.HighPercent : _config.LowPercent;
            if (info.Maximum is { } maximum && target > maximum)
            {
                return new DpiToggleResult(
                    DpiOutcome.NotSupported,
                    info.Current,
                    info.Current,
                    monitor,
                    $"This monitor does not allow {target}% (its largest is {maximum}%).");
            }

            return await ApplyAsync(monitor, info.Current, target);
        }
        finally
        {
            _busy.Release();
        }
    }

    private async Task<DpiToggleResult> ApplyAsync(MonitorInfo monitor, int previous, int target)
    {
        foreach (var backend in _backends)
        {
            if (!backend.ApplyScale(monitor, target))
            {
                continue;
            }

            // The request is accepted before the change has happened; wait until Windows reports it.
            var now = await WaitForScaleAsync(monitor, target);
            if (now == target)
            {
                _status = new DpiStatus(true, null, now, monitor, backend.Name);
                return new DpiToggleResult(DpiOutcome.Ok, previous, now, monitor, null);
            }
        }

        var current = ReadAny(monitor).Info?.Current ?? previous;
        return new DpiToggleResult(
            DpiOutcome.Failed,
            previous,
            current,
            monitor,
            $"Windows did not switch to {target}%.");
    }

    private async Task<int?> WaitForScaleAsync(MonitorInfo monitor, int target)
    {
        var waited = TimeSpan.Zero;
        int? current = null;
        while (true)
        {
            current = ReadAny(monitor).Info?.Current;
            if (current == target || waited >= SettleTimeout)
            {
                return current;
            }

            await _delay(SettlePoll);
            waited += SettlePoll;
        }
    }

    private (ScaleInfo? Info, IDisplayScaleBackend? Backend) ReadAny(MonitorInfo monitor)
    {
        foreach (var backend in _backends)
        {
            var info = backend.ReadScale(monitor);
            if (info is not null)
            {
                return (info, backend);
            }
        }

        return (null, null);
    }

    public void Dispose() => _busy.Dispose();

    private MonitorInfo? PickMonitor()
    {
        var monitors = _screen.GetMonitors();
        if (monitors.Count == 0)
        {
            return null;
        }

        if (!_screen.TryGetCursor(out var x, out var y))
        {
            (x, y) = (RegionPlanner.NoCursor, RegionPlanner.NoCursor);
        }

        return _config.Monitor.ToLowerInvariant() switch
        {
            "primary" => monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0],
            "cursor" => RegionPlanner.PickMonitor(monitors, ViewOptions.FollowCursor, x, y),
            var index when int.TryParse(index, System.Globalization.CultureInfo.InvariantCulture, out var i)
                           && i >= 0 && i < monitors.Count => monitors[i],
            _ => RegionPlanner.PickMonitor(monitors, ViewOptions.FollowCursor, x, y),
        };
    }
}
