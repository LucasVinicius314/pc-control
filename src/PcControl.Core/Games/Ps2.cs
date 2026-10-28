using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace PcControl.Core.Games;

/// <summary>
/// What is needed to give a PlayStation 2 disc image a proper title: read its serial from the disc, and
/// look the serial up in PCSX2's game list. Everything here is forgiving: an unreadable image just has no
/// serial, and the file name is used instead.
/// </summary>
public static partial class Ps2
{
    private const int SectorSize = 2048;
    private const int MaxDirectoryBytes = 1024 * 1024;

    /// <summary>
    /// Reads a small file from the top level of an ISO 9660 disc image (the kind a PS2 disc is). Null if this
    /// is not such an image or the file is not there.
    /// </summary>
    public static string? ReadRootFile(Stream image, string fileName, int maxBytes = 8192)
    {
        ArgumentNullException.ThrowIfNull(image);

        try
        {
            if (image.Length < 17L * SectorSize)
            {
                return null;
            }

            var volume = ReadAt(image, 16L * SectorSize, SectorSize);
            if (volume is null || volume[0] != 1 || Encoding.ASCII.GetString(volume, 1, 5) != "CD001")
            {
                return null;
            }

            // The root directory's record sits at a fixed place in the volume descriptor.
            var rootStart = BinaryPrimitives.ReadUInt32LittleEndian(volume.AsSpan(156 + 2));
            var rootLength = BinaryPrimitives.ReadUInt32LittleEndian(volume.AsSpan(156 + 10));
            if (rootLength is 0 or > MaxDirectoryBytes)
            {
                return null;
            }

            var directory = ReadAt(image, rootStart * (long)SectorSize, (int)rootLength);
            if (directory is null)
            {
                return null;
            }

            for (var position = 0; position < directory.Length;)
            {
                var recordLength = directory[position];
                if (recordLength == 0)
                {
                    // Records never straddle a sector; the rest of this one is padding.
                    position = ((position / SectorSize) + 1) * SectorSize;
                    continue;
                }

                if (position + recordLength > directory.Length || recordLength < 34)
                {
                    return null;
                }

                var nameLength = directory[position + 32];
                var isDirectory = (directory[position + 25] & 0b10) != 0;
                if (!isDirectory && position + 33 + nameLength <= directory.Length)
                {
                    var name = Encoding.ASCII.GetString(directory, position + 33, nameLength);
                    var semicolon = name.IndexOf(';', StringComparison.Ordinal);
                    if (string.Equals(semicolon >= 0 ? name[..semicolon] : name, fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        var start = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(position + 2));
                        var size = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(position + 10));
                        var data = ReadAt(image, start * (long)SectorSize, (int)Math.Min(size, (uint)maxBytes));
                        return data is null ? null : Encoding.ASCII.GetString(data);
                    }
                }

                position += recordLength;
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or NotSupportedException)
        {
            // A damaged or locked image: no serial.
        }

        return null;
    }

    /// <summary>
    /// The game's serial, for example SLUS-21300, from the text of a disc's <c>SYSTEM.CNF</c>
    /// (<c>BOOT2 = cdrom0:\SLUS_213.00;1</c>). Null if there is none.
    /// </summary>
    public static string? SerialFromSystemCnf(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0 || line[..equals].Trim().ToUpperInvariant() is not ("BOOT2" or "BOOT"))
            {
                continue;
            }

            var file = line[(equals + 1)..].Trim();
            file = file[(file.LastIndexOfAny(['\\', '/', ':']) + 1)..];
            var semicolon = file.IndexOf(';', StringComparison.Ordinal);
            if (semicolon >= 0)
            {
                file = file[..semicolon];
            }

            // SLUS_213.00 becomes SLUS-21300, the form PCSX2 uses.
            var serial = file.Replace(".", string.Empty, StringComparison.Ordinal).Replace('_', '-').ToUpperInvariant();
            if (SerialPattern().IsMatch(serial))
            {
                return serial;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads PCSX2's <c>GameIndex.yaml</c>: serial to game name. Only the two things needed are read, line by
    /// line, so the large file is never fully parsed as YAML.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseGameIndex(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? serial = null;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line[0] != ' ')
            {
                var key = line.TrimEnd();
                serial = key.EndsWith(':') && SerialPattern().IsMatch(key[..^1]) ? key[..^1] : null;
                continue;
            }

            if (serial is not null && line.StartsWith("  name:", StringComparison.Ordinal))
            {
                var value = Unquote(line[7..].Trim());
                if (value.Length > 0)
                {
                    names[serial] = value;
                }
            }
        }

        return names;
    }

    /// <summary>The game folders listed in PCSX2's <c>PCSX2.ini</c>, under [GameList].</summary>
    public static IReadOnlyList<(string Path, bool Recursive)> ParseGameFolders(string? ini)
    {
        var folders = new List<(string, bool)>();
        var inGameList = false;
        foreach (var raw in (ini ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inGameList = line.Equals("[GameList]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (!inGameList || equals <= 0)
            {
                continue;
            }

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (value.Length == 0)
            {
                continue;
            }

            if (key.Equals("RecursivePaths", StringComparison.OrdinalIgnoreCase))
            {
                folders.Add((value, true));
            }
            else if (key.Equals("Paths", StringComparison.OrdinalIgnoreCase))
            {
                folders.Add((value, false));
            }
        }

        return folders;
    }

    /// <summary>One value from an ini file: the line <c>Key = value</c> under <c>[Section]</c>. Null if it is not there.</summary>
    public static string? IniValue(string? ini, string section, string key)
    {
        var inSection = false;
        foreach (var raw in (ini ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inSection = line.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (inSection && equals > 0 && line[..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                var value = line[(equals + 1)..].Trim();
                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }

    /// <summary>Disc image types PCSX2 can start and that show in its game list.</summary>
    public static bool IsDiscImage(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() is ".iso" or ".chd" or ".cso" or ".gz" or ".bin" or ".img" or ".mdf" or ".nrg";

    private static byte[]? ReadAt(Stream stream, long offset, int count)
    {
        if (offset < 0 || count <= 0 || offset + count > stream.Length)
        {
            return null;
        }

        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[count];
        stream.ReadExactly(buffer);
        return buffer;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal)
            : value;

    [GeneratedRegex("^[A-Z]{4}-[0-9]{5}$", RegexOptions.IgnoreCase)]
    private static partial Regex SerialPattern();
}
