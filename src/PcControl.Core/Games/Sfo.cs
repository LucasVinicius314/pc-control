using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace PcControl.Core.Games;

/// <summary>
/// Reads the PARAM.SFO file every PlayStation 3 game has: a small table of settings such as the title
/// and the game's serial. Anything malformed gives an empty result instead of an error, because the
/// file comes from a game dump and one bad dump must not break the whole game list.
/// </summary>
public static class Sfo
{
    private const uint Magic = 0x46535000; // "\0PSF" read as little endian
    private const int HeaderSize = 20;
    private const int EntrySize = 16;
    private const int MaxEntries = 256;

    private const ushort FormatUtf8Special = 0x0004;
    private const ushort FormatUtf8 = 0x0204;
    private const ushort FormatInt32 = 0x0404;

    public static IReadOnlyDictionary<string, string> Parse(ReadOnlySpan<byte> file)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (file.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(file) != Magic)
        {
            return result;
        }

        var keyTable = BinaryPrimitives.ReadUInt32LittleEndian(file[8..]);
        var dataTable = BinaryPrimitives.ReadUInt32LittleEndian(file[12..]);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(file[16..]);
        if (count > MaxEntries)
        {
            return result;
        }

        for (var i = 0; i < count; i++)
        {
            var entry = HeaderSize + (i * EntrySize);
            if (entry + EntrySize > file.Length)
            {
                break;
            }

            var keyOffset = BinaryPrimitives.ReadUInt16LittleEndian(file[entry..]);
            var format = BinaryPrimitives.ReadUInt16LittleEndian(file[(entry + 2)..]);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(file[(entry + 4)..]);
            var dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(file[(entry + 12)..]);

            var key = ReadZeroTerminated(file, (long)keyTable + keyOffset);
            if (key is null)
            {
                continue;
            }

            var start = (long)dataTable + dataOffset;
            if (start < 0 || start + length > file.Length)
            {
                continue;
            }

            var data = file.Slice((int)start, (int)length);
            switch (format)
            {
                case FormatUtf8:
                case FormatUtf8Special:
                    result[key] = Encoding.UTF8.GetString(data).TrimEnd('\0');
                    break;
                case FormatInt32 when data.Length >= 4:
                    result[key] = BinaryPrimitives.ReadInt32LittleEndian(data).ToString(CultureInfo.InvariantCulture);
                    break;
            }
        }

        return result;
    }

    private static string? ReadZeroTerminated(ReadOnlySpan<byte> file, long start)
    {
        if (start < 0 || start >= file.Length)
        {
            return null;
        }

        var rest = file[(int)start..];
        var end = rest.IndexOf((byte)0);
        return end <= 0 ? null : Encoding.UTF8.GetString(rest[..end]);
    }
}
