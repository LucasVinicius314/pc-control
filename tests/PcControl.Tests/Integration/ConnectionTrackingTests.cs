using System.Net.WebSockets;

namespace PcControl.Tests.Integration;

public class ConnectionTrackingTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = Environment.TickCount64 + 3000;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"Timed out waiting for {what}.");
    }

    [Fact]
    public async Task ConnectedPhone_AppearsInTheSnapshot_WithItsConnectTime()
    {
        await using var server = await TestServer.StartAsync();
        var session = await server.PairAsync("Pixel");
        var id = session.GetProperty("deviceId").GetGuid();
        Assert.Empty(server.Connections.Snapshot());
        var before = DateTimeOffset.Now.AddSeconds(-1);

        using var socket = await server.ConnectAsync(server.Origin, server.CookieValue());
        await TestServer.ReceiveJsonAsync(socket);

        var info = Assert.Single(server.Connections.Snapshot());
        Assert.Equal(id, info.Key);
        Assert.Equal(1, info.Value.Count);
        Assert.InRange(info.Value.Since, before, DateTimeOffset.Now.AddSeconds(1));
        Assert.Equal(1, server.Connections.DeviceCount);
    }

    [Fact]
    public async Task Disconnecting_RemovesThePhone_AndRaisesChanged()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        var changes = 0;
        server.Connections.Changed += (_, _) => Interlocked.Increment(ref changes);

        var socket = await server.ConnectAsync(server.Origin, server.CookieValue());
        await TestServer.ReceiveJsonAsync(socket);
        Assert.Equal(1, Volatile.Read(ref changes));

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        socket.Dispose();

        await WaitUntilAsync(() => server.Connections.Snapshot().Count == 0, "the phone to disappear");
        await WaitUntilAsync(() => Volatile.Read(ref changes) == 2, "the second change notification");
    }

    [Fact]
    public async Task TwoTabsOnOnePhone_CountAsOneDeviceWithTwoConnections()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        using var first = await server.ConnectAsync(server.Origin, server.CookieValue());
        using var second = await server.ConnectAsync(server.Origin, server.CookieValue());
        await TestServer.ReceiveJsonAsync(first);
        await TestServer.ReceiveJsonAsync(second);

        var info = Assert.Single(server.Connections.Snapshot());
        Assert.Equal(2, info.Value.Count);
        Assert.Equal(1, server.Connections.DeviceCount);
    }

    [Fact]
    public async Task TwoPhones_AreListedSeparately()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync("One");
        using var first = await server.ConnectAsync(server.Origin, server.CookieValue());
        await TestServer.ReceiveJsonAsync(first);
        await server.PairAsync("Two");
        using var second = await server.ConnectAsync(server.Origin, server.CookieValue());
        await TestServer.ReceiveJsonAsync(second);

        Assert.Equal(2, server.Connections.Snapshot().Count);
        Assert.Equal(2, server.Connections.DeviceCount);
    }

    [Fact]
    public async Task RevokingAConnectedPhone_RemovesItFromTheSnapshot()
    {
        await using var server = await TestServer.StartAsync();
        var session = await server.PairAsync();
        using var socket = await server.ConnectAsync(server.Origin, server.CookieValue());
        await TestServer.ReceiveJsonAsync(socket);

        server.Registry.Revoke(session.GetProperty("deviceId").GetGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await socket.ReceiveAsync(new byte[64], timeout.Token); // the 4401 close frame
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);

        await WaitUntilAsync(() => server.Connections.Snapshot().Count == 0, "the revoked phone to disappear");
    }
}
