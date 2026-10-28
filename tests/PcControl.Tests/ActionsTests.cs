using Microsoft.Extensions.Time.Testing;
using PcControl.Core.Actions;
using PcControl.Core.Capture;
using PcControl.Core.Input;

namespace PcControl.Tests;

public sealed class FakeUrlOpener : IUrlOpener
{
    public List<Uri> Opened { get; } = [];

    public bool Succeeds { get; set; } = true;

    public bool Open(Uri url)
    {
        Opened.Add(url);
        return Succeeds;
    }
}

public sealed class FakePowerBackend : IPowerBackend
{
    public List<(PowerAction Action, int Delay)> Performed { get; } = [];

    public int Cancels { get; private set; }

    public bool Succeeds { get; set; } = true;

    public bool Perform(PowerAction action, int delaySeconds)
    {
        if (Succeeds)
        {
            Performed.Add((action, delaySeconds));
        }

        return Succeeds;
    }

    public bool CancelScheduled()
    {
        Cancels++;
        return true;
    }
}

public sealed class FakeProcessCatalog : IProcessCatalog
{
    public List<RunningApp> Apps { get; set; } = [];

    public List<(int Pid, bool Force)> Closed { get; } = [];

    public List<int> Focused { get; } = [];

    public CloseOutcome Result { get; set; } = CloseOutcome.Ok;

    /// <summary>The window pictures to hand out, by process id; a program that is not here has none.</summary>
    public Dictionary<int, RawFrame> Frames { get; } = [];

    public List<(int Pid, int Width)> Previewed { get; } = [];

    public IReadOnlyList<RunningApp> List() => Apps;

    public RawFrame? Preview(RunningApp app, int maxWidth)
    {
        Previewed.Add((app.Pid, maxWidth));
        return Frames.GetValueOrDefault(app.Pid);
    }

    public CloseOutcome Focus(RunningApp app)
    {
        Focused.Add(app.Pid);
        return Result;
    }

    public CloseOutcome Close(RunningApp app, bool force)
    {
        Closed.Add((app.Pid, force));
        return Result;
    }
}

public class ProcessPreviewTests
{
    private static RawFrame Frame(byte value) => new(2, 2, new byte[16].Select(_ => value).ToArray());

    private static (ProcessService Service, FakeProcessCatalog Catalog, string Id) Make(TimeProvider? time = null)
    {
        var catalog = new FakeProcessCatalog { Apps = [new RunningApp(100, 1, "notepad", "Untitled - Notepad"), new RunningApp(200, 2, "chrome", "Anime", Minimized: true)] };
        catalog.Frames[100] = Frame(7);
        var service = new ProcessService(catalog, ownPid: 999, time);
        var id = service.GetList().Single(p => p.Name == "notepad").Id;
        return (service, catalog, id);
    }

    [Fact]
    public void TheList_SaysWhichWindowsAreMinimised()
    {
        var (service, _, _) = Make();

        var list = service.GetList();

        Assert.False(list.Single(p => p.Name == "notepad").Minimized);
        Assert.True(list.Single(p => p.Name == "chrome").Minimized);
    }

    [Fact]
    public void APreview_IsOnlyGivenForAnIdFromTheLatestList()
    {
        var (service, catalog, _) = Make();

        Assert.Equal(ProcessOutcome.Unknown, service.Preview("0000000000000000", 480).Outcome);
        Assert.Empty(catalog.Previewed);
    }

    [Fact]
    public void AProgramWithNothingToShow_IsOkWithoutAFrame()
    {
        var (service, _, _) = Make();
        var chrome = service.GetList().Single(p => p.Name == "chrome").Id;

        var (outcome, frame) = service.Preview(chrome, 480);

        Assert.Equal(ProcessOutcome.Ok, outcome);
        Assert.Null(frame);
    }

    [Fact]
    public void APicture_IsReusedBriefly_ThenDrawnAgain()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var (service, catalog, id) = Make(time);

        var first = service.Preview(id, 480).Frame;
        var second = service.Preview(id, 480).Frame;
        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Single(catalog.Previewed);

