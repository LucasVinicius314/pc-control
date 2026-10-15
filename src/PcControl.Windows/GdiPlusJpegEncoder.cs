using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PcControl.Core.Capture;

namespace PcControl.Windows;

/// <summary>Encodes screen pictures as JPEG with GDI+.</summary>
public sealed class GdiPlusJpegEncoder : IJpegEncoder
{
    private static readonly ImageCodecInfo Jpeg =
        ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);

    public byte[] Encode(RawFrame frame, int quality)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var handle = GCHandle.Alloc(frame.Bgra, GCHandleType.Pinned);
        try
        {
            // Format32bppRgb ignores the alpha byte, which GDI leaves as zero.
            using var bitmap = new Bitmap(
                frame.Width,
                frame.Height,
                frame.Width * 4,
                PixelFormat.Format32bppRgb,
                handle.AddrOfPinnedObject());

            using var parameters = new EncoderParameters(1);
            using var qualityParameter = new EncoderParameter(Encoder.Quality, (long)Math.Clamp(quality, 1, 100));
            parameters.Param[0] = qualityParameter;

            using var stream = new MemoryStream();
            bitmap.Save(stream, Jpeg, parameters);
            return stream.ToArray();
        }
        finally
        {
            handle.Free();
        }
    }
}
