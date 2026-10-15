using System.Net.WebSockets;
using PcControl.Core.Capture;

namespace PcControl.Tests.Integration;

public class CursorChannelTests
{
    private static async Task<ClientWebSocket> ConnectedAsync(TestServer server)
    {
        await server.PairAsync();
        var socket = await server.ConnectAsync(server.Origin, server.CookieValue());
        await TestServer.ReceiveJsonAsync(socket);
        return socket;
    }

    private static async Task<byte[]> NextBinaryAsync(WebSocket socket, byte kind)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[512 * 1024];
        while (true)
        {
            var length = 0;
            ValueWebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer.AsMemory(length), timeout.Token);
                length += result.Count;
            }
            while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Binary && buffer[0] == kind)
            {
                return buffer[..length];
            }
        }
    }

    [Fact]
    public async Task TheDisplayPreview_SendsTheCursorOnItsOwn_AsItMoves()
    {
        await using var server = await TestServer.StartAsync();
        server.Screen.CursorX = 960;
        server.Screen.CursorY = 540;
        using var socket = await ConnectedAsync(server);

        await TestServer.SendTextAsync(socket, """{"type":"view","stream":"display","enabled":true,"fps":1}""");
        var first = await NextBinaryAsync(socket, CursorCodec.Kind);
        server.Screen.CursorX = 1900;
        server.Screen.CursorY = 1000;
        byte[] moved;
        do
        {
            moved = await NextBinaryAsync(socket, CursorCodec.Kind);
        }
        while (moved.AsSpan().SequenceEqual(first));

        Assert.True(CursorCodec.TryDecode(first, out var m1, out var x1, out _));
        Assert.True(CursorCodec.TryDecode(moved, out var m2, out var x2, out var y2));
        Assert.Equal((0, 0), (m1, m2));
        Assert.Equal(0.5, x1, precision: 2);
        Assert.True(x2 > 0.98 && y2 > 0.9, $"cursor ended at ({x2}, {y2})");
    }

    [Fact]
    public async Task ACursorOnlyMove_DoesNotSendAnotherDisplayPicture()
    {
        await using var server = await TestServer.StartAsync();
        using var socket = await ConnectedAsync(server);
        await TestServer.SendTextAsync(socket, """{"type":"view","stream":"display","enabled":true,"fps":10}""");
        await NextBinaryAsync(socket, FrameCodec.DisplayKind);

        for (var i = 0; i < 15; i++)
        {
            server.Screen.CursorX = 100 + (i * 20);
            await Task.Delay(40);
        }

        // Drain what arrived meanwhile and count the pictures among it.
        var pictures = 0;
        var cursors = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var buffer = new byte[512 * 1024];
        try
        {
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(), cts.Token);
                if (result.MessageType != WebSocketMessageType.Binary)
                {
                    continue;
                }

                if (buffer[0] == FrameCodec.DisplayKind)
                {
                    pictures++;
                }
                else if (buffer[0] == CursorCodec.Kind)
                {
                    cursors++;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Done draining.
        }

        Assert.Equal(0, pictures);
        Assert.True(cursors > 5, $"only {cursors} cursor updates arrived");
    }

    [Fact]
    public async Task TurningTheDisplayPreviewOff_StopsTheCursorMessages()
    {
        await using var server = await TestServer.StartAsync();
        using var socket = await ConnectedAsync(server);
        await TestServer.SendTextAsync(socket, """{"type":"view","stream":"display","enabled":true,"fps":10}""");
        await NextBinaryAsync(socket, CursorCodec.Kind);

        await TestServer.SendTextAsync(socket, """{"type":"view","stream":"display","enabled":false}""");
        await Task.Delay(300);

        var captures = server.Screen.Captures.Count;
        for (var i = 0; i < 10; i++)
        {
            server.Screen.CursorX += 30;
            await Task.Delay(30);
        }

        Assert.Equal(captures, server.Screen.Captures.Count);
        await TestServer.SendTextAsync(socket, """{"type":"ping"}""");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var buffer = new byte[512 * 1024];
        var lateCursors = 0;
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(), cts.Token);
            if (result.MessageType == WebSocketMessageType.Text)
            {
                break; // the pong; nothing after the first drain should be a cursor message
            }

            if (buffer[0] == CursorCodec.Kind)
            {
                lateCursors++;
            }
        }

        // A few in-flight messages from before the stop are fine; a continuing stream is not.
        Assert.True(lateCursors < 5, $"{lateCursors} cursor messages kept arriving after the stop");
    }
}