        time.Advance(TimeSpan.FromSeconds(4));
        service.Preview(id, 480);
        Assert.Equal(2, catalog.Previewed.Count);
    }

    [Fact]
    public void ADifferentWidth_IsADifferentPicture()
    {
        var (service, catalog, id) = Make();

        service.Preview(id, 320);
        service.Preview(id, 640);

        Assert.Equal([(100, 320), (100, 640)], catalog.Previewed);
    }
}

public class ControllerInfoTests
{
    [Theory]
    [InlineData(1, 0, ControllerPower.Wired, null)]
    [InlineData(2, 3, ControllerPower.Battery, BatteryLevel.Full)]
    [InlineData(3, 2, ControllerPower.Battery, BatteryLevel.Medium)]
    [InlineData(3, 1, ControllerPower.Battery, BatteryLevel.Low)]
    [InlineData(3, 0, ControllerPower.Battery, BatteryLevel.Empty)]
    [InlineData(3, 9, ControllerPower.Battery, null)]
    [InlineData(255, 3, ControllerPower.Unknown, null)]
    [InlineData(0, 3, ControllerPower.Unknown, null)]
    public void WhatXInputReports_BecomesPowerAndLevel(byte type, byte level, ControllerPower power, BatteryLevel? expected)
    {
        var info = ControllerInfo.FromXInput(2, type, level);

        Assert.Equal(2, info.Slot);
        Assert.Equal(power, info.Power);
        Assert.Equal(expected, info.Level);
    }
}

public class ControllerBatteryReadingTests
{
    private static readonly ControllerInfo Unknown = new(0, ControllerPower.Unknown, null);

    [Theory]
    [InlineData(1000, 1000, 100, BatteryLevel.Full)]
    [InlineData(700, 1000, 70, BatteryLevel.Full)]
    [InlineData(500, 1000, 50, BatteryLevel.Medium)]
    [InlineData(200, 1000, 20, BatteryLevel.Low)]
    [InlineData(100, 1000, 10, BatteryLevel.Empty)]
    [InlineData(0, 1000, 0, BatteryLevel.Empty)]
    [InlineData(1500, 1000, 100, BatteryLevel.Full)]
    public void TheCharge_BecomesAPercentAndALevel(int remaining, int full, int percent, BatteryLevel level)
    {
        var info = ControllerInfo.FromReading(0, new BatteryReading(true, false, remaining, full), Unknown);

        Assert.Equal(ControllerPower.Battery, info.Power);
        Assert.Equal(percent, info.Percent);
        Assert.Equal(level, info.Level);
        Assert.False(info.Charging);
    }

    [Fact]
    public void AControllerThatIsCharging_SaysSo()
    {
        var info = ControllerInfo.FromReading(1, new BatteryReading(true, true, 400, 1000), Unknown);

        Assert.True(info.Charging);
        Assert.Equal(1, info.Slot);
    }

    [Fact]
    public void AControllerWithoutABattery_IsOnACable()
    {
        var info = ControllerInfo.FromReading(0, new BatteryReading(false, false, null, null), Unknown);

        Assert.Equal(ControllerPower.Wired, info.Power);
        Assert.Null(info.Percent);
    }

    [Fact]
    public void WithoutAReport_WhatXInputSaidStands()
    {
        var xinput = new ControllerInfo(0, ControllerPower.Battery, BatteryLevel.Medium);

        Assert.Same(xinput, ControllerInfo.FromReading(0, null, xinput));
    }

    [Fact]
    public void AReportWithoutAFullCharge_HasNoPercent_AndKeepsTheXInputLevel()
    {
        var xinput = new ControllerInfo(0, ControllerPower.Battery, BatteryLevel.Low);

        var info = ControllerInfo.FromReading(0, new BatteryReading(true, false, 300, null), xinput);

        Assert.Null(info.Percent);
        Assert.Equal(BatteryLevel.Low, info.Level);
        Assert.Equal(ControllerPower.Battery, info.Power);
    }
}

