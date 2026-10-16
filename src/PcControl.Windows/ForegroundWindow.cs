using Windows.Win32;
using Windows.Win32.Foundation;

namespace PcControl.Windows;

public static class ForegroundWindow
{
    /// <summary>
    /// Windows only lets the program that owns the foreground window change it. Joining the input queue
    /// of the current foreground window for a moment is the usual way to be allowed to.
    /// </summary>
    public static bool BringToFront(nint handle)
    {
        var window = new HWND(handle);
        if (window.IsNull)
        {
            return false;
        }

        var foreground = PInvoke.GetForegroundWindow();
        var foregroundThread = foreground.IsNull ? 0u : PInvoke.GetWindowThreadProcessId(foreground, out _);
        var thisThread = PInvoke.GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != thisThread && PInvoke.AttachThreadInput(thisThread, foregroundThread, true);
        try
        {
            return PInvoke.SetForegroundWindow(window);
        }
        finally
        {
            if (attached)
            {
                PInvoke.AttachThreadInput(thisThread, foregroundThread, false);
            }
        }
    }
}
