using System.Diagnostics;

namespace PcControl.Core.Capture;

/// <summary>
/// Sends the mouse cursor position to a phone, on its own and at a high rate, so the marker on the
/// phone follows the finger without waiting for the (much slower) screen pictures.
/// </summary>
/// <remarks>
/// The cursor is polled many times a second. For the case that matters most, the phone itself
/// moving the mouse, <see cref="Nudge"/> wakes the loop at once instead of letting the movement wait
/// for the next poll.
/// </remarks>
public sealed class CursorStreamer : IDisposable
{
    /// <summary>How many times a second the cursor is checked. A message goes out only when it moved.</summary>
    public const int PollsPerSecond = 60;

    /// <summary>
    /// After a nudge the movement may not have reached the cursor yet (the input is processed a
    /// moment after it was injected), so look again a few times, a millisecond apart.
    /// </summary>
    public const int SettleChecks = 4;

    private readonly IScreenCapturer _capturer;
    private readonly Func<bool> _isPaused;
    private readonly TimeSpan _pollInterval;
    private readonly SemaphoreSlim _nudged = new(0, 1);
    private byte[]? _last;

    public CursorStreamer(IScreenCapturer capturer, Func<bool> isPaused, TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(capturer);
        ArgumentNullException.ThrowIfNull(isPaused);

        _capturer = capturer;
        _isPaused = isPaused;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1.0 / PollsPerSecond);
    }

    /// <summary>Tells the loop the cursor was probably just moved, so it should look now. Cheap and thread-safe.</summary>
    public void Nudge()
    {
        try
        {
            _nudged.Release();
        }
        catch (SemaphoreFullException)
        {
            // A nudge is already waiting; one is enough.
        }
        catch (ObjectDisposedException)
        {
            // The stream just ended.
        }
    }

    /// <summary>Checks the cursor once and sends it if it moved since the last message.</summary>
    public async Task<StepResult> StepAsync(Func<byte[], CancellationToken, Task> send, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(send);

        if (_isPaused())
        {
            _last = null;
            return StepResult.Paused;
        }

        if (!_capturer.IsDesktopAvailable())
        {
            _last = null;
            return StepResult.Locked;
        }

        if (!_capturer.TryGetCursor(out var x, out var y))
        {
            return StepResult.Failed;
        }

        var message = CursorCodec.Encode(_capturer.GetMonitors(), x, y);
        if (_last is not null && message.AsSpan().SequenceEqual(_last))
        {
            return StepResult.Unchanged;
        }

        await send(message, cancellationToken);
        _last = message;
        return StepResult.Sent;
    }

    public async Task RunAsync(Func<byte[], CancellationToken, Task> send, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(send);

        while (!cancellationToken.IsCancellationRequested)
        {
            var started = Stopwatch.GetTimestamp();
            var result = await StepAsync(send, cancellationToken);

            // When there is nothing to read (paused, locked) look less often instead of spinning.
            if (result is StepResult.Paused or StepResult.Locked or StepResult.Failed)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                _ = _nudged.Wait(0, CancellationToken.None); // drop nudges that piled up meanwhile
                continue;
            }

            var wait = _pollInterval - Stopwatch.GetElapsedTime(started);
            if (wait <= TimeSpan.Zero || !await _nudged.WaitAsync(wait, cancellationToken))
            {
                continue; // the poll interval passed; check again
            }

            // Nudged: the phone just moved the mouse. Look now, and again briefly if the move has not landed yet.
            for (var i = 0; i < SettleChecks && !cancellationToken.IsCancellationRequested; i++)
            {
                if (await StepAsync(send, cancellationToken) == StepResult.Sent)
                {
                    break;
                }

                await Task.Delay(1, cancellationToken);
            }
        }
    }

    public void Dispose() => _nudged.Dispose();
}
