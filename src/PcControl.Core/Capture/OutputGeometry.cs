namespace PcControl.Core.Capture;

/// <summary>
/// How a monitor's picture is turned relative to the image the graphics driver hands out. The values
/// match DXGI_MODE_ROTATION. A portrait monitor is often a landscape panel turned on its side, and the
/// duplicated image then arrives in the panel's own (landscape) orientation.
/// </summary>
public enum OutputRotation
{
    Identity = 1,
    Rotate90 = 2,
    Rotate180 = 3,
    Rotate270 = 4,
}

/// <summary>Pure coordinate arithmetic for reading a monitor's picture. No Windows calls.</summary>
public static class OutputGeometry
{
    /// <summary>
    /// Where the pixel at (<paramref name="x"/>, <paramref name="y"/>) of the monitor as the user sees it
    /// (<paramref name="width"/> by <paramref name="height"/>) lives in the driver's image. For a quarter
    /// turn the driver's image is <paramref name="height"/> wide and <paramref name="width"/> tall.
    /// </summary>
    public static (int X, int Y) ToImage(OutputRotation rotation, int x, int y, int width, int height) => rotation switch
    {
        OutputRotation.Rotate90 => (y, width - 1 - x),
        OutputRotation.Rotate180 => (width - 1 - x, height - 1 - y),
        OutputRotation.Rotate270 => (height - 1 - y, x),
        _ => (x, y),
    };

    /// <summary>The same mapping the other way: from a pixel of the driver's image to the monitor as seen.</summary>
    public static (int X, int Y) ToDesktop(OutputRotation rotation, int imageX, int imageY, int width, int height) => rotation switch
    {
        OutputRotation.Rotate90 => (width - 1 - imageY, imageX),
        OutputRotation.Rotate180 => (width - 1 - imageX, height - 1 - imageY),
        OutputRotation.Rotate270 => (imageY, height - 1 - imageX),
        _ => (imageX, imageY),
    };

    /// <summary>Size of the driver's image for a monitor that is <paramref name="width"/> by <paramref name="height"/> as seen.</summary>
    public static (int Width, int Height) ImageSize(OutputRotation rotation, int width, int height) =>
        rotation is OutputRotation.Rotate90 or OutputRotation.Rotate270 ? (height, width) : (width, height);

    /// <summary>The smallest rectangle of the driver's image that covers <paramref name="region"/> (given in the monitor's own coordinates).</summary>
    public static Rect ImageBox(OutputRotation rotation, Rect region, int width, int height)
    {
        var (ax, ay) = ToImage(rotation, region.X, region.Y, width, height);
        var (bx, by) = ToImage(rotation, region.Right - 1, region.Bottom - 1, width, height);

        var left = Math.Min(ax, bx);
        var top = Math.Min(ay, by);
        return new Rect(left, top, Math.Abs(ax - bx) + 1, Math.Abs(ay - by) + 1);
    }

    /// <summary>
    /// Which source positions to average for each output position along one axis, so a big picture
    /// shrinks smoothly instead of looking jagged. Returns <paramref name="samplesPerPixel"/> positions
    /// per output pixel, evenly spread across that pixel's footprint in the source.
    /// </summary>
    public static int[] SamplePositions(int sourceLength, int outputLength, int samplesPerPixel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceLength, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(outputLength, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(samplesPerPixel, 1);

        var positions = new int[outputLength * samplesPerPixel];
        var scale = (double)sourceLength / outputLength;
        for (var o = 0; o < outputLength; o++)
        {
            for (var k = 0; k < samplesPerPixel; k++)
            {
                var at = (int)((o + ((k + 0.5) / samplesPerPixel)) * scale);
                positions[(o * samplesPerPixel) + k] = Math.Clamp(at, 0, sourceLength - 1);
            }
        }

        return positions;
    }

    /// <summary>How many samples per axis to average for a given shrink factor: 1 when not shrinking, at most 4.</summary>
    public static int SamplesFor(int sourceLength, int outputLength) =>
        Math.Clamp((int)Math.Ceiling((double)sourceLength / Math.Max(1, outputLength)), 1, 4);
}
