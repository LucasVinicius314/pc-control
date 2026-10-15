using Windows.Win32;
using Windows.Win32.System.Threading;

namespace PcControl.Windows;

/// <summary>
/// Makes Windows timers tick every millisecond instead of every 15.6 ms, so a loop that waits 16 ms
/// really wakes after about 16 ms. It costs a little power, so it is only on while someone holds a
/// lease, for example while a phone has a preview open.
/// </summary>
public static class HighResolutionTimer
{
    private const uint Milliseconds = 1;

    // Values from the Windows SDK (processthreadsapi.h); the generator does not emit these constants.
    private const uint ThrottlingCurrentVersion = 1;
    private const uint ThrottlingIgnoreTimerResolution = 0x4;

    private static readonly object Gate = new();
    private static int _leases;
    private static bool _throttlingOptOutDone;

    /// <summary>Raises the timer resolution until the returned lease is disposed.</summary>
    public static IDisposable Acquire()
    {
        lock (Gate)
        {
            if (_leases++ == 0)
            {
                _ = PInvoke.timeBeginPeriod(Milliseconds);
                OptOutOfTimerThrottling();
            }
        }

        return new Lease();
    }

    private static void Release()
    {
        lock (Gate)
        {
            if (--_leases == 0)
            {
                _ = PInvoke.timeEndPeriod(Milliseconds);
            }
        }
    }

    /// <summary>
    /// Windows 11 may ignore the raised resolution for a process without a visible window (like a
    /// tray app). This tells it not to.
    /// </summary>
    private static void OptOutOfTimerThrottling()
    {
        if (_throttlingOptOutDone)
        {
            return;
        }

        _throttlingOptOutDone = true;

        var state = new PROCESS_POWER_THROTTLING_STATE
        {
            Version = ThrottlingCurrentVersion,
            ControlMask = ThrottlingIgnoreTimerResolution,
            StateMask = 0, // 0 with the bit in ControlMask means "do not throttle timer resolution"
        };

        unsafe
        {
            _ = PInvoke.SetProcessInformation(
                PInvoke.GetCurrentProcess(),
                PROCESS_INFORMATION_CLASS.ProcessPowerThrottling,
                &state,
                (uint)sizeof(PROCESS_POWER_THROTTLING_STATE));
        }
    }

    private sealed class Lease : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                Release();
            }
        }
    }
}
