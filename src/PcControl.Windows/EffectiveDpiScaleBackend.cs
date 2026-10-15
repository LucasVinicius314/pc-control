using PcControl.Core.Capture;
using PcControl.Core.Display;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace PcControl.Windows;

/// <summary>
/// Reads a monitor's scale with the documented GetDpiForMonitor call. It cannot change anything, but
/// it does not depend on any undocumented behaviour, so it keeps working after a Windows update and
/// can confirm what the other backends did. It needs the process to be per-monitor DPI aware.
/// </summary>
public sealed class EffectiveDpiScaleBackend : IDisplayScaleBackend
{
    private const double BaselineDpi = 96.0;

    public string Name => "effective-dpi";

    public bool CanSet => false;

    public ScaleInfo? ReadScale(MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        var centre = new System.Drawing.Point(
            monitor.Bounds.X + (monitor.Bounds.Width / 2),
            monitor.Bounds.Y + (monitor.Bounds.Height / 2));

        var handle = PInvoke.MonitorFromPoint(centre, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL);
        if (handle.IsNull)
        {
            return null;
        }

        var result = PInvoke.GetDpiForMonitor(handle, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _);
        if (result.Failed || dpiX == 0)
        {
            return null;
        }

        return new ScaleInfo(DpiScaleTable.Nearest(dpiX / BaselineDpi * 100.0));
    }

    public bool ApplyScale(MonitorInfo monitor, int percent) => false;
}
