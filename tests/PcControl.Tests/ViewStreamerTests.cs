using System.Text;
using PcControl.Core.Capture;

namespace PcControl.Tests;

public class ViewStreamerTests
{
    private readonly FakeScreen _screen = new();
    private readonly List<byte[]> _sent = [];
    private bool _paused;

    private ViewStreamer Streamer(ViewOptions options) => new(_screen, _screen, options, () => _paused);

    private static ViewOptions Magnifier(int monitor = ViewOptions.FollowCursor) =>
        new(ViewChannel.Magnifier, true, monitor, 200, 120, 15);

    private static ViewOptions Display(int monitor = ViewOptions.FollowCursor) =>
        new(ViewChannel.Display, true, monitor, 640, 0, 5);

    private Task Send(byte[] frame, CancellationToken token)
    {
        _sent.Add(frame);
        return Task.CompletedTask;
    }

    private static string Jpeg(byte[] frame) => Encoding.ASCII.GetString(frame, FrameCodec.HeaderLength, frame.Length - FrameCodec.HeaderLength);

    [Fact]
    public async Task Magnifier_SendsARegionAroundTheCursor_WithTheMarkerPosition()
    {
        _screen.CursorX = 500;
        _screen.CursorY = 300;

        var result = await Streamer(Magnifier()).StepAsync(Send, default);

        Assert.Equal(StepResult.Sent, result);
        var frame = Assert.Single(_sent);
        Assert.True(FrameCodec.TryDecodeHeader(frame, out var channel, out _, out var x, out var y));
        Assert.Equal(ViewChannel.Magnifier, channel);
        Assert.Equal((100, 60), (x, y));
        Assert.Equal($"JPEG 200x120 q{ViewStreamer.JpegQuality}", Jpeg(frame));
        Assert.Equal(new Rect(400, 240, 200, 120), Assert.Single(_screen.Captures).Source);
    }

    [Fact]
    public async Task Display_SendsTheWholeMonitorScaledDown()
    {
        _screen.CursorX = 960;
        _screen.CursorY = 540;

        await Streamer(Display()).StepAsync(Send, default);

        var frame = Assert.Single(_sent);
        Assert.True(FrameCodec.TryDecodeHeader(frame, out var channel, out _, out var x, out var y));
        Assert.Equal(ViewChannel.Display, channel);
        Assert.Equal((320, 180), (x, y));
        Assert.Equal("JPEG 640x360 q65", Jpeg(frame));
        Assert.Equal(new Rect(0, 0, 1920, 1080), _screen.Captures[0].Source);
    }

    [Fact]
    public async Task FollowCursor_SwitchesMonitorsWhenTheCursorMoves()
    {
        var streamer = Streamer(Display());

        _screen.CursorX = 100;
        await streamer.StepAsync(Send, default);
        _screen.CursorX = 2500;
        await streamer.StepAsync(Send, default);

        Assert.Equal(new Rect(0, 0, 1920, 1080), _screen.Captures[0].Source);
        Assert.Equal(new Rect(1920, 0, 2560, 1440), _screen.Captures[1].Source);
    }

    [Fact]
    public async Task AFixedMonitor_IsKeptEvenWhenTheCursorLeavesIt()
    {
        _screen.CursorX = 2500;

        await Streamer(Display(monitor: 0)).StepAsync(Send, default);

        Assert.Equal(new Rect(0, 0, 1920, 1080), Assert.Single(_screen.Captures).Source);
        Assert.True(FrameCodec.TryDecodeHeader(_sent[0], out _, out _, out var x, out _));
        Assert.Equal(RegionPlanner.NoCursor, x);
    }

    [Fact]
    public async Task AStillScreenAndCursor_IsNotSentTwice()
    {
        var streamer = Streamer(Magnifier());

        Assert.Equal(StepResult.Sent, await streamer.StepAsync(Send, default));
        Assert.Equal(StepResult.Unchanged, await streamer.StepAsync(Send, default));
        Assert.Single(_sent);
    }

    [Fact]
    public async Task AChangedPicture_IsSentAgain()
    {
        var streamer = Streamer(Magnifier());
        await streamer.StepAsync(Send, default);

        _screen.PixelValue = 9;

        Assert.Equal(StepResult.Sent, await streamer.StepAsync(Send, default));
        Assert.Equal(2, _sent.Count);
    }

