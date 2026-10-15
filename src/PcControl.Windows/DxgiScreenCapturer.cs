using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PcControl.Core.Capture;
using Windows.Win32;
using Windows.Win32.Graphics.Dxgi;

namespace PcControl.Windows;

/// <summary>
/// Reads the screen with DXGI Desktop Duplication, falling back to another capturer (GDI) whenever
/// that is not possible: a remote-desktop session, an unsupported graphics driver, HDR, a screen that
/// is locked. Everything else (monitor list, cursor, lock detection) comes from the fallback.
/// </summary>
/// <remarks>
/// The reason for not simply using GDI: GDI screen capture that includes transparent windows makes
/// the mouse cursor flicker on many systems, and it re-reads the whole screen every time even when
/// nothing changed. Desktop Duplication does neither. Every phone shares one duplication per monitor.
/// </remarks>
public sealed class DxgiScreenCapturer(IScreenCapturer fallback, ILogger<DxgiScreenCapturer> logger, TimeProvider? time = null) : IScreenCapturer, IDisposable
{
    /// <summary>The monitor layout rarely changes; asking Windows for it dozens of times a second is wasted work.</summary>
    public static readonly TimeSpan MonitorCacheLifetime = TimeSpan.FromSeconds(2);

    /// <summary>Whether the screen is locked can change at any moment, but not more than a few times a second matters.</summary>
    public static readonly TimeSpan DesktopCheckLifetime = TimeSpan.FromMilliseconds(250);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private IReadOnlyList<MonitorInfo>? _monitors;
    private DateTimeOffset _monitorsAt;
    private bool _desktopAvailable;
    private DateTimeOffset _desktopCheckedAt;

    /// <summary>After a failure, wait this long before trying Desktop Duplication again.</summary>
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan MinimumLogInterval = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private readonly List<DxgiOutputDuplicator> _duplicators = [];
    private long _retryAtTicks;
    private long _lastLoggedTicks;
    private bool _enumerated;
    private List<(IDXGIAdapter1 Adapter, IDXGIOutput Output, Rect Desktop)> _outputs = [];

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (_monitors is null || now - _monitorsAt >= MonitorCacheLifetime)
            {
                _monitors = fallback.GetMonitors();
                _monitorsAt = now;
            }

