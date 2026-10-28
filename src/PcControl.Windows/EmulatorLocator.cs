using System.ComponentModel;
using System.Diagnostics;

namespace PcControl.Windows;

/// <summary>Something that can be pointed at an emulator's program, and asked where it is.</summary>
public interface IEmulatorLocator
{
    /// <summary>Which emulator this is (see <c>GameSources</c>).</summary>
    string Source { get; }

    /// <summary>The name of the emulator's program, for the file picker.</summary>
    string ExeName { get; }

    /// <summary>"auto", or the path of the program or of its folder.</summary>
    string Setting { get; }

    string? FindExe();

    string? UnavailableReason { get; }

    void Configure(string setting);

    void Rescan();

    /// <summary>True when the path names the program, or a folder that has it.</summary>
    bool IsLocation(string path);

    /// <summary>Something that will stop games from running even though the emulator was found, or null.</summary>
    string? Warning { get; }
}

/// <summary>
/// Finding an emulator's program: the path set by the user, or (in "auto" mode) a copy that is running right
/// now, then the usual folders. What is found is remembered until it goes missing.
/// </summary>
public abstract class EmulatorLocator : IEmulatorLocator
{
    private readonly Func<IEnumerable<string>> _candidates;
    private string _setting;
    private string? _exe;

    /// <param name="setting">"auto", or the path of the program or of the folder it is in.</param>
    /// <param name="extraCandidates">Where to look in "auto" mode; tests use this to stay out of the real PC's folders.</param>
    /// <param name="defaultCandidates">Where to look when no list is given.</param>
    protected EmulatorLocator(string setting, Func<IEnumerable<string>>? extraCandidates, Func<IEnumerable<string>> defaultCandidates)
    {
        _setting = string.IsNullOrWhiteSpace(setting) ? "auto" : setting.Trim();
        _candidates = extraCandidates ?? defaultCandidates;
    }

    /// <summary>Guards what is remembered; the emulator's own caches use it too.</summary>
    protected object Gate { get; } = new();

    public abstract string Source { get; }

    public abstract string ExeName { get; }

    public abstract string? Warning { get; }

    /// <summary>The emulator's name as people say it, for messages.</summary>
    protected abstract string DisplayName { get; }

    /// <summary>The name of the setting in <c>games</c> in config.json, for messages.</summary>
    protected abstract string ConfigKey { get; }

    /// <summary>The program's file names, best first (PCSX2 has had more than one).</summary>
    protected abstract IReadOnlyList<string> ExeNames { get; }

    /// <summary>The names the program's process has, to find a copy that is running.</summary>
    protected abstract IReadOnlyList<string> ProcessNames { get; }

    /// <summary>"auto", or the path of the program or its folder, as set in the config or the desktop window.</summary>
    public string Setting
    {
        get
        {
            lock (Gate)
            {
                return _setting;
            }
        }
    }

    /// <summary>Changes where the emulator is looked for. Takes effect at once.</summary>
    public void Configure(string setting)
    {
        lock (Gate)
        {
            _setting = string.IsNullOrWhiteSpace(setting) ? "auto" : setting.Trim();
            _exe = null;
        }
    }

    /// <summary>Forgets where the emulator was found, so the next look searches again.</summary>
    public virtual void Rescan()
    {
        lock (Gate)
        {
            _exe = null;
        }
    }

    /// <summary>True when the path names the program, or a folder that has it.</summary>
    public bool IsLocation(string path) => FromSetting(path) is not null;

    public string? UnavailableReason
    {
        get
        {
            if (FindExe() is not null)
            {
                return null;
            }

            var setting = Setting;
            return setting.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? $"{DisplayName} was not found. Choose {ExeName} in the PC Control window on the PC, or set games.{ConfigKey} in config.json."
                : $"There is no {ExeName} at {setting}.";
        }
    }

    /// <summary>The path of the program, or null. Looked up again while it is missing, so installing the emulator later works.</summary>
    public string? FindExe()
    {
        lock (Gate)
        {
            if (_exe is not null && File.Exists(_exe))
            {
                return _exe;
            }

            _exe = _setting.Equals("auto", StringComparison.OrdinalIgnoreCase) ? Detect() : FromSetting(_setting);
            return _exe;
        }
    }

    private string? FromSetting(string setting)
    {
        if (File.Exists(setting))
        {
            return Path.GetFullPath(setting);
        }

        return Directory.Exists(setting) ? ExeIn(setting) : null;
    }

    private string? ExeIn(string folder) =>
        ExeNames.Select(name => Path.Combine(folder, name)).FirstOrDefault(File.Exists) is { } found ? Path.GetFullPath(found) : null;

    private string? Detect()
    {
        // A copy that is running right now is the one the user means.
        foreach (var name in ProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
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
        }

        foreach (var folder in _candidates())
        {
            if (ExeIn(folder) is { } exe)
            {
                return exe;
            }
        }

        return null;
    }

    /// <summary>The release folders an emulator is usually unpacked into, such as "rpcs3-v0.0.37-...", newest first, under each parent that exists.</summary>
    protected static IEnumerable<string> UnpackedFolders(string pattern, params string[] parents)
    {
        foreach (var parent in parents)
        {
            if (!Directory.Exists(parent))
            {
                continue;
            }

            string[] found;
            try
            {
                found = [.. Directory.EnumerateDirectories(parent, pattern).OrderByDescending(Directory.GetLastWriteTimeUtc)];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue; // not readable; skip it
            }

            foreach (var folder in found)
            {
                yield return folder;
            }
        }
    }
}
