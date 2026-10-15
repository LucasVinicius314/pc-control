using PcControl.Core.Capture;
using PcControl.Core.Config;
using PcControl.Core.Display;

namespace PcControl.Tests;

public class DpiScaleTableTests
{
    [Fact]
    public void TheTable_IsTheStandardWindowsList_InOrder()
    {
        Assert.Equal([100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500], DpiScaleTable.Percents);
    }

    [Theory]
    // minRel is minus the recommended step: a recommendation of 150% is step 2, so minRel is -2.
    [InlineData(-2, 0, 3, 150, 150, 225)]
    [InlineData(-2, -2, 3, 100, 150, 225)]
    [InlineData(-2, 2, 3, 200, 150, 225)]
    [InlineData(0, 0, 4, 100, 100, 200)]
    [InlineData(-4, 0, 7, 200, 200, 500)]
    [InlineData(-3, -1, 2, 150, 175, 225)]
    public void RelativeSteps_BecomePercentages(int minRel, int curRel, int maxRel, int current, int recommended, int maximum)
    {
        Assert.True(DpiScaleTable.TryRead(minRel, curRel, maxRel, out var info));
        Assert.Equal(new ScaleInfo(current, recommended, maximum), info);
    }

    [Theory]
    [InlineData(-2, -3, 3)] // current below 100%
    [InlineData(-2, 20, 3)] // current beyond the table
    [InlineData(-30, 0, 3)] // recommendation beyond the table
    [InlineData(-2, 3, 1)] // current larger than the maximum
    [InlineData(-2, 0, 40)] // maximum beyond the table
    public void NonsenseFromWindows_IsRefused(int minRel, int curRel, int maxRel)
    {
        Assert.False(DpiScaleTable.TryRead(minRel, curRel, maxRel, out var info));
        Assert.Null(info);
    }

    [Theory]
    [InlineData(200, 150, 2)]
    [InlineData(100, 150, -2)]
    [InlineData(150, 150, 0)]
    [InlineData(100, 100, 0)]
    [InlineData(500, 100, 11)]
    public void APercentage_BecomesARelativeStep(int percent, int recommended, int expected)
    {
        Assert.True(DpiScaleTable.TryToRelative(percent, recommended, out var relative));
        Assert.Equal(expected, relative);
    }

    [Theory]
    [InlineData(130, 150)]
    [InlineData(200, 130)]
    [InlineData(0, 100)]
    public void AnUnknownScale_IsRefused(int percent, int recommended)
    {
        Assert.False(DpiScaleTable.TryToRelative(percent, recommended, out _));
    }

    [Fact]
    public void Reading_AndAskingForTheSameScale_RoundTrip()
    {
        foreach (var recommended in DpiScaleTable.Percents)
        {
            foreach (var wanted in DpiScaleTable.Percents)
            {
                Assert.True(DpiScaleTable.TryToRelative(wanted, recommended, out var rel));
                var recIndex = DpiScaleTable.Percents.ToList().IndexOf(recommended);
                Assert.True(DpiScaleTable.TryRead(-recIndex, rel, DpiScaleTable.Percents.Count - 1 - recIndex, out var info));
                Assert.Equal(wanted, info!.Current);
            }
        }
    }

    [Theory]
    [InlineData(100.0, 100)]
    [InlineData(150.0, 150)]
    [InlineData(124.9, 125)]
    [InlineData(199, 200)]
    [InlineData(270, 250)]
    public void NearestScale_SnapsAMeasuredValue(double measured, int expected)
    {
        Assert.Equal(expected, DpiScaleTable.Nearest(measured));
    }
}

public class DpiConfigTests
{
    [Fact]
    public void Defaults_AreValid_And_Are100And200OnThePrimaryMonitor()
    {
        var config = new AppConfig();

        Assert.Empty(ConfigValidator.Validate(config));
        Assert.Equal(("primary", 100, 200, null), (config.Dpi.Monitor, config.Dpi.LowPercent, config.Dpi.HighPercent, config.Dpi.FallbackScriptPath));
    }

