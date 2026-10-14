using System.Net.WebSockets;

namespace PcControl.Tests.Integration;

public class InputTests
{
    private static async Task<ClientWebSocket> ConnectedAsync(TestServer server)
    {
        await server.PairAsync();
        var socket = await server.ConnectAsync(server.Origin, server.CookieValue());
        await TestServer.ReceiveJsonAsync(socket); // hello
        return socket;
    }

    private static Task SendBinaryAsync(WebSocket socket, byte kind, short a, short b)
    {
        var frame = new byte[5];
        frame[0] = kind;
        BitConverter.TryWriteBytes(frame.AsSpan(1), a);
        BitConverter.TryWriteBytes(frame.AsSpan(3), b);
        return socket.SendAsync(frame, WebSocketMessageType.Binary, true, CancellationToken.None);
    }

    [Fact]
    public async Task BinaryMove_MovesTheCursor()
    {
        await using var server = await TestServer.StartAsync();
        using var socket = await ConnectedAsync(server);

        await SendBinaryAsync(socket, 0x01, 1, 0);

        // Default gain 2 at distance 1: 2 * 1.1 = 2.2 -> 2 px.
        await server.Injector.WaitForAsync("move 2,0");
    }

    [Fact]
    public async Task ClickTypeAndKeyMessages_ReachTheInjector()
    {
        await using var server = await TestServer.StartAsync();
        using var socket = await ConnectedAsync(server);

        await TestServer.SendTextAsync(socket, """{"type":"button","button":"left","action":"click"}""");
        await TestServer.SendTextAsync(socket, """{"type":"text","text":"hi"}""");
        await TestServer.SendTextAsync(socket, """{"type":"key","code":"Enter","action":"press"}""");

        await server.Injector.WaitForAsync("key 0D up");
        Assert.Equal(["left down", "left up", "text hi", "key 0D down", "key 0D up"], server.Injector.Events);
    }

    [Fact]
    public async Task BadInput_GetsAnError_AndTheSocketStaysOpen()
    {
        await using var server = await TestServer.StartAsync();
        using var socket = await ConnectedAsync(server);

        await TestServer.SendTextAsync(socket, """{"type":"key","code":"Bogus","action":"press"}""");
        var error = await TestServer.ReceiveJsonAsync(socket);
        await TestServer.SendTextAsync(socket, """{"type":"ping"}""");
        var pong = await TestServer.ReceiveJsonAsync(socket);

        Assert.Equal("unknown_key", error.GetProperty("code").GetString());
        Assert.Equal("pong", pong.GetProperty("type").GetString());
        Assert.Empty(server.Injector.Events);
    }

    [Fact]
    public async Task ClosingTheSocket_ReleasesHeldKeysAndButtons()
    {
        await using var server = await TestServer.StartAsync();
        using var socket = await ConnectedAsync(server);
        await TestServer.SendTextAsync(socket, """{"type":"key","code":"ControlLeft","action":"down"}""");
        await TestServer.SendTextAsync(socket, """{"type":"button","button":"left","action":"down"}""");
        await server.Injector.WaitForAsync("left down");

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);

        await server.Injector.WaitForAsync("key A2 up");
        await server.Injector.WaitForAsync("left up");
    }

    [Fact]
    public async Task DroppingTheConnection_WithoutAClose_AlsoReleases()
    {
        await using var server = await TestServer.StartAsync();
        var socket = await ConnectedAsync(server);
        await TestServer.SendTextAsync(socket, """{"type":"key","code":"ShiftLeft","action":"down"}""");
        await server.Injector.WaitForAsync("key A0 down");

        socket.Abort();
        socket.Dispose();

        await server.Injector.WaitForAsync("key A0 up");
    }

    [Fact]
    public async Task KillSwitch_ReleasesHeldInput_AndDropsFurtherInput()
    {
        await using var server = await TestServer.StartAsync();
        using var socket = await ConnectedAsync(server);
        await TestServer.SendTextAsync(socket, """{"type":"key","code":"ControlLeft","action":"down"}""");
        await server.Injector.WaitForAsync("key A2 down");

        server.State.Paused = true;
        await server.Injector.WaitForAsync("key A2 up");
        Assert.Equal("paused", (await TestServer.ReceiveJsonAsync(socket)).GetProperty("type").GetString());

        server.Injector.Clear();
        await TestServer.SendTextAsync(socket, """{"type":"text","text":"nope"}""");
        await SendBinaryAsync(socket, 0x01, 50, 50);
        await TestServer.SendTextAsync(socket, """{"type":"ping"}""");
        Assert.Equal("pong", (await TestServer.ReceiveJsonAsync(socket)).GetProperty("type").GetString());

        Assert.Empty(server.Injector.Events);
    }

    [Fact]
    public async Task ResumingAfterPause_AcceptsInputAgain()
    {
        await using var server = await TestServer.StartAsync();
        using var socket = await ConnectedAsync(server);
        server.State.Paused = true;
        await TestServer.ReceiveJsonAsync(socket);
        server.State.Paused = false;
        await TestServer.ReceiveJsonAsync(socket);

        await TestServer.SendTextAsync(socket, """{"type":"text","text":"back"}""");

        await server.Injector.WaitForAsync("text back");
    }

    [Fact]
    public async Task EachPhone_HasItsOwnHeldState()
    {
        await using var server = await TestServer.StartAsync();
        using var first = await ConnectedAsync(server);
        using var second = await server.ConnectAsync(server.Origin, server.CookieValue());
        await TestServer.ReceiveJsonAsync(second);
        await TestServer.SendTextAsync(first, """{"type":"key","code":"ControlLeft","action":"down"}""");
        await server.Injector.WaitForAsync("key A2 down");
        server.Injector.Clear();

        await second.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        await Task.Delay(300);

        Assert.DoesNotContain("key A2 up", server.Injector.Events);
    }
}
