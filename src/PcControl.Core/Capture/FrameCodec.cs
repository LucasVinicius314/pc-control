using System.Buffers.Binary;

namespace PcControl.Core.Capture;

public enum ViewChannel
{
    Magnifier,
    Display,
}

/// <summary>
/// Layout of a picture sent to the phone (a binary WebSocket frame, see docs/protocol/websocket.md):
/// the kind, the index of the monitor it shows, the cursor position inside the picture as two
/// little-endian int16 values (-1, -1 when the cursor is not in it), then the JPEG.
/// </summary>
public static class FrameCodec
{
    public const byte MagnifierKind = 0x10;
    public const byte DisplayKind = 0x11;
    public const int HeaderLength = 6;

    public static byte KindOf(ViewChannel stream) => stream == ViewChannel.Magnifier ? MagnifierKind : DisplayKind;

    public static byte[] Encode(ViewChannel stream, int monitorIndex, int cursorX, int cursorY, ReadOnlySpan<byte> jpeg)
    {
        var frame = new byte[HeaderLength + jpeg.Length];
        frame[0] = KindOf(stream);
        frame[1] = (byte)Math.Clamp(monitorIndex, 0, byte.MaxValue);
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(2), (short)Math.Clamp(cursorX, short.MinValue, short.MaxValue));
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(4), (short)Math.Clamp(cursorY, short.MinValue, short.MaxValue));
        jpeg.CopyTo(frame.AsSpan(HeaderLength));
        return frame;
    }

    public static bool TryDecodeHeader(
        ReadOnlySpan<byte> frame,
        out ViewChannel stream,
        out int monitorIndex,
        out int cursorX,
        out int cursorY)
    {
        stream = default;
        monitorIndex = 0;
        cursorX = cursorY = RegionPlanner.NoCursor;

        if (frame.Length <= HeaderLength || (frame[0] != MagnifierKind && frame[0] != DisplayKind))
        {
            return false;
        }

        stream = frame[0] == MagnifierKind ? ViewChannel.Magnifier : ViewChannel.Display;
        monitorIndex = frame[1];
        cursorX = BinaryPrimitives.ReadInt16LittleEndian(frame[2..]);
        cursorY = BinaryPrimitives.ReadInt16LittleEndian(frame[4..]);
        return true;
    }
}

/// <summary>
/// The mouse cursor on its own, so the phone can move its marker without waiting for a picture.
/// A message is the kind, the monitor index (<see cref="NoMonitor"/> if the cursor is on none), and
/// the position inside that monitor as two little-endian uint16 values scaled to 0-65535.
/// </summary>
public static class CursorCodec
{
    public const byte Kind = 0x12;
    public const byte NoMonitor = 0xFF;
    public const int Length = 6;
    public const int Scale = 65535;

    public static byte[] Encode(IReadOnlyList<MonitorInfo> monitors, int cursorX, int cursorY)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        var message = new byte[Length];
        message[0] = Kind;
        message[1] = NoMonitor;

        var monitor = monitors.FirstOrDefault(m => m.Bounds.Contains(cursorX, cursorY));
        if (monitor is not null)
        {
            var b = monitor.Bounds;
            message[1] = (byte)Math.Clamp(monitor.Index, 0, NoMonitor - 1);
            BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(2), ToScale(cursorX - b.X, b.Width));
            BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(4), ToScale(cursorY - b.Y, b.Height));
        }

        return message;
    }

    public static bool TryDecode(ReadOnlySpan<byte> message, out int monitorIndex, out double x, out double y)
    {
        monitorIndex = NoMonitor;
        x = y = 0;

        if (message.Length != Length || message[0] != Kind)
        {
            return false;
        }

        monitorIndex = message[1];
        x = BinaryPrimitives.ReadUInt16LittleEndian(message[2..]) / (double)Scale;
        y = BinaryPrimitives.ReadUInt16LittleEndian(message[4..]) / (double)Scale;
        return true;
    }

    private static ushort ToScale(int offset, int size) =>
        (ushort)Math.Clamp((int)Math.Round((double)offset * Scale / Math.Max(1, size - 1)), 0, Scale);
}
