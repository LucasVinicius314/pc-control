using Windows.Win32;
using Windows.Win32.UI.HiDpi;

namespace PcControl.Windows;

public static class DpiAwareness
{
    /// <summary>
    /// Makes the whole process per-monitor DPI aware (v2). The app does this at startup through its
    /// settings; this is for hosts that do not, such as a test runner. Screen duplication refuses to
    /// work in a process that is not DPI aware. Returns false if it was already set or cannot be.
    /// </summary>
    public static bool EnsurePerMonitorV2() =>
        IsPerMonitorV2() || PInvoke.SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

    /// <summary>True when the calling thread is per-monitor DPI aware (v2), so coordinates are physical pixels.</summary>
    public static bool IsPerMonitorV2()
    {
        var current = PInvoke.GetThreadDpiAwarenessContext();
        return PInvoke.AreDpiAwarenessContextsEqual(current, DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    }
}
