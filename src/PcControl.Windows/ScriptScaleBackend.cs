using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PcControl.Core.Capture;
using PcControl.Core.Display;

namespace PcControl.Windows;

/// <summary>
/// The last resort: runs a script or program the user chose in config.json (for example their old
/// AutoHotkey script that flips the scale through Windows Settings). It cannot say which scale it
/// produced, so <see cref="DpiToggleService"/> checks the result with another backend. The path
/// comes only from the config file, never from the phone.
/// </summary>
public sealed class ScriptScaleBackend(string path, ILogger<ScriptScaleBackend> logger) : IDisplayScaleBackend
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public string Name => "script";

    public bool CanSet => File.Exists(path);

    public ScaleInfo? ReadScale(MonitorInfo monitor) => null;

    public bool ApplyScale(MonitorInfo monitor, int percent)
    {
        if (!File.Exists(path))
        {
            logger.LogWarning("The fallback scale script {Path} does not exist.", path);
            return false;
        }

        try
        {
            logger.LogInformation("Running the fallback scale script {Path}", path);

            // The script flips between two scales on its own; it is not told which one is wanted.
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory,
                CreateNoWindow = true,
            });

            return process is not null && (process.WaitForExit(Timeout) ? process.ExitCode == 0 : Kill(process));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            logger.LogWarning(ex, "The fallback scale script could not be started.");
            return false;
        }
    }

    private bool Kill(Process process)
    {
        logger.LogWarning("The fallback scale script did not finish in {Seconds} s and was stopped.", Timeout.TotalSeconds);
        process.Kill(entireProcessTree: true);
        return false;
    }
}
