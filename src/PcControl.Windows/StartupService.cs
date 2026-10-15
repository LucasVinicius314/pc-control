using Microsoft.Win32;

namespace PcControl.Windows;

/// <summary>
/// Starts the app when the user signs in, through the per-user Run key (the same list Windows shows
/// under Settings > Apps > Startup). The registry is the source of truth, so turning it off in
/// Windows is respected.
/// </summary>
public sealed class StartupService(string valueName = "PcControl", string? runKeyPath = null, string? approvalKeyPath = null)
{
    private const string DefaultRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string DefaultApprovalKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private readonly string _runKey = runKeyPath ?? DefaultRunKey;
    private readonly string _approvalKey = approvalKeyPath ?? DefaultApprovalKey;

    /// <summary>True when the app is set to start at sign-in and Windows has not switched that off.</summary>
    public bool IsEnabled
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(_runKey);
            if (run?.GetValue(valueName) is not string)
            {
                return false;
            }

            // Windows records "turned off in Task Manager / Settings" here: the first byte is odd when disabled.
            using var approval = Registry.CurrentUser.OpenSubKey(_approvalKey);
            return approval?.GetValue(valueName) is not byte[] { Length: > 0 } state || (state[0] & 1) == 0;
        }
    }

    public void SetEnabled(bool enabled, string? executablePath = null)
    {
        using var run = Registry.CurrentUser.CreateSubKey(_runKey);

        if (enabled)
        {
            var path = executablePath ?? Environment.ProcessPath
                ?? throw new InvalidOperationException("The path of this program is not known.");
            run.SetValue(valueName, $"\"{path}\"");

            // Forget an earlier "disabled in Windows" so that turning it on here really turns it on.
            using var approval = Registry.CurrentUser.OpenSubKey(_approvalKey, writable: true);
            approval?.DeleteValue(valueName, throwOnMissingValue: false);
        }
        else
        {
            run.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }
}
