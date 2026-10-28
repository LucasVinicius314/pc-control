using System.Runtime.InteropServices;
using PcControl.Core.Capture;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.StationsAndDesktops;

namespace PcControl.Windows;

/// <summary>Reads the screen with GDI (BitBlt). Fast enough for a phone preview; no cursor in the picture.</summary>
public sealed class GdiScreenCapturer : IScreenCapturer
{
    // Desktop Duplication or Windows.Graphics.Capture can replace this if the frame rate is too low.

    private const uint MonitorInfoFPrimary = 1; // MONITORINFOF_PRIMARY

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var found = new List<(string Name, Rect Bounds, bool Primary)>();

        unsafe
        {
            PInvoke.EnumDisplayMonitors(
                HDC.Null,
                (RECT?)null,
                (monitor, _, _, _) =>
                {
                    var info = new MONITORINFOEXW { monitorInfo = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFOEXW) } };
                    if (PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info))
                    {
                        var r = info.monitorInfo.rcMonitor;
                        found.Add((info.szDevice.ToString(), new Rect(r.left, r.top, r.right - r.left, r.bottom - r.top), (info.monitorInfo.dwFlags & MonitorInfoFPrimary) != 0));
                    }

                    return true;
                },
                0);
        }

        // Left to right, then top to bottom, so "monitor 1" means the same thing every time.
        return [.. found
            .OrderBy(m => m.Bounds.X)
            .ThenBy(m => m.Bounds.Y)
            .Select((m, i) => new MonitorInfo(i, m.Name, m.Bounds, m.Primary))];
    }

    public bool TryGetCursor(out int x, out int y)
    {
        if (PInvoke.GetCursorPos(out var point))
        {
            x = point.X;
            y = point.Y;
            return true;
        }

        x = y = 0;
        return false;
    }

    public bool IsDesktopAvailable()
    {
        // While the PC is locked, or a UAC prompt is up, the input desktop is a protected one that a
        // normal process cannot open. That is also exactly when the screen cannot be captured.
        var desktop = PInvoke.OpenInputDesktop(0, false, DESKTOP_ACCESS_FLAGS.DESKTOP_SWITCHDESKTOP);
        if (desktop.IsNull)
        {
            return false;
        }

        PInvoke.CloseDesktop(desktop);
        return true;
    }

    public RawFrame? Capture(Rect source, int outputWidth, int outputHeight)
    {
        if (source.Width <= 0 || source.Height <= 0 || outputWidth <= 0 || outputHeight <= 0)
        {
            return null;
        }

        var screen = PInvoke.GetDC(HWND.Null);
        if (screen.IsNull)
        {
            return null;
        }

        var memory = PInvoke.CreateCompatibleDC(screen);
        var bitmap = PInvoke.CreateCompatibleBitmap(screen, outputWidth, outputHeight);
        try
        {
            if (memory.IsNull || bitmap.IsNull)
            {
                return null;
            }

            var previous = PInvoke.SelectObject(memory, bitmap);

            // CAPTUREBLT includes layered windows, which many modern apps use.
            var copied = outputWidth == source.Width && outputHeight == source.Height
                ? (bool)PInvoke.BitBlt(memory, 0, 0, outputWidth, outputHeight, screen, source.X, source.Y, ROP_CODE.SRCCOPY | ROP_CODE.CAPTUREBLT)
                : ScaledCopy(memory, screen, source, outputWidth, outputHeight);

            PInvoke.SelectObject(memory, previous);
            return copied ? ReadPixels(memory, bitmap, outputWidth, outputHeight) : null;
        }
        finally
        {
            if (!bitmap.IsNull)
            {
                PInvoke.DeleteObject(bitmap);
            }

            if (!memory.IsNull)
            {
                PInvoke.DeleteDC(memory);
            }

            _ = PInvoke.ReleaseDC(HWND.Null, screen);
        }
    }

    private static bool ScaledCopy(HDC memory, HDC screen, Rect source, int outputWidth, int outputHeight)
    {
        // HALFTONE averages source pixels when shrinking, which looks far better than dropping them.
        _ = PInvoke.SetStretchBltMode(memory, STRETCH_BLT_MODE.HALFTONE);
        return PInvoke.StretchBlt(
            memory, 0, 0, outputWidth, outputHeight,
            screen, source.X, source.Y, source.Width, source.Height,
            ROP_CODE.SRCCOPY | ROP_CODE.CAPTUREBLT);
    }

    internal static RawFrame? ReadPixels(HDC memory, HBITMAP bitmap, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // negative: top row first
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
            },
        };

        unsafe
        {
            fixed (byte* p = pixels)
            {
                var lines = PInvoke.GetDIBits(memory, bitmap, 0, (uint)height, p, &info, DIB_USAGE.DIB_RGB_COLORS);
                return lines == height ? new RawFrame(width, height, pixels) : null;
            }
        }
    }
}
