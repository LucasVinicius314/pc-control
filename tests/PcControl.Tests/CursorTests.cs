using PcControl.Core.Capture;

namespace PcControl.Tests;

public class CursorCodecTests
{
    private static readonly IReadOnlyList<MonitorInfo> Monitors =
    [
        new MonitorInfo(0, "Left", new Rect(0, 0, 1920, 1080), IsPrimary: true),
        new MonitorInfo(1, "Right", new Rect(1920, 0, 2560, 1440), IsPrimary: false),
    ];

    [Fact]
    public void ACursor_IsSentAsAFractionOfItsMonitor()
    {
        var message = CursorCodec.Encode(Monitors, 960, 540);

        Assert.Equal(CursorCodec.Length, message.Length);
        Assert.True(CursorCodec.TryDecode(message, out var monitor, out var x, out var y));
        Assert.Equal(0, monitor);
        Assert.Equal(0.5, x, precision: 2);
        Assert.Equal(0.5, y, precision: 2);
    }

    [Fact]
    public void TheSecondMonitor_IsMeasuredFromItsOwnOrigin()
    {
        var message = CursorCodec.Encode(Monitors, 1920 + 2559, 1439);

        Assert.True(CursorCodec.TryDecode(message, out var monitor, out var x, out var y));
        Assert.Equal(1, monitor);
        Assert.Equal(1.0, x, precision: 6);
        Assert.Equal(1.0, y, precision: 6);
    }

    [Fact]
    public void TheCorners_MapToZeroAndOne()
    {
        CursorCodec.TryDecode(CursorCodec.Encode(Monitors, 0, 0), out _, out var x0, out var y0);
        CursorCodec.TryDecode(CursorCodec.Encode(Monitors, 1919, 1079), out _, out var x1, out var y1);

        Assert.Equal((0.0, 0.0), (x0, y0));
        Assert.Equal((1.0, 1.0), (x1, y1));
    }

    [Fact]
    public void ACursorOnNoMonitor_SaysSo()
    {
        var message = CursorCodec.Encode(Monitors, -50, 9000);

        Assert.True(CursorCodec.TryDecode(message, out var monitor, out _, out _));
        Assert.Equal(CursorCodec.NoMonitor, monitor);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x12, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0x12, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0x11, 0, 0, 0, 0, 0 })]
    public void BadMessages_AreRefused(byte[] message)
    {
        Assert.False(CursorCodec.TryDecode(message, out _, out _, out _));
    }
}

public class CursorStreamerTests
{
    private readonly FakeScreen _screen = new();
    private readonly List<byte[]> _sent = [];
    private bool _paused;

    private CursorStreamer Streamer() => new(_screen, () => _paused);

