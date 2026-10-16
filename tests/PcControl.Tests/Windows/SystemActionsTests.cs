using PcControl.Core.Actions;
using PcControl.Windows;
using Xunit.Abstractions;

namespace PcControl.Tests.Windows;

/// <summary>
/// Reads the real list of programs. Nothing is closed, locked or opened. Off by default:
/// <c>$env:PCCONTROL_REAL_INPUT = "1"; dotnet test --filter SystemActionsTests</c>.
/// </summary>
public class SystemActionsTests(ITestOutputHelper output)
{
    private static bool Enabled => Environment.GetEnvironmentVariable("PCCONTROL_REAL_INPUT") == "1";

    [Fact]
    public void TheRealList_HasProgramsWithWindows_AndWithoutSystemProcesses()
    {
        if (!Enabled)
        {
            return;
        }

        var service = new ProcessService(new WindowsProcessCatalog(), Environment.ProcessId);

        var list = service.GetList();

        foreach (var item in list)
        {
            output.WriteLine($"{item.Id}  {item.Name,-22} {item.Title}");
        }

        Assert.NotEmpty(list);
        Assert.DoesNotContain(list, p => ProcessService.Protected.Contains(p.Name));
    }

    [Fact]
    public void ANumberThatNowBelongsToAnotherProgram_IsNotTouched()
    {
        if (!Enabled)
        {
            return;
        }

        // This test's own process, claimed to have started at the wrong time: it must be left alone.
        var impostor = new RunningApp(Environment.ProcessId, StartTicks: 1, "testhost", "impostor");

        Assert.Equal(CloseOutcome.Gone, new WindowsProcessCatalog().Close(impostor, force: true));
    }

    [Fact]
    public void ANumberNobodyHas_IsGone()
    {
        if (!Enabled)
        {
            return;
        }

        Assert.Equal(CloseOutcome.Gone, new WindowsProcessCatalog().Close(new RunningApp(int.MaxValue, 1, "nothing", "nothing"), force: false));
    }

    [Fact]
    public void FocusingTheWindowThatIsAlreadyInFront_Works_AndLeavesTheScreenAlone()
    {
        if (!Enabled)
        {
            return;
        }

        var catalog = new WindowsProcessCatalog();
        var front = global::Windows.Win32.PInvoke.GetForegroundWindow();
        _ = global::Windows.Win32.PInvoke.GetWindowThreadProcessId(front, out var pid);
        var app = catalog.List().FirstOrDefault(a => a.Pid == (int)pid);
        if (app is null)
        {
            return; // the front window belongs to something without a listed window (for example the desktop)
        }

        Assert.Equal(CloseOutcome.Ok, catalog.Focus(app));
    }

    [Fact]
    public void FocusingAnImpostor_IsRefused()
    {
        if (!Enabled)
        {
            return;
        }

        Assert.Equal(CloseOutcome.Gone, new WindowsProcessCatalog().Focus(new RunningApp(Environment.ProcessId, 1, "x", "x")));
    }
}
