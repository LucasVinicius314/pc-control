using Microsoft.Extensions.Logging.Abstractions;
using PcControl.Core.Display;
using PcControl.Windows;

namespace PcControl.Tests.Windows;

/// <summary>
/// Talks to the real display settings. These only READ, except the last test, which asks for the
/// scale a monitor already has (so nothing visibly changes). Off by default; run with
/// <c>$env:PCCONTROL_REAL_INPUT = "1"; dotnet test --filter DisplayScaleBackendTests</c>.
/// </summary>
public static class DisplayScaleBackendTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("PCCONTROL_REAL_INPUT") == "1";

    [Fact]
    public static void TheUndocumentedApi_AgreesWithTheDocumentedOne_OnEveryRealMonitor()
    {
        if (!Enabled)
        {
            return;
        }

        var monitors = new GdiScreenCapturer().GetMonitors();
        var undocumented = new DisplayConfigScaleBackend(NullLogger<DisplayConfigScaleBackend>.Instance);
        var documented = new EffectiveDpiScaleBackend();
        Assert.NotEmpty(monitors);

        foreach (var monitor in monitors)
        {
            var viaConfig = undocumented.ReadScale(monitor);
            var viaDpi = documented.ReadScale(monitor);

            Assert.True(viaConfig is not null, $"display-config could not read {monitor.Name}");
            Assert.True(viaDpi is not null, $"GetDpiForMonitor could not read {monitor.Name}");

            // The step table is only trustworthy if both ways of asking give the same answer.
            Assert.Equal(viaDpi.Current, viaConfig.Current);
            Assert.True(viaConfig.Recommended is >= 100);
            Assert.True(viaConfig.Maximum >= viaConfig.Current, "the current scale cannot exceed the maximum");
            Assert.True(DpiScaleTable.IsKnownScale(viaConfig.Current));
        }
    }

    [Fact]
    public static void AskingForTheScaleAMonitorAlreadyHas_IsAccepted_AndChangesNothing()
    {
        if (!Enabled)
        {
            return;
        }

        var monitor = new GdiScreenCapturer().GetMonitors().First(m => m.IsPrimary);
        var undocumented = new DisplayConfigScaleBackend(NullLogger<DisplayConfigScaleBackend>.Instance);
        var before = undocumented.ReadScale(monitor);
        Assert.NotNull(before);

        var accepted = undocumented.ApplyScale(monitor, before.Current);

        Assert.True(accepted, "Windows should accept a request for the scale it already has");
        Assert.Equal(before.Current, undocumented.ReadScale(monitor)!.Current);
    }

    [Fact]
    public static void AnUnknownMonitorName_IsRefused_NotGuessed()
    {
        if (!Enabled)
        {
            return;
        }

        var real = new GdiScreenCapturer().GetMonitors()[0];
        var fake = real with { Name = @"\.\DISPLAY99" };
        var undocumented = new DisplayConfigScaleBackend(NullLogger<DisplayConfigScaleBackend>.Instance);

        Assert.Null(undocumented.ReadScale(fake));
        Assert.False(undocumented.ApplyScale(fake, 100));
    }
}

/// <summary>Prints what Windows reports for each monitor, so the numbers can be read rather than trusted.</summary>
public class DisplayScaleReport(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void ReportTheRealScales()
    {
        if (Environment.GetEnvironmentVariable("PCCONTROL_REAL_INPUT") != "1")
        {
            return;
        }

        var undocumented = new DisplayConfigScaleBackend(NullLogger<DisplayConfigScaleBackend>.Instance);
        var documented = new EffectiveDpiScaleBackend();
        foreach (var m in new GdiScreenCapturer().GetMonitors())
        {
            var a = undocumented.ReadScale(m);
            var b = documented.ReadScale(m);
            output.WriteLine($"monitor {m.Index} {m.Name} {m.Bounds.Width}x{m.Bounds.Height}{(m.IsPrimary ? " (primary)" : string.Empty)}: " +
                             $"display-config says {a?.Current}% (recommended {a?.Recommended}%, max {a?.Maximum}%), GetDpiForMonitor says {b?.Current}%");
        }
    }
}

/// <summary>
/// Actually changes the display scale of the primary monitor (100% to 200% and back), so windows
/// on it will resize. Off unless you ask for it with its own switch:
/// <c>$env:PCCONTROL_REAL_DPI_CHANGE = "1"; dotnet test --filter RealDisplayScaleChange</c>
/// </summary>
public class RealDisplayScaleChange
{
    [Fact]
    public async Task SwitchingToTheHighScale_AndBack_ReallyChangesTheMonitor_AndIsRestored()
    {
        if (Environment.GetEnvironmentVariable("PCCONTROL_REAL_DPI_CHANGE") != "1")
        {
            return;
        }

        var screen = new GdiScreenCapturer();
        using var service = new DpiToggleService(
            screen,
            [new DisplayConfigScaleBackend(NullLogger<DisplayConfigScaleBackend>.Instance), new EffectiveDpiScaleBackend()],
            new PcControl.Core.Config.DpiConfig { Monitor = "primary" });
        var status = service.Check();
        Assert.True(status.Available, status.Reason);
        var original = status.Percent!.Value;

        try
        {
            // Start from the low scale so the first toggle goes up.
            if (original != service.LowPercent)
            {
                Assert.Equal(DpiOutcome.Ok, (await service.ToggleAsync()).Outcome);
            }

            var up = await service.ToggleAsync();
            Assert.Equal(DpiOutcome.Ok, up.Outcome);
            Assert.Equal(service.HighPercent, up.Current);

            // Independent confirmation through the documented call.
            Assert.Equal(service.HighPercent, new EffectiveDpiScaleBackend().ReadScale(up.Monitor!)!.Current);

            var down = await service.ToggleAsync();
            Assert.Equal(DpiOutcome.Ok, down.Outcome);
            Assert.Equal(service.LowPercent, down.Current);
        }
        finally
        {
            // Leave the monitor as it was found, whatever happened above.
            for (var attempt = 0; attempt < 3 && service.Read().Percent != original; attempt++)
            {
                _ = await service.ToggleAsync();
            }
        }
    }
}
