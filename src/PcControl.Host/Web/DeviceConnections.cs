using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace PcControl.Host.Web;

/// <summary>One live phone WebSocket. Sends are serialised; the socket allows only one at a time.</summary>
internal sealed class WsConnection(WebSocket socket, Guid deviceId, string deviceName) : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan CloseHandshakeTimeout = TimeSpan.FromSeconds(3);
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public Guid Id { get; } = Guid.NewGuid();

    public WebSocket Socket { get; } = socket;

    public Guid DeviceId { get; } = deviceId;

    public string DeviceName { get; } = deviceName;

    public async Task SendAsync(object message, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Json);

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (Socket.State == WebSocketState.Open)
            {
                await Socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>Sends a close frame. The read loop ends when the phone answers it.</summary>
    public async Task CloseOutputAsync(WebSocketCloseStatus status, string reason)
    {
        await _sendLock.WaitAsync();
        try
        {
            if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(CloseHandshakeTimeout);
                await Socket.CloseOutputAsync(status, reason, timeout.Token);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The phone is already gone.
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>Tells the phone its token was removed (close code 4401), then drops it if it never answers.</summary>
    public async Task RevokeAsync()
    {
        await CloseOutputAsync((WebSocketCloseStatus)WebSocketEndpoint.RevokedCloseCode, "revoked");

        await Task.Delay(CloseHandshakeTimeout);
        if (Socket.State != WebSocketState.Closed)
        {
            Socket.Abort();
        }
    }

    public void Dispose() => _sendLock.Dispose();
}

/// <summary>Tracks live sockets so the tray can broadcast the kill switch and drop revoked phones.</summary>
internal sealed class DeviceConnections
{
    private readonly ConcurrentDictionary<Guid, WsConnection> _connections = new();

    public int Count => _connections.Count;

    public void Add(WsConnection connection) => _connections[connection.Id] = connection;

    public void Remove(WsConnection connection) => _connections.TryRemove(connection.Id, out _);

    public void Revoke(Guid deviceId)
    {
        foreach (var connection in _connections.Values.Where(c => c.DeviceId == deviceId))
        {
            _ = connection.RevokeAsync();
        }
    }

    public async Task BroadcastAsync(object message)
    {
        foreach (var connection in _connections.Values)
        {
            try
            {
                await connection.SendAsync(message, CancellationToken.None);
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or OperationCanceledException)
            {
                // The socket is already going away; its own handler cleans up.
            }
        }
    }
}
