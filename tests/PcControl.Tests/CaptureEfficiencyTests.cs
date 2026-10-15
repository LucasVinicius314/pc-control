using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using PcControl.Core.Capture;
using PcControl.Core.Config;
using PcControl.Windows;

namespace PcControl.Tests;

/// <summary>A screen that counts how often it was asked, so wasted questions can be seen.</summary>
internal sealed class CountingScreen(FakeScreen inner) : IScreenCapturer
{
    public int MonitorQueries { get; private set; }

    public int DesktopQueries { get; private set; }

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        MonitorQueries++;
        return inner.GetMonitors();
    }

    public bool TryGetCursor(out int x, out int y) => inner.TryGetCursor(out x, out y);

    public bool IsDesktopAvailable()
    {
        DesktopQueries++;
        return inner.IsDesktopAvailable();
    }

    public RawFrame? Capture(Rect source, int outputWidth, int outputHeight) => inner.Capture(source, outputWidth, outputHeight);
}

public sealed class DxgiCapturerCachingTests : IDisposable
{
    private readonly FakeScreen _screen = new();
    private readonly CountingScreen _counting;
    private readonly FakeTimeProvider _time = new();
    private readonly DxgiScreenCapturer _capturer;

    public DxgiCapturerCachingTests()
    {
        _counting = new CountingScreen(_screen);
        _capturer = new DxgiScreenCapturer(_counting, NullLogger<DxgiScreenCapturer>.Instance, _time);
    }

    public void Dispose() => _capturer.Dispose();

    [Fact]
    public void TheMonitorList_IsAskedForOnce_NotOnEveryTick()
    {
        for (var i = 0; i < 200; i++)
        {
            _capturer.GetMonitors();
            _time.Advance(TimeSpan.FromMilliseconds(10)); // 200 ticks over two seconds... minus one
        }

        Assert.InRange(_counting.MonitorQueries, 1, 2);
    }

    [Fact]
    public void TheMonitorList_IsReadAgainAfterItsLifetime()
    {
        _capturer.GetMonitors();
        _time.Advance(DxgiScreenCapturer.MonitorCacheLifetime);
        _capturer.GetMonitors();

        Assert.Equal(2, _counting.MonitorQueries);
    }

    [Fact]
    public void ANewMonitorLayout_IsNoticedWithinTheCacheLifetime()
    {
        Assert.Equal(2, _capturer.GetMonitors().Count);

        _screen.Monitors = [_screen.Monitors[0]];
        _time.Advance(DxgiScreenCapturer.MonitorCacheLifetime + TimeSpan.FromMilliseconds(1));

        Assert.Single(_capturer.GetMonitors());
    }

    [Fact]
    public void TheLockedCheck_IsDoneAFewTimesASecond_NotSixtyTimes()
    {
        for (var i = 0; i < 100; i++)
        {
            _capturer.IsDesktopAvailable();
            _time.Advance(TimeSpan.FromMilliseconds(10)); // one second of 100 Hz asking
        }

        // 250 ms lifetime over one second: about four real checks.
        Assert.InRange(_counting.DesktopQueries, 3, 5);
    }

    [Fact]
    public void ALockedScreen_IsNoticedWithinTheCheckLifetime()
    {
        Assert.True(_capturer.IsDesktopAvailable());

        _screen.DesktopAvailable = false;
        Assert.True(_capturer.IsDesktopAvailable()); // still the cached answer
        _time.Advance(DxgiScreenCapturer.DesktopCheckLifetime);

        Assert.False(_capturer.IsDesktopAvailable());
    }

    [Fact]
    public void TheCursor_IsNeverCached()
    {
        _screen.CursorX = 10;
        Assert.True(_capturer.TryGetCursor(out var first, out _));
        _screen.CursorX = 500;
        Assert.True(_capturer.TryGetCursor(out var second, out _));

        Assert.Equal((10, 500), (first, second));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    public void NothingToCapture_IsRefusedWithoutTouchingTheGraphicsCard(int width, int height)
    {
        Assert.Null(_capturer.Capture(new Rect(0, 0, width, height), 10, 10));
    }
}

public class CursorIdlePollingTests
{
    private static readonly TimeSpan Active = TimeSpan.FromMilliseconds(16);

    [Fact]
    public void WhileTheCursorMoves_ItIsCheckedAtTheNormalRate()
    {
        Assert.Equal(Active, CursorStreamer.IntervalFor(TimeSpan.Zero, Active));
        Assert.Equal(Active, CursorStreamer.IntervalFor(TimeSpan.FromMilliseconds(999), Active));
    }

    [Fact]
    public void OnceItHasRested_ItIsCheckedLessOften()
    {
        Assert.Equal(CursorStreamer.IdlePollInterval, CursorStreamer.IntervalFor(CursorStreamer.IdleAfter, Active));
        Assert.Equal(CursorStreamer.IdlePollInterval, CursorStreamer.IntervalFor(TimeSpan.FromHours(1), Active));
    }

    [Fact]
    public void TheIdleRate_IsNeverFasterThanTheActiveOne()
    {
        var slow = TimeSpan.FromMilliseconds(200);

        Assert.Equal(slow, CursorStreamer.IntervalFor(TimeSpan.FromMinutes(1), slow));
    }

    [Fact]
    public void TheIdleRate_IsSlowerThanTheActiveOne()
    {
        Assert.True(CursorStreamer.IdlePollInterval > Active);
    }

    [Fact]
    public async Task AMoveAfterALongRest_StillArrivesQuickly_ThroughTheNudge()
    {
        var screen = new FakeScreen();
        var sent = new List<byte[]>();
        using var streamer = new CursorStreamer(screen, () => false, Active);
        using var cts = new CancellationTokenSource();
        var run = streamer.RunAsync((message, _) => { sent.Add(message); return Task.CompletedTask; }, cts.Token);

        await Task.Delay(CursorStreamer.IdleAfter + TimeSpan.FromMilliseconds(300)); // let it settle into slow polling
        var before = sent.Count;
        screen.CursorX += 40;
        var started = Environment.TickCount64;
        streamer.Nudge();
        while (sent.Count == before && Environment.TickCount64 - started < 2000)
        {
            await Task.Delay(1);
        }

        var took = Environment.TickCount64 - started;
        await cts.CancelAsync();
        try
        {
            await run;
        }
        catch (OperationCanceledException)
        {
            // the normal way out
        }

        Assert.True(sent.Count > before, "the moved cursor was never sent");
        Assert.True(took < 40, $"it took {took} ms; a nudge should not wait for the slow poll");
    }
}

public class CaptureConfigTests
{
    [Fact]
    public void TheDefault_IsAutomatic_AndValid()
    {
        var config = new AppConfig();

        Assert.Equal("auto", config.Capture.Method);
        Assert.Empty(ConfigValidator.Validate(config));
    }

    [Theory]
    [InlineData("auto", true)]
    [InlineData("gdi", true)]
    [InlineData("GDI", true)]
    [InlineData(" Auto ", true)]
    [InlineData("dxgi", false)]
    [InlineData("", false)]
    [InlineData("magic", false)]
    public void TheMethod_IsValidated(string method, bool valid)
    {
        var config = new AppConfig { Capture = new CaptureConfig { Method = method } };

        Assert.Equal(valid, ConfigValidator.Validate(config).Count == 0);
    }
}
