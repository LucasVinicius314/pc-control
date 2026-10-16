using System.ComponentModel;
using System.Diagnostics;
using PcControl.Core.Games;

namespace PcControl.Windows;

/// <summary>
/// The PS3 games RPCS3 knows about, read from its own game list (<c>config/games.yml</c>) and each
/// game's <c>PARAM.SFO</c>, and started through RPCS3's command line. Nothing here changes RPCS3's files.
/// </summary>
public sealed class Rpcs3Library : IGameLibrary
{
    private const long MaxImageBytes = 8 * 1024 * 1024;

    private readonly string _setting;
    private readonly bool _fullscreen;
    private readonly Func<ProcessStartInfo, bool> _start;
    private readonly Func<IEnumerable<string>> _extraCandidates;
    private string? _exe;

    /// <param name="setting">"auto", or the path of rpcs3.exe or of the folder it is in.</param>
    /// <param name="fullscreen">Start games fullscreen (RPCS3 only honours this when started without its window).</param>
    /// <param name="start">How a process is started; tests pass a fake.</param>
    /// <param name="extraCandidates">More places to look in "auto" mode; tests use this to stay out of the real PC's folders.</param>
    public Rpcs3Library(string setting, bool fullscreen, Func<ProcessStartInfo, bool>? start = null, Func<IEnumerable<string>>? extraCandidates = null)
    {
        _setting = string.IsNullOrWhiteSpace(setting) ? "auto" : setting.Trim();
        _fullscreen = fullscreen;
        _start = start ?? StartProcess;
        _extraCandidates = extraCandidates ?? DefaultCandidates;
    }

    public bool Available => FindExe() is not null;

    public string? UnavailableReason =>
        FindExe() is not null
            ? null
            : _setting.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? "RPCS3 was not found. Set games.rpcs3Path in config.json to rpcs3.exe or its folder."
                : $"There is no rpcs3.exe at {_setting}.";

    /// <summary>The path of rpcs3.exe, or null. Looked up again while it is missing, so installing RPCS3 later works.</summary>
    public string? FindExe()
    {
        if (_exe is not null && File.Exists(_exe))
        {
            return _exe;
        }

        _exe = _setting.Equals("auto", StringComparison.OrdinalIgnoreCase) ? Detect() : FromSetting(_setting);
        return _exe;
    }

    public IReadOnlyList<GameEntry> GetGames()
    {
        var exe = FindExe();
        if (exe is null)
        {
            return [];
        }

        var root = Path.GetDirectoryName(exe)!;
        var file = Path.Combine(root, "config", "games.yml");
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var games = new List<GameEntry>();
        foreach (var (serial, rawPath) in Rpcs3GamesFile.Parse(text))
        {
            var folder = Resolve(rawPath, root);
            if (!Directory.Exists(folder))
            {
                continue;
            }

            games.Add(GameEntry.Create(TitleOf(folder) ?? Path.GetFileName(folder), serial, folder));
        }

        return games;
    }

    public byte[]? GetImage(GameEntry game, GameImage image)
    {
        ArgumentNullException.ThrowIfNull(game);

        var name = image == GameImage.Cover ? "ICON0.PNG" : "PIC1.PNG";
        var root = FindExe() is { } exe ? Path.GetDirectoryName(exe)! : null;

        string?[] candidates =
        [
            Path.Combine(game.LaunchPath, "PS3_GAME", name),
            Path.Combine(game.LaunchPath, name),
            root is null ? null : Path.Combine(root, "Icons", "game_icons", game.Serial, name),
        ];

        foreach (var candidate in candidates)
        {
            try
            {
                if (candidate is not null && File.Exists(candidate) && new FileInfo(candidate).Length <= MaxImageBytes)
                {
                    return File.ReadAllBytes(candidate);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Try the next place.
            }
        }

        return null;
    }

    public bool Launch(GameEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);

        var exe = FindExe();
        if (exe is null || !Directory.Exists(game.LaunchPath))
        {
            return false;
        }

        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        info.ArgumentList.Add("--no-gui");
        if (_fullscreen)
        {
            info.ArgumentList.Add("--fullscreen");
        }

        info.ArgumentList.Add(game.LaunchPath);
        return _start(info);
    }

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

    /// <summary>The game folder games.yml names, with RPCS3's own "$(EmulatorDir)" placeholder resolved.</summary>
    private static string Resolve(string rawPath, string root)
    {
        var path = rawPath.Replace("$(EmulatorDir)", root.Replace('\\', '/') + "/", StringComparison.OrdinalIgnoreCase);
        return Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar)).TrimEnd(Path.DirectorySeparatorChar);
    }

    private static string? TitleOf(string folder)
    {
        foreach (var file in new[] { Path.Combine(folder, "PS3_GAME", "PARAM.SFO"), Path.Combine(folder, "PARAM.SFO") })
        {
            try
            {
                if (File.Exists(file) && new FileInfo(file).Length < 1024 * 1024)
                {
                    var values = Sfo.Parse(File.ReadAllBytes(file));
                    if (values.TryGetValue("TITLE", out var title) && !string.IsNullOrWhiteSpace(title))
                    {
                        return title.Trim();
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Fall back to the folder name.
            }
        }

        return null;
    }

    private static string? FromSetting(string setting)
    {
        var exe = Directory.Exists(setting) ? Path.Combine(setting, "rpcs3.exe") : setting;
        return File.Exists(exe) ? Path.GetFullPath(exe) : null;
    }

    private string? Detect()
    {
        // A copy that is running right now is the one the user means.
        foreach (var process in Process.GetProcessesByName("rpcs3"))
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (path is not null && File.Exists(path))
                    {
                        return path;
                    }
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    // No access to that process; keep looking.
                }
            }
        }

        foreach (var folder in _extraCandidates())
        {
            var exe = Path.Combine(folder, "rpcs3.exe");
            if (File.Exists(exe))
            {
                return exe;
            }
        }

        return null;
    }

    private static List<string> DefaultCandidates()
    {
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var places = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RPCS3"),
            @"C:\RPCS3",
            @"D:\RPCS3",
            Path.Combine(user, "RPCS3"),
            Path.Combine(user, "Documents", "RPCS3"),
        };

        // Release builds are unpacked into folders such as "rpcs3-v0.0.37-18022-9c93ec0b_win64_msvc".
        foreach (var parent in new[] { Path.Combine(user, "Downloads"), Path.Combine(user, "Desktop"), Path.Combine(user, "Documents") })
        {
            if (Directory.Exists(parent))
            {
                places.AddRange(Directory.EnumerateDirectories(parent, "rpcs3*").OrderByDescending(Directory.GetLastWriteTimeUtc));
            }
        }

        return places;
    }
}