    [Theory]
    [InlineData(100, 200, "cursor", true)]
    [InlineData(125, 150, "primary", true)]
    [InlineData(100, 300, "2", true)]
    [InlineData(200, 100, "cursor", false)]
    [InlineData(100, 100, "cursor", false)]
    [InlineData(100, 130, "cursor", false)]
    [InlineData(100, 200, "tv", false)]
    [InlineData(100, 200, "99", false)]
    [InlineData(100, 200, "", false)]
    public void Settings_AreValidated(int low, int high, string monitor, bool valid)
    {
        var config = new AppConfig { Dpi = new DpiConfig { LowPercent = low, HighPercent = high, Monitor = monitor } };

        Assert.Equal(valid, ConfigValidator.Validate(config).Count == 0);
    }

    [Fact]
    public void ABlankScriptPath_IsRefused()
    {
        var config = new AppConfig { Dpi = new DpiConfig { FallbackScriptPath = "  " } };

        Assert.Contains(ConfigValidator.Validate(config), e => e.Contains("fallbackScriptPath", StringComparison.Ordinal));
    }
}

/// <summary>A pretend display backend. Everything about it can be made to fail.</summary>
public sealed class FakeScaleBackend(string name = "fake", bool canSet = true, FakeScaleBackend.Display? display = null) : IDisplayScaleBackend
{
    /// <summary>The pretend screen. Backends that share one see each other's changes, as with real Windows.</summary>
    public sealed class Display
    {
        public Dictionary<int, int> Scale { get; } = new() { [0] = 100, [1] = 100 };

        public (int Monitor, int Percent, int Remaining)? Pending { get; set; }
    }

    private readonly Display _display = display ?? new Display();

    public string Name { get; } = name;

    public bool CanSet { get; } = canSet;

    /// <summary>Current scale per monitor index.</summary>
    public Dictionary<int, int> Scale => _display.Scale;

    public int? Maximum { get; set; } = 300;

    public bool CanRead { get; set; } = true;

    public bool AcceptRequests { get; set; } = true;

    /// <summary>When false, a request is accepted but Windows never actually changes.</summary>
    public bool ChangeTakesEffect { get; set; } = true;

    /// <summary>How many reads happen before an accepted change shows up.</summary>
    public int ReadsUntilApplied { get; set; }

    public List<(int Monitor, int Percent)> Requests { get; } = [];

    public ScaleInfo? ReadScale(MonitorInfo monitor)
    {
        if (!CanRead)
        {
            return null;
        }

        if (_display.Pending is { } p && p.Monitor == monitor.Index)
        {
            if (p.Remaining <= 0)
            {
                Scale[p.Monitor] = p.Percent;
                _display.Pending = null;
            }
            else
            {
                _display.Pending = p with { Remaining = p.Remaining - 1 };
            }
        }

        return new ScaleInfo(Scale[monitor.Index], 150, Maximum);
    }

    public bool ApplyScale(MonitorInfo monitor, int percent)
    {
        Requests.Add((monitor.Index, percent));
        if (!AcceptRequests)
        {
            return false;
        }

        if (ChangeTakesEffect)
        {
            _display.Pending = (monitor.Index, percent, ReadsUntilApplied);
        }

        return true;
    }
}

public class DpiToggleServiceTests
{
    private readonly FakeScreen _screen = new();
    private readonly FakeScaleBackend _backend = new();

    private DpiToggleService Service(DpiConfig? config = null, params IDisplayScaleBackend[] backends) =>
        new(_screen, backends.Length > 0 ? backends : [_backend], config ?? new DpiConfig(), _ => Task.CompletedTask);

    [Fact]
    public void TheSelfTest_ReadsTheScale_AndTurnsTheToggleOn()
    {
        using var service = Service();

        var status = service.Check();

        Assert.True(status.Available);
        Assert.Null(status.Reason);
        Assert.Equal(100, status.Percent);
        Assert.Equal("fake", status.Backend);
        Assert.True(service.Available);
    }

