using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using PcControl.Core.Actions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PcControl.Windows;

/// <summary>Opens links with the shell, which hands http and https addresses to the default browser.</summary>
public sealed class ShellUrlOpener : IUrlOpener
{
    public bool Open(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        // UrlRules has already limited this to http and https.
        if (url.Scheme is not ("http" or "https"))
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}

/// <summary>Power actions through Windows: lock and sleep directly, the rest through shutdown.exe.</summary>
public sealed class WindowsPowerBackend : IPowerBackend
{
    public bool Perform(PowerAction action, int delaySeconds)
    {
        var delay = delaySeconds.ToString(CultureInfo.InvariantCulture);
        switch (action)
        {
            case PowerAction.Lock:
                return PInvoke.LockWorkStation();
            case PowerAction.Sleep:
                return PInvoke.SetSuspendState(false, false, false) != 0;
            case PowerAction.Restart:
                return Shutdown("/r", "/t", delay, "/c", "PC Control: restarting. Cancel it from the phone, or run shutdown /a.");
            case PowerAction.Shutdown:
                return Shutdown("/s", "/t", delay, "/c", "PC Control: shutting down. Cancel it from the phone, or run shutdown /a.");
            case PowerAction.SignOut:
                return Shutdown("/l");
            default:
                return false;
        }
    }

    public bool CancelScheduled() => Shutdown("/a");

    private static bool Shutdown(params string[] arguments)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(info);
            if (process is null)
            {
                return false;
            }

            process.WaitForExit(10_000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}

/// <summary>Lists and closes programs that have a window.</summary>
public sealed class WindowsProcessCatalog : IProcessCatalog
{
    public IReadOnlyList<RunningApp> List()
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        var result = new List<RunningApp>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != sessionId || process.MainWindowHandle == 0 || string.IsNullOrWhiteSpace(process.MainWindowTitle))
                    {
                        continue;
                    }

                    result.Add(new RunningApp(process.Id, process.StartTime.Ticks, process.ProcessName, process.MainWindowTitle));
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // A process that exited meanwhile, or one we are not allowed to look at.
                }
            }
        }

        return result;
    }

    public CloseOutcome Focus(RunningApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        Process process;
        try
        {
            process = Process.GetProcessById(app.Pid);
        }
        catch (ArgumentException)
        {
            return CloseOutcome.Gone;
        }

        using (process)
        {
            try
            {
                if (process.StartTime.Ticks != app.StartTicks)
                {
                    return CloseOutcome.Gone;
                }

                var window = new HWND(process.MainWindowHandle);
                if (window.IsNull || !PInvoke.IsWindow(window))
                {
                    return CloseOutcome.Failed;
                }

                if (PInvoke.IsIconic(window))
                {
                    PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_RESTORE);
                }

                return BringToFront(window) ? CloseOutcome.Ok : CloseOutcome.Failed;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                return process.HasExited ? CloseOutcome.Gone : CloseOutcome.Failed;
            }
        }
    }

    /// <summary>
    /// Windows only lets the program that owns the foreground window change it. Joining the input queue
    /// of the current foreground window for a moment is the usual way to be allowed to.
    /// </summary>
    private static bool BringToFront(HWND window)
    {
        var foreground = PInvoke.GetForegroundWindow();
        var foregroundThread = foreground.IsNull ? 0u : PInvoke.GetWindowThreadProcessId(foreground, out _);
        var thisThread = PInvoke.GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != thisThread && PInvoke.AttachThreadInput(thisThread, foregroundThread, true);
        try
        {
            return PInvoke.SetForegroundWindow(window);
        }
        finally
        {
            if (attached)
            {
                PInvoke.AttachThreadInput(thisThread, foregroundThread, false);
            }
        }
    }

    public CloseOutcome Close(RunningApp app, bool force)
    {
        ArgumentNullException.ThrowIfNull(app);

        Process process;
        try
        {
            process = Process.GetProcessById(app.Pid);
        }
        catch (ArgumentException)
        {
            return CloseOutcome.Gone;
        }

        using (process)
        {
            try
            {
                if (process.StartTime.Ticks != app.StartTicks)
                {
                    return CloseOutcome.Gone; // the number now belongs to a different program
                }

                if (force)
                {
                    process.Kill();
                    return CloseOutcome.Ok;
                }

                return process.CloseMainWindow() ? CloseOutcome.Ok : CloseOutcome.Failed;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                return process.HasExited ? CloseOutcome.Gone : CloseOutcome.Failed;
            }
        }
    }
}
