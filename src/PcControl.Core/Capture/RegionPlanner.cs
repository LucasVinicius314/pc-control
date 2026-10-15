namespace PcControl.Core.Capture;

/// <summary>What to capture and where the mouse cursor sits inside the resulting picture.</summary>
public readonly record struct CapturePlan(Rect Source, int OutputWidth, int OutputHeight, int CursorX, int CursorY);

/// <summary>Works out which part of the screen to send. Pure arithmetic, no Windows calls.</summary>
public static class RegionPlanner
{
    /// <summary>Marks a cursor that is not inside the picture.</summary>
    public const int NoCursor = -1;

    /// <summary>
    /// The monitor to show. A <paramref name="requested"/> index picks that monitor; anything else
    /// ("follow the cursor") picks the one the cursor is on, falling back to the primary monitor.
    /// </summary>
    public static MonitorInfo? PickMonitor(IReadOnlyList<MonitorInfo> monitors, int requested, int cursorX, int cursorY)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        if (monitors.Count == 0)
        {
            return null;
        }

        if (requested >= 0 && requested < monitors.Count)
        {
            return monitors[requested];
        }

        return monitors.FirstOrDefault(m => m.Bounds.Contains(cursorX, cursorY))
            ?? monitors.FirstOrDefault(m => m.IsPrimary)
            ?? monitors[0];
    }

    /// <summary>
    /// A region of <paramref name="width"/> by <paramref name="height"/> pixels centred on the
    /// cursor, shifted back inside the monitor when the cursor is near an edge. It is captured at
    /// full resolution; the phone enlarges it, so the zoom is just the ratio of sizes.
    /// </summary>
    public static CapturePlan Magnifier(Rect monitor, int cursorX, int cursorY, int width, int height)
    {
        var w = Math.Clamp(width, 1, monitor.Width);
        var h = Math.Clamp(height, 1, monitor.Height);

        var x = Math.Clamp(cursorX - (w / 2), monitor.X, monitor.Right - w);
        var y = Math.Clamp(cursorY - (h / 2), monitor.Y, monitor.Bottom - h);
        var source = new Rect(x, y, w, h);

        return new CapturePlan(source, w, h, source.Contains(cursorX, cursorY) ? cursorX - x : NoCursor, source.Contains(cursorX, cursorY) ? cursorY - y : NoCursor);
    }

    /// <summary>
    /// The whole monitor, scaled down (never up) to fit within <paramref name="maxWidth"/> by
    /// <paramref name="maxHeight"/> pixels. Limiting the height too matters for portrait monitors:
    /// 800 pixels wide would otherwise mean over 1800 tall, and big pictures arrive late.
    /// </summary>
    public static CapturePlan Display(Rect monitor, int cursorX, int cursorY, int maxWidth, int maxHeight = ViewOptions.MaxDisplayHeight)
    {
        var scale = Math.Min(
            1.0,
            Math.Min((double)Math.Max(1, maxWidth) / monitor.Width, (double)Math.Max(1, maxHeight) / monitor.Height));
        var outWidth = Math.Max(1, (int)Math.Round(monitor.Width * scale));
        var outHeight = Math.Max(1, (int)Math.Round(monitor.Height * scale));

        if (!monitor.Contains(cursorX, cursorY))
        {
            return new CapturePlan(monitor, outWidth, outHeight, NoCursor, NoCursor);
        }

        var cx = (int)Math.Round((double)(cursorX - monitor.X) * outWidth / monitor.Width);
        var cy = (int)Math.Round((double)(cursorY - monitor.Y) * outHeight / monitor.Height);
        return new CapturePlan(monitor, outWidth, outHeight, Math.Min(cx, outWidth - 1), Math.Min(cy, outHeight - 1));
    }
}
