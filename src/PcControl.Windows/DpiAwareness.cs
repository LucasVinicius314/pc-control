using Windows.Win32;
using Windows.Win32.UI.HiDpi;

namespace PcControl.Windows;

public static class DpiAwareness
{
    /// <summary>True when the calling thread is per-monitor DPI aware (v2), so coordinates are physical pixels.</summary>
    public static bool IsPerMonitorV2()
    {
        var current = PInvoke.GetThreadDpiAwarenessContext();
        return PInvoke.AreDpiAwarenessContextsEqual(current, DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    }
}
