using System.ComponentModel;
using System.Diagnostics;
using PcControl.Core.Games;

namespace PcControl.Windows;

/// <summary>
/// The PlayStation 2 games PCSX2 knows about. PCSX2 keeps its game folders in <c>inis\PCSX2.ini</c>; the
/// games are disc images in those folders. A game's title comes from its serial (read off the disc image)
/// and PCSX2's own <c>GameIndex.yaml</c>, falling back to the file name. Games are started through PCSX2's
/// command line. Nothing here changes PCSX2's files.
/// </summary>
public sealed class Pcsx2Library : EmulatorLocator, IGameLibrary
{
    private const long MaxImageBytes = 8 * 1024 * 1024;
    private const int MaxGames = 2000;
    private static readonly string[] CoverExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    private readonly bool _fullscreen;
    private readonly Func<ProcessStartInfo, bool> _start;
    private readonly string? _dataRoot;
    private readonly Func<string, byte[]?>? _fetchCover;
    private readonly HashSet<string> _noCover = new(StringComparer.OrdinalIgnoreCase);

    // What was read from each disc image, so the list does not read the images again every few seconds.
    private readonly Dictionary<string, (string Serial, string Title)> _titles = new(StringComparer.OrdinalIgnoreCase);
    private (string File, DateTime Stamp, IReadOnlyDictionary<string, string> Names)? _index;

    /// <param name="setting">"auto", or the path of pcsx2-qt.exe or of the folder it is in.</param>
    /// <param name="fullscreen">Start games fullscreen.</param>
    /// <param name="start">How a process is started; tests pass a fake.</param>
    /// <param name="extraCandidates">Where to look in "auto" mode; tests use this to stay out of the real PC's folders.</param>
    /// <param name="fetchCover">Gets the cover picture for a serial from the internet, or null; without it covers are only read from disk.</param>
    /// <param name="dataRoot">PCSX2's data folder; null means where PCSX2 keeps it (next to the program when it is portable, otherwise in Documents).</param>
    public Pcsx2Library(string setting, bool fullscreen, Func<ProcessStartInfo, bool>? start = null, Func<IEnumerable<string>>? extraCandidates = null, string? dataRoot = null, Func<string, byte[]?>? fetchCover = null)
        : base(setting, extraCandidates, DefaultCandidates)
    {
        _fullscreen = fullscreen;
        _start = start ?? StartProcess;
        _dataRoot = dataRoot;
        _fetchCover = fetchCover;
    }

    public override string Source => GameSources.Pcsx2;

    public override string ExeName => "pcsx2-qt.exe";

    protected override string DisplayName => "PCSX2";

    protected override string ConfigKey => "pcsx2Path";

    protected override IReadOnlyList<string> ExeNames => ["pcsx2-qt.exe", "pcsx2.exe"];

    protected override IReadOnlyList<string> ProcessNames => ["pcsx2-qt", "pcsx2"];

    /// <summary>Forgets where PCSX2 was found, and what was read from its files.</summary>
    public override void Rescan()
    {
        base.Rescan();
        lock (Gate)
        {
            _titles.Clear();
            _index = null;
            _noCover.Clear();
        }
    }

    public bool Available => FindExe() is not null;

    /// <summary>PCSX2's data folder: next to the program when it is portable (a portable.ini is there), otherwise in Documents.</summary>
    public string? DataRoot()
    {
        if (_dataRoot is not null)
        {
            return _dataRoot;
        }

        var exe = FindExe();
        if (exe is null)
        {
            return null;
        }

        var folder = Path.GetDirectoryName(exe)!;
        return File.Exists(Path.Combine(folder, "portable.ini")) || File.Exists(Path.Combine(folder, "portable.txt"))
            ? folder
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PCSX2");
    }