    private Task Send(byte[] message, CancellationToken token)
    {
        _sent.Add(message);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task TheFirstPosition_IsSent()
    {
        Assert.Equal(StepResult.Sent, await Streamer().StepAsync(Send, default));
        Assert.Single(_sent);
    }

    [Fact]
    public async Task AStillCursor_SendsNothingMore()
    {
        var streamer = Streamer();
        await streamer.StepAsync(Send, default);

        Assert.Equal(StepResult.Unchanged, await streamer.StepAsync(Send, default));
        Assert.Equal(StepResult.Unchanged, await streamer.StepAsync(Send, default));
        Assert.Single(_sent);
    }

    [Fact]
    public async Task AMovingCursor_SendsEveryNewPosition()
    {
        var streamer = Streamer();
        await streamer.StepAsync(Send, default);

        _screen.CursorX += 10;
        await streamer.StepAsync(Send, default);
        _screen.CursorY += 10;
        await streamer.StepAsync(Send, default);

        Assert.Equal(3, _sent.Count);
    }

    [Fact]
    public async Task WhilePaused_NothingIsSent_AndTheCursorIsSentAgainOnResuming()
    {
        var streamer = Streamer();
        await streamer.StepAsync(Send, default);
        _paused = true;

        Assert.Equal(StepResult.Paused, await streamer.StepAsync(Send, default));
        _paused = false;

        // Same position as before, but the phone may have missed a change while paused.
        Assert.Equal(StepResult.Sent, await streamer.StepAsync(Send, default));
    }

    [Fact]
    public async Task WhileLocked_NothingIsSent()
    {
        _screen.DesktopAvailable = false;

        Assert.Equal(StepResult.Locked, await Streamer().StepAsync(Send, default));
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task AnUnreadableCursor_IsAFailure()
    {
        _screen.CursorKnown = false;

        Assert.Equal(StepResult.Failed, await Streamer().StepAsync(Send, default));
    }

    [Fact]
    public async Task ANudge_SendsTheNewPositionAtOnce_EvenWithAVerySlowPollInterval()
    {
        using var streamer = new CursorStreamer(_screen, () => _paused, TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource();
        var run = streamer.RunAsync(Send, cts.Token);
        await WaitForAsync(() => _sent.Count == 1, "the first position");

        _screen.CursorX += 50;
        var before = Environment.TickCount64;
        streamer.Nudge();
        await WaitForAsync(() => _sent.Count == 2, "the nudged position");

        Assert.True(Environment.TickCount64 - before < 1000, "a nudge should not wait for the 5 s poll");
        await cts.CancelAsync();
        await StopAsync(run);
    }

    [Fact]
    public async Task ANudge_StillFindsAMoveThatLandedAMomentLater()
    {
        using var streamer = new CursorStreamer(_screen, () => _paused, TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource();
        var run = streamer.RunAsync(Send, cts.Token);
        await WaitForAsync(() => _sent.Count == 1, "the first position");

        // The nudge arrives first, the cursor moves a few milliseconds after (as input is processed).
        streamer.Nudge();
        await Task.Delay(2);
        _screen.CursorX += 50;

        await WaitForAsync(() => _sent.Count == 2, "the late move");
        await cts.CancelAsync();
        await StopAsync(run);
    }

    [Fact]
    public async Task ManyNudges_WithoutMovement_SendNothing()
    {
        using var streamer = new CursorStreamer(_screen, () => _paused, TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource();
        var run = streamer.RunAsync(Send, cts.Token);
        await WaitForAsync(() => _sent.Count == 1, "the first position");

        for (var i = 0; i < 20; i++)
        {
            streamer.Nudge();
            await Task.Delay(5);
        }

        Assert.Single(_sent);
        await cts.CancelAsync();
        await StopAsync(run);
    }

    [Fact]
    public void Nudging_AfterDisposal_IsHarmless()
    {
        var streamer = new CursorStreamer(_screen, () => _paused);
        streamer.Dispose();

        streamer.Nudge();
    }

    /// <summary>Waits for a cancelled loop to end. It either throws or returns, depending on where the cancel landed.</summary>
    private static async Task StopAsync(Task run)
    {
        try
        {
            await run;
        }
        catch (OperationCanceledException)
        {
            // Cancelled while waiting; that is the normal way out.
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, string what)
    {
        var deadline = Environment.TickCount64 + 3000;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(1);
        }

        Assert.Fail($"Timed out waiting for {what}.");
    }

    [Fact]
    public async Task Run_PollsFastEnoughToFollowAMovingCursor_AndStopsWhenCancelled()
    {
        using var cts = new CancellationTokenSource();
        var run = Streamer().RunAsync(Send, cts.Token);

        for (var i = 0; i < 20; i++)
        {
            _screen.CursorX = 100 + (i * 5);
            await Task.Delay(30);
        }

        await cts.CancelAsync();
        await StopAsync(run);

        // 20 moves over about 600 ms: a poll rate of tens per second sees most of them.
        Assert.True(_sent.Count >= 10, $"only {_sent.Count} positions were sent");
    }
}
