using PcControl.Core.Capture;

namespace PcControl.Tests;

/// <summary>A pretend screen: two side-by-side monitors, a movable cursor and a changeable picture.</summary>
public sealed class FakeScreen : IScreenCapturer, IJpegEncoder
{
    private readonly object _gate = new();

    public IReadOnlyList<MonitorInfo> Monitors { get; set; } =
    [
        new MonitorInfo(0, "Left", new Rect(0, 0, 1920, 1080), IsPrimary: true),
        new MonitorInfo(1, "Right", new Rect(1920, 0, 2560, 1440), IsPrimary: false),
    ];

    public int CursorX { get; set; } = 100;

    public int CursorY { get; set; } = 100;

    public bool CursorKnown { get; set; } = true;

    public bool DesktopAvailable { get; set; } = true;

    /// <summary>Changes the picture; each value gives different pixels.</summary>
    public byte PixelValue { get; set; } = 1;

    public bool FailCapture { get; set; }

    public List<(Rect Source, int Width, int Height)> Captures { get; } = [];

    public IReadOnlyList<MonitorInfo> GetMonitors() => Monitors;

    public bool TryGetCursor(out int x, out int y)
    {
        x = CursorX;
        y = CursorY;
        return CursorKnown;
    }

    public bool IsDesktopAvailable() => DesktopAvailable;

    public RawFrame? Capture(Rect source, int outputWidth, int outputHeight)
    {
        lock (_gate)
        {
            Captures.Add((source, outputWidth, outputHeight));
        }

        if (FailCapture)
        {
            return null;
        }

        var pixels = new byte[outputWidth * outputHeight * 4];
        Array.Fill(pixels, PixelValue);
        return new RawFrame(outputWidth, outputHeight, pixels);
    }

    /// <summary>The "JPEG" is just the size and quality, so tests can see what was encoded.</summary>
    public byte[] Encode(RawFrame frame, int quality) =>
        System.Text.Encoding.ASCII.GetBytes($"JPEG {frame.Width}x{frame.Height} q{quality}");
}