public class UrlRulesTests
{
    [Theory]
    [InlineData("https://example.com/a?b=1", "https://example.com/a?b=1")]
    [InlineData("http://example.com", "http://example.com/")]
    [InlineData("  example.com/watch  ", "https://example.com/watch")]
    [InlineData("HTTPS://Example.com", "https://example.com/")]
    [InlineData("http://192.168.0.5:8080/x", "http://192.168.0.5:8080/x")]
    public void WebAddresses_AreAccepted_AndCleanedUp(string input, string expected)
    {
        Assert.True(UrlRules.TryNormalize(input, out var url));
        Assert.Equal(expected, url.AbsoluteUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:display")]
    [InlineData("ftp://example.com")]
    [InlineData("https://user:pass@example.com")]
    [InlineData("https://exa mple.com")]
    [InlineData("https://example.com/\r\nx")]
    [InlineData("https://")]
    public void Everything_Else_IsRefused(string? input)
    {
        Assert.False(UrlRules.TryNormalize(input, out _));
    }

    [Fact]
    public void TooLongAddresses_AreRefused()
    {
        Assert.False(UrlRules.TryNormalize("https://example.com/" + new string('a', UrlRules.MaxLength), out _));
    }
}

public class PowerServiceTests
{
    private readonly FakePowerBackend _backend = new();
    private readonly FakeTimeProvider _time = new();

    private PowerService Create() => new(_backend, _time);

    [Theory]
    [InlineData("lock", PowerAction.Lock)]
    [InlineData("SLEEP", PowerAction.Sleep)]
    [InlineData("restart", PowerAction.Restart)]
    [InlineData("shutdown", PowerAction.Shutdown)]
    [InlineData("signOut", PowerAction.SignOut)]
    public void ActionNames_AreParsed(string name, PowerAction expected)
    {
        Assert.True(PowerService.TryParseAction(name, out var action));
        Assert.Equal(expected, action);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hibernate")]
    [InlineData("shutdown /s")]
    public void UnknownNames_AreRefused(string? name)
    {
        Assert.False(PowerService.TryParseAction(name, out _));
    }

    [Theory]
    [InlineData(PowerAction.Lock)]
    [InlineData(PowerAction.Sleep)]
    public void LockAndSleep_NeedNoConfirmation_AndHappenAtOnce(PowerAction action)
    {
        var result = Create().Request(action, confirmed: false);

        Assert.Equal(PowerOutcome.Ok, result.Outcome);
        Assert.Equal([(action, 0)], _backend.Performed);
    }

    [Theory]
    [InlineData(PowerAction.Restart)]
    [InlineData(PowerAction.Shutdown)]
    [InlineData(PowerAction.SignOut)]
    public void SessionEndingActions_AreRefusedWithoutConfirmation(PowerAction action)
    {
        var result = Create().Request(action, confirmed: false);

        Assert.Equal(PowerOutcome.ConfirmationRequired, result.Outcome);
        Assert.Empty(_backend.Performed);
    }

    [Fact]
    public void ARestart_CountsDown_AndCanBeCancelled()
    {
        var service = Create();

        var result = service.Request(PowerAction.Restart, confirmed: true);

        Assert.Equal(PowerService.DefaultDelaySeconds, result.DelaySeconds);
        Assert.Equal(PowerAction.Restart, service.Pending!.Action);
        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(10, service.Pending!.SecondsLeft);

        Assert.True(service.Cancel());
        Assert.Null(service.Pending);
        Assert.Equal(1, _backend.Cancels);
    }

    [Fact]
    public void SigningOut_HasNoCountdown()
    {
        var service = Create();

        service.Request(PowerAction.SignOut, confirmed: true);

        Assert.Null(service.Pending);
        Assert.False(service.Cancel());
    }

    [Fact]
    public void TheCountdown_EndsByItself()
    {
        var service = Create();
        service.Request(PowerAction.Shutdown, confirmed: true);

        _time.Advance(TimeSpan.FromSeconds(PowerService.DefaultDelaySeconds + 1));

        Assert.Null(service.Pending);
        Assert.False(service.Cancel());
        Assert.Equal(0, _backend.Cancels);
    }

    [Fact]
    public void WhenWindowsRefuses_NothingIsPending()
    {
        _backend.Succeeds = false;
        var service = Create();

        var result = service.Request(PowerAction.Shutdown, confirmed: true);

        Assert.Equal(PowerOutcome.Failed, result.Outcome);
        Assert.Null(service.Pending);
    }
}

public class ProcessServiceTests
{
    private readonly FakeProcessCatalog _catalog = new()
    {
        Apps =
        [
            new RunningApp(10, 111, "zed", "Zed editor"),
            new RunningApp(20, 222, "chrome", "anime - Chrome"),
            new RunningApp(30, 333, "explorer", "File Explorer"),
            new RunningApp(40, 444, "svchost", "Some service window"),
            new RunningApp(50, 555, "me", "This app"),
            new RunningApp(60, 666, "blank", "  "),
        ],
    };

    private ProcessService Create() => new(_catalog, ownPid: 50);

    [Fact]
    public void TheList_HasOnlyUserPrograms_SortedByTitle()
    {
        var titles = Create().GetList().Select(p => p.Title).ToList();

        Assert.Equal(["anime - Chrome", "Zed editor"], titles);
    }

    [Fact]
    public void TheList_NeverExposesProcessNumbers()
    {
        var list = Create().GetList();

        Assert.All(list, p => Assert.Matches("^[0-9a-f]{16}$", p.Id));
    }

    [Fact]
    public void Closing_UsesTheIdFromTheList_AndTellsTheCatalog()
    {
        var service = Create();
        var chrome = service.GetList().Single(p => p.Name == "chrome");

        var (outcome, title) = service.Close(chrome.Id, force: true);

        Assert.Equal(ProcessOutcome.Ok, outcome);
        Assert.Equal("anime - Chrome", title);
        Assert.Equal([(20, true)], _catalog.Closed);
    }

    [Fact]
    public void Switching_UsesTheIdFromTheList_AndOnlyFocuses()
    {
        var service = Create();
        var chrome = service.GetList().Single(p => p.Name == "chrome");

        var (outcome, title) = service.Switch(chrome.Id);

        Assert.Equal(ProcessOutcome.Ok, outcome);
        Assert.Equal("anime - Chrome", title);
        Assert.Equal([20], _catalog.Focused);
        Assert.Empty(_catalog.Closed);
    }

    [Fact]
    public void SwitchingToAnUnlistedId_IsRefused()
    {
        var service = Create();
        service.GetList();

        Assert.Equal(ProcessOutcome.Unknown, service.Switch("0000000000000000").Outcome);
        Assert.Empty(_catalog.Focused);
    }

    [Fact]
    public void AnIdThatWasNeverListed_IsRefused()
    {
        var service = Create();
        service.GetList();

        Assert.Equal(ProcessOutcome.Unknown, service.Close(ProcessService.IdOf(new RunningApp(30, 333, "explorer", "x")), false).Outcome);
        Assert.Equal(ProcessOutcome.Unknown, service.Close("0000000000000000", false).Outcome);
        Assert.Empty(_catalog.Closed);
    }

    [Fact]
    public void NothingCanBeClosed_BeforeTheListWasFetched()
    {
        Assert.Equal(ProcessOutcome.Unknown, Create().Close(ProcessService.IdOf(_catalog.Apps[0]), false).Outcome);
    }

    [Theory]
    [InlineData(CloseOutcome.Gone, ProcessOutcome.Gone)]
    [InlineData(CloseOutcome.Failed, ProcessOutcome.Failed)]
    public void CatalogFailures_AreReported(CloseOutcome fromCatalog, ProcessOutcome expected)
    {
        var service = Create();
        var id = service.GetList()[0].Id;
        _catalog.Result = fromCatalog;

        Assert.Equal(expected, service.Close(id, false).Outcome);
    }
}
