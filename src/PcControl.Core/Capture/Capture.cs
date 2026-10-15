namespace PcControl.Core.Capture;

/// <summary>A rectangle in physical screen pixels.</summary>
public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>True when the two rectangles share at least one pixel. Empty rectangles share nothing.</summary>
    public bool Intersects(Rect other) =>
        Width > 0 && Height > 0 && other.Width > 0 && other.Height > 0
        && X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
}

public sealed record MonitorInfo(int Index, string Name, Rect Bounds, bool IsPrimary);

/// <summary>Raw screen pixels: 4 bytes per pixel in B, G, R, A order, top row first, no padding.</summary>
public sealed record RawFrame(int Width, int Height, byte[] Bgra);

/// <summary>Reads the screen. The Windows implementation uses GDI; tests use a fake.</summary>
public interface IScreenCapturer
{
    /// <summary>The attached monitors, left to right then top to bottom, numbered from 0.</summary>
    IReadOnlyList<MonitorInfo> GetMonitors();

    bool TryGetCursor(out int x, out int y);

    /// <summary>False while the PC is locked or a UAC prompt is up: the screen cannot be read then.</summary>
    bool IsDesktopAvailable();

    /// <summary>
    /// Copies <paramref name="source"/> from the screen, scaled to the given output size. The mouse
    /// cursor is not part of the picture. Returns null if the capture failed.
    /// </summary>
    RawFrame? Capture(Rect source, int outputWidth, int outputHeight);
}

public interface IJpegEncoder
{
    byte[] Encode(RawFrame frame, int quality);
}
