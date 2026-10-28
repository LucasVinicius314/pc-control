using PcControl.Core.Capture;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Storage.Xps;

namespace PcControl.Windows;

/// <summary>
/// A picture of one window, like the thumbnails in Alt+Tab. The window draws itself into a bitmap
/// (PrintWindow with full content, so GPU-drawn windows such as browsers work), so it does not matter
/// what is on top of it. A minimised window has nothing to draw and gives no picture.
/// </summary>
internal static class WindowPicture
{
    private const int MinSize = 16;
    private const int MaxSize = 16384;

    // PW_RENDERFULLCONTENT: also draws windows that a GPU composites (browsers, modern apps). Not named by the generated flags.
    private const PRINT_WINDOW_FLAGS RenderFullContent = (PRINT_WINDOW_FLAGS)2;

    public static RawFrame? Capture(HWND window, int maxWidth)
    {
        if (window.IsNull || !PInvoke.IsWindow(window) || PInvoke.IsIconic(window) || maxWidth <= 0)
        {
            return null;
        }

        if (!PInvoke.GetWindowRect(window, out var rect))
        {
            return null;
        }

        var width = rect.right - rect.left;
        var height = rect.bottom - rect.top;
        if (width < MinSize || height < MinSize || width > MaxSize || height > MaxSize)
        {
            return null;
        }

        var screen = PInvoke.GetDC(HWND.Null);
        if (screen.IsNull)
        {
            return null;
        }

        var full = default(HDC);
        var fullBitmap = default(HBITMAP);
        var small = default(HDC);
        var smallBitmap = default(HBITMAP);
        try
        {
            full = PInvoke.CreateCompatibleDC(screen);
            fullBitmap = PInvoke.CreateCompatibleBitmap(screen, width, height);
            if (full.IsNull || fullBitmap.IsNull)
            {
                return null;
            }

            var previous = PInvoke.SelectObject(full, fullBitmap);
            var drawn = PInvoke.PrintWindow(window, full, RenderFullContent);
            if (!drawn)
            {
                PInvoke.SelectObject(full, previous);
                return null;
            }

            var outWidth = Math.Min(width, maxWidth);
            var outHeight = Math.Max(1, (int)((long)height * outWidth / width));
            RawFrame? frame;
            if (outWidth == width)
            {
                PInvoke.SelectObject(full, previous);
                frame = GdiScreenCapturer.ReadPixels(full, fullBitmap, width, height);
            }
            else
            {
                small = PInvoke.CreateCompatibleDC(screen);
                smallBitmap = PInvoke.CreateCompatibleBitmap(screen, outWidth, outHeight);
                if (small.IsNull || smallBitmap.IsNull)
                {
                    PInvoke.SelectObject(full, previous);
                    return null;
                }

                var smallPrevious = PInvoke.SelectObject(small, smallBitmap);
                // HALFTONE averages the pixels when shrinking, which looks far better than dropping them.
                _ = PInvoke.SetStretchBltMode(small, STRETCH_BLT_MODE.HALFTONE);
                var scaled = PInvoke.StretchBlt(small, 0, 0, outWidth, outHeight, full, 0, 0, width, height, ROP_CODE.SRCCOPY);
                PInvoke.SelectObject(small, smallPrevious);
                PInvoke.SelectObject(full, previous);
                frame = scaled ? GdiScreenCapturer.ReadPixels(small, smallBitmap, outWidth, outHeight) : null;
            }

            return frame is not null && !IsBlank(frame) ? frame : null;
        }
        finally
        {
            if (!smallBitmap.IsNull)
            {
                PInvoke.DeleteObject(smallBitmap);
            }

            if (!small.IsNull)
            {
                PInvoke.DeleteDC(small);
            }

            if (!fullBitmap.IsNull)
            {
                PInvoke.DeleteObject(fullBitmap);
            }

            if (!full.IsNull)
            {
                PInvoke.DeleteDC(full);
            }

            _ = PInvoke.ReleaseDC(HWND.Null, screen);
        }
    }

    /// <summary>
    /// True when the picture is all black: a window that cannot draw itself this way (some games, protected
    /// windows) comes out black, and a black box is worse than showing the program's name instead.
    /// </summary>
    internal static bool IsBlank(RawFrame frame)
    {
        var pixels = frame.Bgra;
        var step = Math.Max(1, frame.Width * frame.Height / 400) * 4;
        for (var i = 0; i + 2 < pixels.Length; i += step)
        {
            if (pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0)
            {
                return false;
            }
        }

        return true;
    }
}