    public IReadOnlyList<GameEntry> GetGames()
    {
        var exe = FindExe();
        var root = DataRoot();
        if (exe is null || root is null)
        {
            return [];
        }

        string ini;
        try
        {
            ini = File.ReadAllText(Path.Combine(root, "inis", "PCSX2.ini"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var names = GameIndex(exe);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var games = new List<GameEntry>();

        foreach (var (rawFolder, recursive) in Ps2.ParseGameFolders(ini))
        {
            var folder = Path.IsPathRooted(rawFolder) ? rawFolder : Path.GetFullPath(Path.Combine(root, rawFolder));
            foreach (var file in DiscImagesIn(folder, recursive))
            {
                if (games.Count >= MaxGames)
                {
                    return games;
                }

                if (!seen.Add(file))
                {
                    continue;
                }

                var (serial, title) = TitleOf(file, names);
                games.Add(GameEntry.Create(title, serial, file, GameSources.Pcsx2));
            }
        }

        return games;
    }

    /// <summary>The cover: a picture in PCSX2's covers folder named after the serial or the title (as PCSX2 looks), or next to the game.</summary>
    public byte[]? GetImage(GameEntry game, GameImage image)
    {
        ArgumentNullException.ThrowIfNull(game);
        // PS2 discs have no large background picture, so the box art serves as both (the page blurs it behind).
        var stem = Path.GetFileNameWithoutExtension(game.LaunchPath);
        var names = new List<string>();
        if (game.Serial.Length > 0)
        {
            names.Add(game.Serial);
        }

        names.Add(game.Title);
        names.Add(stem);

        var folders = new List<string>();
        if (DataRoot() is { } root)
        {
            folders.Add(Path.Combine(root, "covers"));
        }

        if (Path.GetDirectoryName(game.LaunchPath) is { } own)
        {
            folders.Add(own);
            names.AddRange(["cover", "folder", "poster"]);
        }

        foreach (var folder in folders)
        {
            foreach (var name in names)
            {
                foreach (var extension in CoverExtensions)
                {
                    var candidate = Path.Combine(folder, Sanitize(name) + extension);
                    try
                    {
                        if (File.Exists(candidate) && new FileInfo(candidate).Length <= MaxImageBytes)
                        {
                            return File.ReadAllBytes(candidate);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                    {
                        // Try the next name.
                    }
                }
            }
        }

        return DownloadCover(game);
    }

    /// <summary>Gets a cover PCSX2 does not have yet and keeps it in PCSX2's covers folder, so PCSX2 shows it too. Asks once per game per run.</summary>
    private byte[]? DownloadCover(GameEntry game)
    {
        if (_fetchCover is null || game.Serial.Length == 0 || DataRoot() is not { } root)
        {
            return null;
        }

        lock (Gate)
        {
            if (!_noCover.Add(game.Serial))
            {
                return null;
            }
        }

        byte[]? bytes;
        try
        {
            bytes = _fetchCover(game.Serial);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null; // offline or slow: the first letter will do
        }

        // Only a real, reasonably sized JPEG is kept.
        if (bytes is not { Length: > 4 } || bytes.Length > MaxImageBytes || bytes[0] != 0xFF || bytes[1] != 0xD8)
        {
            return null;
        }

        try
        {
            var folder = Path.Combine(root, "covers");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, Sanitize(game.Serial) + ".jpg"), bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Could not keep it; still show it this time.
        }

        return bytes;
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>The public PS2 cover collection (the one PCSX2 itself downloads from), by serial. Null when there is none.</summary>
    public static byte[]? FetchCoverFromInternet(string serial)
    {
        using var response = Http.GetAsync($"https://raw.githubusercontent.com/xlenore/ps2-covers/main/covers/default/{Uri.EscapeDataString(serial)}.jpg").GetAwaiter().GetResult();
        return response.IsSuccessStatusCode ? response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult() : null;
    }

    /// <summary>The emulator needs a PS2 BIOS to run anything. If it cannot find one, starting a game only opens an error.</summary>
    public override string? Warning => BiosProblem();

    public string? LaunchProblem(GameEntry game) => BiosProblem();

    /// <summary>
    /// Checks what PCSX2 itself checks: the BIOS file it is set to use, or failing that any BIOS image in its
    /// BIOS folder. The folder in <c>PCSX2.ini</c> is relative to PCSX2's data folder, so moving things around
    /// can leave it pointing nowhere.
    /// </summary>
    private string? BiosProblem()
    {
        var root = DataRoot();
        if (root is null)
        {
            return null;
        }

        string ini;
        try
        {
            ini = File.ReadAllText(Path.Combine(root, "inis", "PCSX2.ini"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // not set up yet; PCSX2's own wizard deals with that
        }

        var folderSetting = Ps2.IniValue(ini, "Folders", "Bios") ?? "bios";
        var fileSetting = Ps2.IniValue(ini, "Filenames", "BIOS");
        var folder = Path.IsPathRooted(folderSetting) ? folderSetting : Path.GetFullPath(Path.Combine(root, folderSetting));

        try
        {
            if (fileSetting is not null && File.Exists(Path.Combine(folder, fileSetting)))
            {
                return null;
            }

            // PCSX2 falls back to any BIOS image in the folder; the ones it accepts are at least half a megabyte.
            if (Directory.Exists(folder) && Directory.EnumerateFiles(folder).Any(f => new FileInfo(f).Length >= 512 * 1024))
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // cannot tell; let PCSX2 decide
        }

        var found = FoundBiosFolder();
        var hint = found is null
            ? "Open PCSX2, then Settings, BIOS, and choose the folder that has it."
            : $"There is a BIOS in {found}: open PCSX2, then Settings, BIOS, and choose that folder.";
        return Directory.Exists(folder)
            ? $"PCSX2 has no PS2 BIOS in {folder}. {hint}"
            : $"PCSX2 is set to look for its PS2 BIOS in {folder}, which does not exist. {hint}";
    }

    /// <summary>A "bios" folder with a BIOS image in it next to the program or in the data folder, to point the user at.</summary>
    private string? FoundBiosFolder()
    {
        var candidates = new List<string>();
        if (FindExe() is { } exe)
        {
            candidates.Add(Path.Combine(Path.GetDirectoryName(exe)!, "bios"));
        }

        if (DataRoot() is { } root)
        {
            candidates.Add(Path.Combine(root, "bios"));
        }

        foreach (var candidate in candidates)
        {
            try
            {
                if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate).Any(f => new FileInfo(f).Length >= 512 * 1024))
                {
                    return candidate;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not readable; try the next.
            }
        }

        return null;
    }

    public bool Launch(GameEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);

        var exe = FindExe();
        if (exe is null || !File.Exists(game.LaunchPath) || BiosProblem() is not null)
        {
            return false;
        }

        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };

        // No main window, only the game; PCSX2 closes itself when the game ends.
        info.ArgumentList.Add("-nogui");
        if (_fullscreen)
        {
            info.ArgumentList.Add("-fullscreen");
        }

        info.ArgumentList.Add("--");
        info.ArgumentList.Add(game.LaunchPath);
        return _start(info);
    }

    private (string Serial, string Title) TitleOf(string file, IReadOnlyDictionary<string, string> names)
    {
        string key;
        try
        {
            var info = new FileInfo(file);
            key = $"{file}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            key = file;
        }

        lock (Gate)
        {
            if (_titles.TryGetValue(key, out var known))
            {
                return known;
            }
        }

        var stem = Path.GetFileNameWithoutExtension(file);
        var serial = string.Empty;
        if (Path.GetExtension(file).Equals(".iso", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                serial = Ps2.SerialFromSystemCnf(Ps2.ReadRootFile(stream, "SYSTEM.CNF")) ?? string.Empty;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Locked or unreadable: the file name will do.
            }
        }

        var title = serial.Length > 0 && names.TryGetValue(serial, out var known2) ? known2 : stem;
        lock (Gate)
        {
            _titles[key] = (serial, title);
        }

        return (serial, title);
    }

    /// <summary>The serial-to-name table from PCSX2's own files, read again only when the file changes.</summary>
    private IReadOnlyDictionary<string, string> GameIndex(string exe)
    {
        var file = Path.Combine(Path.GetDirectoryName(exe)!, "resources", "GameIndex.yaml");
        try
        {
            if (!File.Exists(file))
            {
                return new Dictionary<string, string>();
            }

            var stamp = File.GetLastWriteTimeUtc(file);
            lock (Gate)
            {
                if (_index is { } cached && cached.File == file && cached.Stamp == stamp)
                {
                    return cached.Names;
                }
            }

            using var reader = new StreamReader(file);
            var names = Ps2.ParseGameIndex(reader);
            lock (Gate)
            {
                _index = (file, stamp, names);
            }

            return names;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Dictionary<string, string>();
        }
    }

    private static IEnumerable<string> DiscImagesIn(string folder, bool recursive)
    {
        if (!Directory.Exists(folder))
        {
            yield break;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files.Where(Ps2.IsDiscImage).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            yield return file;
        }
    }

    private static string Sanitize(string name) =>
        string.Concat(name.Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c));

    private static bool StartProcess(ProcessStartInfo info)
    {
        try
        {
            using var process = Process.Start(info);
            return process is not null;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static List<string> DefaultCandidates()
    {
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        List<string> places =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PCSX2"),
            @"C:\PCSX2",
            @"D:\PCSX2",
            Path.Combine(user, "PCSX2"),
        ];

        // Release builds are unpacked into folders such as "pcsx2-v2.4.0-windows-x64-Qt", often inside an
        // "apps" folder, so one level down is searched too.
        places.AddRange(UnpackedFolders(
            "pcsx2*",
            Path.Combine(user, "Downloads"),
            Path.Combine(user, "Downloads", "apps"),
            Path.Combine(user, "Desktop"),
            Path.Combine(user, "Documents"),
            @"D:\"));
        return places;
    }
}
