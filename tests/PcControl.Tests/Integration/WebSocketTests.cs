using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace PcControl.Tests.Integration;

public class WebSocketTests
{
    [Fact]
    public async Task PairedPhone_ConnectsAndReceivesHello()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync("Pixel");

        using var socket = await server.ConnectAsync(server.Origin, server.CookieValue());
        var hello = await TestServer.ReceiveJsonAsync(socket);

        Assert.Equal("hello", hello.GetProperty("type").GetString());
        Assert.Equal(1, hello.GetProperty("protocolVersion").GetInt32());
        Assert.Equal("Pixel", hello.GetProperty("deviceName").GetString());
        Assert.False(hello.GetProperty("paused").GetBoolean());
    }

    [Fact]
    public async Task Ping_GetsPong()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        using var socket = await server.ConnectAsync(server.Origin, server.CookieValue());
        await TestServer.ReceiveJsonAsync(socket);

        await TestServer.SendTextAsync(socket, """{"type":"ping"}""");

        Assert.Equal("pong", (await TestServer.ReceiveJsonAsync(socket)).GetProperty("type").GetString());
    }

    [Fact]
    public async Task InvalidAndUnknownMessages_GetErrors_WithoutClosingTheSocket()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        using var socket = await server.ConnectAsync(server.Origin, server.CookieValue());
        await TestServer.ReceiveJsonAsync(socket);

        await TestServer.SendTextAsync(socket, "not json");
        var invalid = await TestServer.ReceiveJsonAsync(socket);
        await TestServer.SendTextAsync(socket, """{"type":"mystery"}""");
        var unknown = await TestServer.ReceiveJsonAsync(socket);

        Assert.Equal("invalid_message", invalid.GetProperty("code").GetString());
        Assert.Equal("unknown_type", unknown.GetProperty("code").GetString());
        Assert.Equal(WebSocketState.Open, socket.State);
    }

    [Fact]
    public async Task WithoutAToken_HandshakeIsRefused()
    {
        await using var server = await TestServer.StartAsync();

        var error = await Assert.ThrowsAsync<WebSocketException>(() => server.ConnectAsync(server.Origin, cookie: null));

        Assert.Contains("401", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForeignOrigin_HandshakeIsRefused_EvenWithAValidCookie()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var error = await Assert.ThrowsAsync<WebSocketException>(
            () => server.ConnectAsync("http://evil.example.com", server.CookieValue()));

        Assert.Contains("403", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingOrigin_IsRefusedForCookies_ButAllowedForBearerTokens()
    {
        await using var server = await TestServer.StartAsync();
        var session = await server.PairAsync(bearer: true);
        var token = session.GetProperty("token").GetString()!;

        var cookieOnly = await Assert.ThrowsAsync<WebSocketException>(
            () => server.ConnectAsync(origin: null, server.CookieValue()));
        using var bearer = await server.ConnectAsync(origin: null, cookie: null, bearer: token);
        var hello = await TestServer.ReceiveJsonAsync(bearer);

        Assert.Contains("403", cookieOnly.Message, StringComparison.Ordinal);
        Assert.Equal("hello", hello.GetProperty("type").GetString());
    }

    [Fact]
    public async Task RevokingADevice_ClosesItsSocket_WithCode4401()
    {
        await using var server = await TestServer.StartAsync();
        var session = await server.PairAsync();
        using var socket = await server.ConnectAsync(server.Origin, server.CookieValue());
        await TestServer.ReceiveJsonAsync(socket);

        server.Registry.Revoke(session.GetProperty("deviceId").GetGuid());

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await socket.ReceiveAsync(new byte[256], timeout.Token);
        Assert.Equal(WebSocketMessageType.Close, result.MessageType);
        Assert.Equal(4401, (int)result.CloseStatus!.Value);
    }

    [Fact]
    public async Task RevokedToken_CannotReconnect()
    {
        await using var server = await TestServer.StartAsync();
        var session = await server.PairAsync();
        var cookie = server.CookieValue();
        server.Registry.Revoke(session.GetProperty("deviceId").GetGuid());

        await Assert.ThrowsAsync<WebSocketException>(() => server.ConnectAsync(server.Origin, cookie));
    }

    [Fact]
    public async Task KillSwitch_IsBroadcastToConnectedPhones_AndReportedOnNewOnes()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        using var first = await server.ConnectAsync(server.Origin, server.CookieValue());
        await TestServer.ReceiveJsonAsync(first);

        server.State.Paused = true;

        var broadcast = await TestServer.ReceiveJsonAsync(first);
        Assert.Equal("paused", broadcast.GetProperty("type").GetString());
        Assert.True(broadcast.GetProperty("paused").GetBoolean());

        using var second = await server.ConnectAsync(server.Origin, server.CookieValue());
        Assert.True((await TestServer.ReceiveJsonAsync(second)).GetProperty("paused").GetBoolean());
    }

    [Fact]
    public async Task OversizedMessage_ClosesTheSocket()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        using var socket = await server.ConnectAsync(server.Origin, server.CookieValue());
        await TestServer.ReceiveJsonAsync(socket);

        var payload = Encoding.UTF8.GetBytes(new string('a', 20_000));
        await socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await socket.ReceiveAsync(new byte[256], timeout.Token);
        Assert.Equal(WebSocketMessageType.Close, result.MessageType);
        Assert.Equal(WebSocketCloseStatus.MessageTooBig, result.CloseStatus);
    }

    [Fact]
    public async Task NonWebSocketRequest_ToWsPath_IsABadRequest()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var response = await server.Http.GetAsync("/ws");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
