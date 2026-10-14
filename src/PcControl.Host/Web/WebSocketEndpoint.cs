using System.Net.WebSockets;
using PcControl.Core.Input;
using PcControl.Core.Network;
using PcControl.Core.Protocol;

namespace PcControl.Host.Web;

internal static class WebSocketEndpoint
{
    /// <summary>Close code sent when the device's token was revoked. Clients must not retry.</summary>
    public const int RevokedCloseCode = 4401;

    public static void Map(IEndpointRouteBuilder app) =>
        app.Map("/ws", Handle).RequireAuthorization();

    private static async Task Handle(
        HttpContext context,
        DeviceConnections connections,
        RemoteControlState state,
        IInputInjector injector,
        PointerCurve curve,
        ILogger<WsConnection> logger)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        // Browsers always send Origin on a WebSocket handshake, so a foreign Origin is a hostile
        // page trying to ride the phone's cookie. Without an Origin the caller is not a browser,
        // and then only a bearer token (never a cookie) is accepted.
        var origin = context.Request.Headers.Origin.ToString();
        var viaBearer = context.User.FindFirst(DeviceAuth.TokenSourceClaim)?.Value == DeviceAuth.BearerSource;
        var originOk = origin.Length > 0
            ? OriginPolicy.IsSameOrigin(origin, context.Request.Host.Value)
            : viaBearer;

        if (!originOk)
        {
            logger.LogWarning("Rejected WebSocket handshake from {Remote}: bad origin {Origin}", context.Connection.RemoteIpAddress, origin);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var deviceId = context.User.DeviceId();
        var deviceName = context.User.Identity!.Name!;

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var connection = new WsConnection(socket, deviceId, deviceName, new InputSession(injector, curve));

        connections.Add(connection);
        logger.LogInformation("{DeviceName} connected from {Remote}", deviceName, context.Connection.RemoteIpAddress);

        try
        {
            await connection.SendAsync(
                new { type = WsMessageTypes.Hello, protocolVersion = ProtocolInfo.Version, deviceId, deviceName, paused = state.Paused },
                context.RequestAborted);

            // Never cancel a pending receive: that aborts the socket. Revocation sends a close
            // frame instead and the phone's reply ends this loop.
            await ReadLoop(connection, state, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // The request was aborted (server shutdown).
        }
        catch (WebSocketException)
        {
            // The phone went away without a close handshake (sleep, Wi-Fi drop).
        }
        finally
        {
            connections.Remove(connection);
            // Whatever the reason the socket closed, nothing may stay held down on the PC.
            connection.Input.ReleaseAll();
            logger.LogInformation("{DeviceName} disconnected", deviceName);
        }

        await connection.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty);
    }

    private static async Task ReadLoop(WsConnection connection, RemoteControlState state, CancellationToken cancellationToken)
    {
        var socket = connection.Socket;
        var buffer = new byte[WsMessageParser.MaxMessageBytes];

        while (socket.State == WebSocketState.Open)
        {
            var length = 0;
            ValueWebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer.AsMemory(length), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                length += result.Count;
                if (!result.EndOfMessage && length >= buffer.Length)
                {
                    await connection.CloseOutputAsync(WebSocketCloseStatus.MessageTooBig, "message too big");
                    return;
                }
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
            {
                if (!state.Paused)
                {
                    await ReplyIfError(connection, InputDispatcher.HandleBinary(buffer.AsSpan(0, length), connection.Input), cancellationToken);
                }

                continue;
            }

            if (!WsMessageParser.TryParse(buffer.AsSpan(0, length), out var message, out _))
            {
                await connection.SendAsync(new { type = WsMessageTypes.Error, code = "invalid_message" }, cancellationToken);
                continue;
            }

            switch (message!.Type)
            {
                case WsMessageTypes.Ping:
                    await connection.SendAsync(new { type = WsMessageTypes.Pong }, cancellationToken);
                    break;
                default:
                    // While the kill switch is on, input is dropped without a reply. The phone has
                    // already been told, and answering every mouse move would only add noise.
                    if (InputDispatcher.IsInputMessage(message.Type))
                    {
                        if (!state.Paused)
                        {
                            await ReplyIfError(connection, InputDispatcher.HandleJson(message, connection.Input), cancellationToken);
                        }

                        break;
                    }

                    await connection.SendAsync(new { type = WsMessageTypes.Error, code = "unknown_type" }, cancellationToken);
                    break;
            }
        }
    }

    private static Task ReplyIfError(WsConnection connection, DispatchResult result, CancellationToken cancellationToken) =>
        result.ErrorCode is null
            ? Task.CompletedTask
            : connection.SendAsync(new { type = WsMessageTypes.Error, code = result.ErrorCode }, cancellationToken);
}
