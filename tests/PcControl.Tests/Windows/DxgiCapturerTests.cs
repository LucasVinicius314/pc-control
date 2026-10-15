using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using PcControl.Core.Capture;
using PcControl.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi;
using Xunit.Abstractions;

namespace PcControl.Tests.Windows;

/// <summary>
/// Compares Desktop Duplication with the older GDI capture on the real screen. Off by default:
/// <c>$env:PCCONTROL_REAL_INPUT = "1"; dotnet test --filter DxgiCapturerMatchesGdi</c>.
/// </summary>
public class DxgiCapturerMatchesGdi(ITestOutputHelper output)
{
    private static bool Enabled => Environment.GetEnvironmentVariable("PCCONTROL_REAL_INPUT") == "1";

    /// <summary>Average difference per colour channel (0 to 255) between two same-sized pictures.</summary>
    private static double MeanDifference(RawFrame a, RawFrame b)
    {
        Assert.Equal((a.Width, a.Height), (b.Width, b.Height));
        long total = 0;
        for (var i = 0; i < a.Bgra.Length; i += 4)
        {
            total += Math.Abs(a.Bgra[i] - b.Bgra[i]) + Math.Abs(a.Bgra[i + 1] - b.Bgra[i + 1]) + Math.Abs(a.Bgra[i + 2] - b.Bgra[i + 2]);
        }

        return total / (3.0 * (a.Bgra.Length / 4));
    }

    /// <summary>The screen can change between the two captures, so take the best of a few tries.</summary>
    private static double BestDifference(GdiScreenCapturer one, DxgiScreenCapturer other, Rect region, int outputWidth, int outputHeight)
    {
        var best = double.MaxValue;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var a = one.Capture(region, outputWidth, outputHeight);
            var b = other.Capture(region, outputWidth, outputHeight);
            Assert.NotNull(a);
            Assert.NotNull(b);
            best = Math.Min(best, MeanDifference(a, b));
            Thread.Sleep(30);
        }

        return best;
    }

    [Fact]
    public void ForEveryMonitor_TheNewCaptureShowsTheSamePictureAsTheOld()
    {
        if (!Enabled)
        {
            return;
        }

        Assert.True(DpiAwareness.EnsurePerMonitorV2());
        var gdi = new GdiScreenCapturer();
        using var dxgi = new DxgiScreenCapturer(gdi, NullLogger<DxgiScreenCapturer>.Instance);

        foreach (var monitor in gdi.GetMonitors())
        {
            var b = monitor.Bounds;
            var middle = new Rect(b.X + (b.Width / 2) - 200, b.Y + (b.Height / 2) - 150, 400, 300);
            var corner = new Rect(b.X + 10, b.Y + 10, 300, 200);

            var centre = BestDifference(gdi, dxgi, middle, 400, 300);
            var topLeft = BestDifference(gdi, dxgi, corner, 300, 200);
            var whole = BestDifference(gdi, dxgi, b, 640, (int)Math.Round(640.0 * b.Height / b.Width));
            output.WriteLine($"monitor {monitor.Index} {monitor.Name} {b.Width}x{b.Height}: centre diff {centre:F2}, top-left diff {topLeft:F2}, whole screen scaled diff {whole:F2}  (0 = identical)");

            Assert.True(centre < 8, $"monitor {monitor.Index}: the middle differs by {centre:F1}");
            Assert.True(topLeft < 8, $"monitor {monitor.Index}: the top-left differs by {topLeft:F1}");
            Assert.True(whole < 14, $"monitor {monitor.Index}: the whole screen differs by {whole:F1}");
        }

        output.WriteLine($"monitors using Desktop Duplication: {dxgi.ActiveDuplications} of {gdi.GetMonitors().Count}");
        Assert.Equal(gdi.GetMonitors().Count, dxgi.ActiveDuplications);
    }

    [Fact]
    public void AnUnchangedScreen_IsAnswered_WithTheVeryObjectItGaveBefore()
    {
        if (!Enabled)
        {
            return;
        }

        Assert.True(DpiAwareness.EnsurePerMonitorV2());
        using var dxgi = new DxgiScreenCapturer(new GdiScreenCapturer(), NullLogger<DxgiScreenCapturer>.Instance);
        var region = new Rect(10, 10, 120, 80);

        var same = 0;
        // The first requests may be answered by the older capture while the first picture is awaited;
        // wait until the same object comes back twice in a row.
        RawFrame? first = null;
        for (var wait = 0; wait < 80; wait++)
        {
            var next = dxgi.Capture(region, 120, 80);
            if (next is not null && ReferenceEquals(first, next))
            {
                break;
            }

            first = next;
            Thread.Sleep(25);
        }

        Assert.NotNull(first);
        for (var i = 0; i < 40; i++)
        {
            Thread.Sleep(25);
            var again = dxgi.Capture(region, 120, 80);
            if (ReferenceEquals(first, again))
            {
                same++;
            }
        }

        // The corner of a desktop is usually still; on a busy one some repeats are still expected.
        output.WriteLine($"{same} of 40 requests over one second came back without any new capture work");
        Assert.True(same > 0, "no request was ever answered from the cache");
    }
}