    [Fact]
    public void BeforeTheSelfTest_TheToggleIsOff()
    {
        using var service = Service();

        Assert.False(service.Available);
    }

    [Fact]
    public void IfWindowsDoesNotAnswer_TheToggleTurnsItselfOff_WithAReason()
    {
        _backend.CanRead = false;
        using var service = Service();

        var status = service.Check();

        Assert.False(status.Available);
        Assert.Contains("Windows did not answer", status.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadOnlyBackends_AreNotEnough()
    {
        using var service = Service(null, new FakeScaleBackend("reader", canSet: false));

        var status = service.Check();

        Assert.False(status.Available);
        Assert.Contains("fallbackScriptPath", status.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadOnlyBackend_PlusAScriptBackend_IsAvailable()
    {
        var reader = new FakeScaleBackend("reader", canSet: false);
        var script = new FakeScaleBackend("script") { CanRead = false };
        using var service = Service(null, reader, script);

        Assert.True(service.Check().Available);
    }

    [Fact]
    public async Task Toggling_From100_Goes200()
    {
        using var service = Service();
        service.Check();

        var result = await service.ToggleAsync();

        Assert.Equal(DpiOutcome.Ok, result.Outcome);
        Assert.Equal((100, 200), (result.Previous, result.Current));
        Assert.Equal([(0, 200)], _backend.Requests);
        Assert.Equal(200, service.Status.Percent);
    }

    [Fact]
    public async Task Toggling_From200_Goes100()
    {
        _backend.Scale[0] = 200;
        using var service = Service();
        service.Check();

        var result = await service.ToggleAsync();

        Assert.Equal((200, 100), (result.Previous, result.Current));
    }

    [Theory]
    [InlineData(150)]
    [InlineData(125)]
    [InlineData(300)]
    public async Task AnyOtherScale_GoesToTheLowOne(int current)
    {
        _backend.Scale[0] = current;
        using var service = Service();
        service.Check();

        var result = await service.ToggleAsync();

        Assert.Equal(100, result.Current);
    }

    [Fact]
    public async Task TogglingTwice_ComesBack()
    {
        using var service = Service();
        service.Check();

        await service.ToggleAsync();
        var back = await service.ToggleAsync();

        Assert.Equal(100, back.Current);
    }

    [Fact]
    public async Task TheScalesAreConfigurable()
    {
        _backend.Scale[0] = 125;
        using var service = Service(new DpiConfig { LowPercent = 125, HighPercent = 175 });
        service.Check();

        var result = await service.ToggleAsync();

        Assert.Equal(175, result.Current);
    }

    [Fact]
    public async Task AMonitorThatCannotGoThatHigh_IsToldSo_AndNothingIsRequested()
    {
        _backend.Maximum = 150;
        using var service = Service();
        service.Check();

        var result = await service.ToggleAsync();

        Assert.Equal(DpiOutcome.NotSupported, result.Outcome);
        Assert.Contains("150%", result.Message, StringComparison.Ordinal);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public async Task AChangeThatAppearsAfterAMoment_IsWaitedFor()
    {
        _backend.ReadsUntilApplied = 5;
        using var service = Service();
        service.Check();

        var result = await service.ToggleAsync();

        Assert.Equal(DpiOutcome.Ok, result.Outcome);
        Assert.Equal(200, result.Current);
    }

    [Fact]
    public async Task AChangeThatNeverHappens_IsReportedAsAFailure_NotASuccess()
    {
        _backend.ChangeTakesEffect = false;
        using var service = Service();
        service.Check();

        var result = await service.ToggleAsync();

        Assert.Equal(DpiOutcome.Failed, result.Outcome);
        Assert.Equal(100, result.Current);
        Assert.Contains("200%", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArefusedRequest_FallsThroughToTheNextBackend()
    {
        var shared = new FakeScaleBackend.Display();
        var broken = new FakeScaleBackend("api", display: shared) { AcceptRequests = false };
        var script = new FakeScaleBackend("script", display: shared);
        using var service = Service(null, broken, script);
        service.Check();

        var result = await service.ToggleAsync();

        Assert.Equal(DpiOutcome.Ok, result.Outcome);
        Assert.Single(broken.Requests);
        Assert.Single(script.Requests);
    }

    [Fact]
    public async Task AnAcceptedButIneffectiveRequest_AlsoFallsThrough()
    {
        var shared = new FakeScaleBackend.Display();
        var lying = new FakeScaleBackend("api", display: shared) { ChangeTakesEffect = false };
        var script = new FakeScaleBackend("script", display: shared);
        using var service = Service(null, lying, script);
        service.Check();

        var result = await service.ToggleAsync();

        // The first backend said yes but nothing changed; only the second one's request took effect.
        Assert.Single(lying.Requests);
        Assert.Single(script.Requests);
        Assert.Equal(DpiOutcome.Ok, result.Outcome);
        Assert.Equal(200, result.Current);
    }

    [Fact]
    public async Task WhileUnavailable_NothingIsChanged()
    {
        _backend.CanRead = false;
        using var service = Service();
        service.Check();

        var result = await service.ToggleAsync();

        Assert.Equal(DpiOutcome.Unavailable, result.Outcome);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public async Task ASecondToggleWhileOneIsRunning_IsRefusedAsBusy_InsteadOfUndoingTheFirst()
    {
        var release = new TaskCompletionSource();
        _backend.ReadsUntilApplied = 1;
        using var service = new DpiToggleService(_screen, [_backend], new DpiConfig(), _ => release.Task);
        service.Check();

        var first = service.ToggleAsync();
        var second = await service.ToggleAsync();
        release.SetResult();
        var firstResult = await first;

        Assert.Equal(DpiOutcome.Busy, second.Outcome);
        Assert.Equal(DpiOutcome.Ok, firstResult.Outcome);
        Assert.Single(_backend.Requests);
    }

    [Fact]
    public async Task TheCursorsMonitor_CanBeChosen()
    {
        _screen.CursorX = 2500; // on the second monitor
        using var service = Service(new DpiConfig { Monitor = "cursor" });
        service.Check();

        var result = await service.ToggleAsync();

        Assert.Equal(1, result.Monitor!.Index);
        Assert.Equal([(1, 200)], _backend.Requests);
    }

    [Fact]
    public async Task ThePrimaryMonitor_IsTheDefaultTarget_WhereverTheCursorIs()
    {
        _screen.CursorX = 2500;
        using var service = Service();
        service.Check();

        var result = await service.ToggleAsync();

        Assert.Equal(0, result.Monitor!.Index);
    }

    [Fact]
    public async Task AMonitorNumber_CanBeChosen()
    {
        using var service = Service(new DpiConfig { Monitor = "1" });
        service.Check();

        var result = await service.ToggleAsync();

        Assert.Equal(1, result.Monitor!.Index);
    }

    [Fact]
    public async Task ABadMonitorNumber_FallsBackToTheCursorsMonitor()
    {
        _screen.CursorX = 2500;
        using var service = Service(new DpiConfig { Monitor = "9" });
        service.Check();

        var result = await service.ToggleAsync();

        Assert.Equal(1, result.Monitor!.Index);
    }

    [Fact]
    public void WithNoMonitors_TheSelfTestFails()
    {
        _screen.Monitors = [];
        using var service = Service();

        var status = service.Check();

        Assert.False(status.Available);
        Assert.Contains("No monitor", status.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_ReportsTheCurrentScale_WithoutChangingAvailability()
    {
        using var service = Service();
        service.Check();
        _backend.Scale[0] = 150;

        var status = service.Read();

        Assert.True(status.Available);
        Assert.Equal(150, status.Percent);
    }
}