            return _monitors;
        }
    }

    public bool TryGetCursor(out int x, out int y) => fallback.TryGetCursor(out x, out y);

    public bool IsDesktopAvailable()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (_desktopCheckedAt == default || now - _desktopCheckedAt >= DesktopCheckLifetime)
            {
                _desktopAvailable = fallback.IsDesktopAvailable();
                _desktopCheckedAt = now;
            }

            return _desktopAvailable;
        }
    }

    /// <summary>What Windows reported about the mouse pointer on all monitors so far. For diagnostics and tests.</summary>
    internal PointerStatistics PointerActivity
    {
        get
        {
            lock (_gate)
            {
                return _duplicators.Aggregate(
                    default(PointerStatistics),
                    (sum, d) => new PointerStatistics(sum.Updates + d.Pointer.Updates, sum.VisibilityFlips + d.Pointer.VisibilityFlips, sum.ShapeChanges + d.Pointer.ShapeChanges));
            }
        }
    }

    /// <summary>How many of the monitors currently have a working duplication. For diagnostics and tests.</summary>
    public int ActiveDuplications
    {
        get
        {
            lock (_gate)
            {
                return _duplicators.Count;
            }
        }
    }

    public RawFrame? Capture(Rect source, int outputWidth, int outputHeight)
    {
        if (source.Width <= 0 || source.Height <= 0 || outputWidth <= 0 || outputHeight <= 0)
        {
            return null;
        }

        var frame = TryDuplicate(source, outputWidth, outputHeight);
        return frame ?? fallback.Capture(source, outputWidth, outputHeight);
    }

    private RawFrame? TryDuplicate(Rect source, int outputWidth, int outputHeight)
    {
        lock (_gate)
        {
            if (Environment.TickCount64 < _retryAtTicks)
            {
                return null;
            }

            try
            {
                var duplicator = DuplicatorFor(source);
                if (duplicator is null)
                {
                    return Failed("no screen duplication is available for that part of the screen");
                }

                if (!duplicator.Refresh(firstFrameTimeoutMs: 1000))
                {
                    Discard(duplicator);
                    return Failed(duplicator.Unsupported
                        ? "the graphics driver delivers this monitor's picture in a form that cannot be read"
                        : "Windows ended the screen duplication (it is rebuilt next time)");
                }

                var local = new Rect(source.X - duplicator.Desktop.X, source.Y - duplicator.Desktop.Y, source.Width, source.Height);

                // Not an error: the first picture has not arrived yet. The older capture covers this
                // one request and the next call asks again.
                return duplicator.Read(local, outputWidth, outputHeight);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or AccessViolationException)
            {
                return Failed(ex.Message);
            }
        }
    }

    private DxgiOutputDuplicator? DuplicatorFor(Rect source)
    {
        var centreX = source.X + (source.Width / 2);
        var centreY = source.Y + (source.Height / 2);

        var existing = _duplicators.FirstOrDefault(d => d.Desktop.Contains(centreX, centreY));
        if (existing is not null)
        {
            return existing;
        }

        EnumerateOutputs();
        foreach (var (adapter, output, desktop) in _outputs)
        {
            if (!desktop.Contains(centreX, centreY))
            {
                continue;
            }

            var created = DxgiOutputDuplicator.TryCreate(adapter, output, out var problem);
            if (created is null)
            {
                Log($"Desktop Duplication is not available for the monitor at {desktop.X},{desktop.Y}: {problem}. Using the older screen capture.");
                return null;
            }

            _duplicators.Add(created);
            return created;
        }

        return null;
    }

    private void EnumerateOutputs()
    {
        if (_enumerated)
        {
            return;
        }

        _enumerated = true;
        _outputs = [];

        var iid = typeof(IDXGIFactory1).GUID;
        unsafe
        {
            if (PInvoke.CreateDXGIFactory1(&iid, out var raw).Failed || raw is not IDXGIFactory1 factory)
            {
                return;
            }

            for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Succeeded; a++)
            {
                for (uint o = 0; adapter.EnumOutputs(o, out var output).Succeeded; o++)
                {
                    var desc = output.GetDesc();
                    if (!desc.AttachedToDesktop)
                    {
                        continue;
                    }

                    var r = desc.DesktopCoordinates;
                    _outputs.Add((adapter, output, new Rect(r.left, r.top, r.right - r.left, r.bottom - r.top)));
                }
            }
        }
    }

    private void Discard(DxgiOutputDuplicator duplicator)
    {
        _duplicators.Remove(duplicator);
        duplicator.Dispose();

        // The list of monitors may have changed (a resolution change, a monitor unplugged): look again.
        _enumerated = false;
        foreach (var (adapter, output, _) in _outputs)
        {
            Marshal.ReleaseComObject(output);
            Marshal.ReleaseComObject(adapter);
        }

        _outputs = [];
    }

    private RawFrame? Failed(string reason)
    {
        _retryAtTicks = Environment.TickCount64 + (long)RetryAfterFailure.TotalMilliseconds;
        Log($"Screen capture fell back to the older method: {reason}");
        return null;
    }

    private void Log(string message)
    {
        // A locked screen or a game can fail this over and over; say so once a minute at most.
        var now = Environment.TickCount64;
        if (now - _lastLoggedTicks >= MinimumLogInterval.TotalMilliseconds || _lastLoggedTicks == 0)
        {
            _lastLoggedTicks = now;
            logger.LogInformation("{Message}", message);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var duplicator in _duplicators)
            {
                duplicator.Dispose();
            }

            _duplicators.Clear();

            foreach (var (adapter, output, _) in _outputs)
            {
                Marshal.ReleaseComObject(output);
                Marshal.ReleaseComObject(adapter);
            }

            _outputs = [];
        }
    }
}
