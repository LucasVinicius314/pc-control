using System.Diagnostics;

namespace PcControl.Core.Capture;

public enum StreamState
{
    Running,
    Paused,
    Locked,
    Failed,
}

public enum StepResult
{
    Sent,
    Unchanged,
    Paused,
    Locked,
    Failed,
}

/// <summary>
/// Produces the pictures for one stream of one phone. <see cref="StepAsync"/> does a single
/// capture-and-send; <see cref="RunAsync"/> repeats it at the requested frame rate.
/// </summary>
public sealed class ViewStreamer(
    IScreenCapturer capturer,
    IJpegEncoder encoder,
    ViewOptions options,
    Func<bool> isPaused)
{
    public const int JpegQuality = 65;

    private byte[]? _lastPixels;
    private int _lastCursorX = int.MinValue;
    private int _lastCursorY = int.MinValue;

    public ViewOptions Options { get; } = options;

    /// <summary>Captures once and sends the picture, unless nothing changed since the last one.</summary>
    public async Task<StepResult> StepAsync(Func<byte[], CancellationToken, Task> send, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(send);

        if (isPaused())
        {
            return StepResult.Paused;
        }

        if (!capturer.IsDesktopAvailable())
        {
            Forget();
            return StepResult.Locked;
        }

        var monitors = capturer.GetMonitors();
        if (!capturer.TryGetCursor(out var cursorX, out var cursorY))
        {
            (cursorX, cursorY) = (RegionPlanner.NoCursor, RegionPlanner.NoCursor);
        }

        var monitor = RegionPlanner.PickMonitor(monitors, Options.Monitor, cursorX, cursorY);
        if (monitor is null)
        {
            return StepResult.Failed;
        }

        var plan = Options.Stream == ViewChannel.Magnifier
            ? RegionPlanner.Magnifier(monitor.Bounds, cursorX, cursorY, Options.Width, Options.Height)
            : RegionPlanner.Display(monitor.Bounds, cursorX, cursorY, Options.Width);

        var frame = capturer.Capture(plan.Source, plan.OutputWidth, plan.OutputHeight);
        if (frame is null)
        {
            return StepResult.Failed;
        }

        // A still screen produces the same picture; skip it to save the phone's data. The display
        // preview ignores the cursor here, because the phone gets it separately and quickly (see
        // CursorStreamer). In the mini preview the marker is part of the picture, so it counts.
        var cursorMatters = Options.Stream == ViewChannel.Magnifier;
        if (_lastPixels is not null
            && (!cursorMatters || (plan.CursorX == _lastCursorX && plan.CursorY == _lastCursorY))
            // The same object means the capturer already knows nothing changed; no need to compare bytes.
            && (ReferenceEquals(frame.Bgra, _lastPixels) || frame.Bgra.AsSpan().SequenceEqual(_lastPixels)))
        {
            return StepResult.Unchanged;
        }

        var jpeg = encoder.Encode(frame, JpegQuality);
        await send(FrameCodec.Encode(Options.Stream, monitor.Index, plan.CursorX, plan.CursorY, jpeg), cancellationToken);

        _lastPixels = frame.Bgra;
        _lastCursorX = plan.CursorX;
        _lastCursorY = plan.CursorY;
        return StepResult.Sent;
    }

    /// <summary>
    /// Streams until cancelled. <paramref name="onState"/> is called only when the state changes, so
    /// the phone can show "paused" or "screen locked" without being flooded.
    /// </summary>
    public async Task RunAsync(
        Func<byte[], CancellationToken, Task> send,
        Func<StreamState, Task> onState,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(onState);

        var interval = TimeSpan.FromMilliseconds(1000.0 / Options.Fps);
        StreamState? reported = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var started = Stopwatch.GetTimestamp();
            var result = await StepAsync(send, cancellationToken);

            var state = result switch
            {
                StepResult.Paused => StreamState.Paused,
                StepResult.Locked => StreamState.Locked,
                StepResult.Failed => StreamState.Failed,
                _ => StreamState.Running,
            };

            if (state != reported)
            {
                reported = state;
                await onState(state);
            }

            // When the screen cannot be read, check less often instead of spinning.
            var wait = state == StreamState.Running
                ? interval - Stopwatch.GetElapsedTime(started)
                : TimeSpan.FromMilliseconds(500);

            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }
        }
    }

    private void Forget() => _lastPixels = null;
}