    [Fact]
    public async Task InTheMiniPreview_AMovedMarker_IsSentAgain_EvenWithTheSamePicture()
    {
        // Near the edge the region cannot follow the cursor, so only the marker moves.
        _screen.CursorX = 5;
        _screen.CursorY = 5;
        var streamer = Streamer(Magnifier());
        await streamer.StepAsync(Send, default);

        _screen.CursorX = 40;

        Assert.Equal(StepResult.Sent, await streamer.StepAsync(Send, default));
    }

    [Fact]
    public async Task InTheDisplayPreview_AMovedCursor_DoesNotResendThePicture()
    {
        var streamer = Streamer(Display());
        await streamer.StepAsync(Send, default);

        _screen.CursorX += 200;

        // The cursor reaches the phone on its own channel, so a still screen stays unsent.
        Assert.Equal(StepResult.Unchanged, await streamer.StepAsync(Send, default));
        Assert.Single(_sent);
    }

    [Fact]
    public async Task EachFrame_SaysWhichMonitorItShows()
    {
        var streamer = Streamer(Display());

        _screen.CursorX = 100;
        await streamer.StepAsync(Send, default);
        _screen.CursorX = 2500;
        _screen.PixelValue = 9;
        await streamer.StepAsync(Send, default);

        Assert.True(FrameCodec.TryDecodeHeader(_sent[0], out _, out var first, out _, out _));
        Assert.True(FrameCodec.TryDecodeHeader(_sent[1], out _, out var second, out _, out _));
        Assert.Equal((0, 1), (first, second));
    }

    [Fact]
    public async Task WhileTheKillSwitchIsOn_NothingIsCaptured()
    {
        _paused = true;

        var result = await Streamer(Display()).StepAsync(Send, default);

        Assert.Equal(StepResult.Paused, result);
        Assert.Empty(_sent);
        Assert.Empty(_screen.Captures);
    }

    [Fact]
    public async Task WhenTheScreenIsLocked_NothingIsCaptured_AndTheNextFrameIsSentAfterUnlocking()
    {
        var streamer = Streamer(Display());
        await streamer.StepAsync(Send, default);
        _screen.DesktopAvailable = false;

        Assert.Equal(StepResult.Locked, await streamer.StepAsync(Send, default));
        Assert.Single(_screen.Captures);

        _screen.DesktopAvailable = true;
        Assert.Equal(StepResult.Sent, await streamer.StepAsync(Send, default));
    }

    [Fact]
    public async Task AFailedCapture_IsReported_AndSendsNothing()
    {
        _screen.FailCapture = true;

        Assert.Equal(StepResult.Failed, await Streamer(Display()).StepAsync(Send, default));
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task NoMonitors_IsAFailure()
    {
        _screen.Monitors = [];

        Assert.Equal(StepResult.Failed, await Streamer(Display()).StepAsync(Send, default));
    }

    [Fact]
    public async Task AnUnknownCursor_StillSendsAPictureWithoutAMarker()
    {
        _screen.CursorKnown = false;

        await Streamer(Display()).StepAsync(Send, default);

        Assert.True(FrameCodec.TryDecodeHeader(_sent[0], out _, out _, out var x, out var y));
        Assert.Equal((RegionPlanner.NoCursor, RegionPlanner.NoCursor), (x, y));
    }

    [Fact]
    public async Task Run_ReportsStateChangesOnly_AndStopsWhenCancelled()
    {
        var states = new List<StreamState>();
        var streamer = Streamer(new ViewOptions(ViewChannel.Magnifier, true, ViewOptions.FollowCursor, 100, 60, 20));
        using var cts = new CancellationTokenSource();

        var run = streamer.RunAsync(Send, s => { states.Add(s); return Task.CompletedTask; }, cts.Token);
        await Task.Delay(300);
        _paused = true;
        await Task.Delay(700);
        _paused = false;
        _screen.PixelValue = 7;
        await Task.Delay(700);
        await cts.CancelAsync();
        try
        {
            await run; // a cancel that lands between two pictures ends the loop cleanly
        }
        catch (OperationCanceledException)
        {
            // a cancel that lands while it waits ends it with this; both are the normal way out
        }

        Assert.Equal([StreamState.Running, StreamState.Paused, StreamState.Running], states);
        Assert.True(_sent.Count >= 2, "a picture before the pause and one after it");
    }
}
