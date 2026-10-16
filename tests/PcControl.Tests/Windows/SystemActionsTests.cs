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
}
