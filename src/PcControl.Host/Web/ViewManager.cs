using System.Net.WebSockets;
using PcControl.Core.Capture;
using PcControl.Windows;

namespace PcControl.Host.Web;

/// <summary>
/// Runs the picture streams (mini preview and display preview) of one connected phone. Each phone
/// has at most one stream per kind; asking again replaces it. Everything stops when the socket does.
/// </summary>
internal sealed class ViewManager(
    WsConnection connection,
    IScreenCapturer capturer,
    IJpegEncoder encoder,
    RemoteControlState state,
    ILogger logger) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<ViewChannel, (CancellationTokenSource Cancel, Task Task)> _running = [];
    private readonly List<Task> _pendingStops = [];
    private CursorStreamer? _cursor;

    /// <summary>Starts, restarts or stops a stream to match what the phone asked for.</summary>
    public void Apply(ViewOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        lock (_gate)
        {
            Stop(options.Stream);

            if (!options.Enabled)
            {
                return;
            }

            var cancel = new CancellationTokenSource();
            _running[options.Stream] = (cancel, Task.Run(() => RunAsync(options, cancel.Token)));
        }

        logger.LogInformation(
            "{DeviceName} started the {Channel} preview (monitor {Monitor}, {Fps} fps)",
            connection.DeviceName,
            options.Stream,
            options.Monitor == ViewOptions.FollowCursor ? "follows cursor" : options.Monitor,
            options.Fps);
    }

    /// <summary>The phone just moved the mouse: send the new cursor position right away.</summary>
    public void CursorMoved() => Volatile.Read(ref _cursor)?.Nudge();

    public async ValueTask DisposeAsync()
    {
        List<Task> tasks;
        lock (_gate)
        {
            foreach (var channel in _running.Keys.ToList())
            {
                Stop(channel);
            }

            tasks = [.. _pendingStops];
            _pendingStops.Clear();
        }

        await Task.WhenAll(tasks);
    }

    private void Stop(ViewChannel channel)
    {
        if (_running.Remove(channel, out var running))
        {
            running.Cancel.Cancel();
            _pendingStops.RemoveAll(t => t.IsCompleted);
            _pendingStops.Add(running.Task.ContinueWith(_ => running.Cancel.Dispose(), TaskScheduler.Default));
        }
    }

    private async Task RunAsync(ViewOptions options, CancellationToken cancellationToken)
    {
        var streamer = new ViewStreamer(capturer, encoder, options, () => state.Paused);
        var name = options.Stream.ToString().ToLowerInvariant();

        // Ordinary Windows timers tick every 15.6 ms. While a preview is open, ask for 1 ms so the
        // frame and cursor loops wake when they should.
        using var timerResolution = HighResolutionTimer.Acquire();
        CursorStreamer? cursor = null;

        try
        {
            // The token is deliberately not passed to the socket: cancelling a send in progress would
            // abort the whole connection, and the phone only wanted this one stream to stop.
            var work = new List<Task>
            {
                streamer.RunAsync(
                    (frame, _) => connection.SendBinaryAsync(frame),
                    streamState => connection.SendAsync(
                        new { type = "viewStatus", stream = name, state = streamState.ToString().ToLowerInvariant() },
                        CancellationToken.None),
                    cancellationToken),
            };

            // The display preview also gets the cursor on its own, fast, so the marker does not wait for pictures.
            if (options.Stream == ViewChannel.Display)
            {
                cursor = new CursorStreamer(capturer, () => state.Paused);
                Volatile.Write(ref _cursor, cursor);
                work.Add(cursor.RunAsync((message, _) => connection.SendBinaryAsync(message), cancellationToken));
            }

            await Task.WhenAll(work);
        }
        catch (OperationCanceledException)
        {
            // Stopped by the phone or by the connection closing.
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException)
        {
            // The phone went away; the connection's own handler cleans up.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The {Stream} preview for {DeviceName} stopped unexpectedly", name, connection.DeviceName);
        }
        finally
        {
            if (cursor is not null)
            {
                // Only clear the field if it still points at ours: a restarted stream has its own.
                Interlocked.CompareExchange(ref _cursor, null, cursor);
                cursor.Dispose();
            }
        }
    }
}
