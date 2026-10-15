using PcControl.Core.Capture;
using PcControl.Windows;

namespace PcControl.Tests.Windows;

/// <summary>
/// Reads the real screen, so it needs an unlocked interactive desktop. It is off by default.
/// Run it with: <c>$env:PCCONTROL_REAL_INPUT = "1"; dotnet test --filter GdiScreenCapturerTests</c>
/// </summary>
public static class GdiScreenCapturerTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("PCCONTROL_REAL_INPUT") == "1";

    [Fact]
    public static void RealScreen_HasMonitors_AndTheDesktopIsAvailable()
    {
        if (!Enabled)
        {
            return;
        }

        var capturer = new GdiScreenCapturer();
        var monitors = capturer.GetMonitors();

        Assert.NotEmpty(monitors);
        Assert.All(monitors, m => Assert.True(m.Bounds.Width > 0 && m.Bounds.Height > 0));
        Assert.Single(monitors, m => m.IsPrimary);
        Assert.Equal(Enumerable.Range(0, monitors.Count), monitors.Select(m => m.Index));
        Assert.True(capturer.IsDesktopAvailable());
        Assert.True(capturer.TryGetCursor(out _, out _));
    }

    [Fact]
    public static void RealCapture_ProducesPixels_AndAValidJpeg_AtFullAndScaledSize()
    {
        if (!Enabled)
        {
            return;
        }

        var capturer = new GdiScreenCapturer();
        var encoder = new GdiPlusJpegEncoder();
        var primary = capturer.GetMonitors().First(m => m.IsPrimary);

        var full = capturer.Capture(new Rect(primary.Bounds.X, primary.Bounds.Y, 300, 200), 300, 200);
        var scaled = capturer.Capture(primary.Bounds, 640, (int)Math.Round(640.0 * primary.Bounds.Height / primary.Bounds.Width));

        Assert.NotNull(full);
        Assert.Equal((300, 200), (full.Width, full.Height));
        Assert.Equal(300 * 200 * 4, full.Bgra.Length);
        Assert.NotNull(scaled);
        Assert.Equal(640, scaled.Width);

        foreach (var frame in new[] { full, scaled })
        {
            var jpeg = encoder.Encode(frame, 65);
            Assert.True(jpeg.Length > 200, "a JPEG with real content is not tiny");
            Assert.Equal([0xFF, 0xD8], jpeg[..2]);
            Assert.Equal([0xFF, 0xD9], jpeg[^2..]);
        }
    }

    [Fact]
    public static void RealCapture_RejectsEmptyRegions()
    {
        if (!Enabled)
        {
            return;
        }

        var capturer = new GdiScreenCapturer();

        Assert.Null(capturer.Capture(new Rect(0, 0, 0, 10), 10, 10));
        Assert.Null(capturer.Capture(new Rect(0, 0, 10, 10), 0, 10));
    }
}
