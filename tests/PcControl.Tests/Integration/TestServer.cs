using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using PcControl.Core.Config;
using PcControl.Core.Pairing;
using PcControl.Host;
using PcControl.Host.Web;

namespace PcControl.Tests.Integration;

/// <summary>Runs the real server (Kestrel, auth, WebSocket) on a random loopback port.</summary>
internal sealed class TestServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly CookieContainer _cookies = new();
    private readonly HttpClientHandler _handler;

    private TestServer(WebApplication app, PinService pins, DeviceRegistry registry, RemoteControlState state, AppConfig config, FakeInjector injector, DeviceConnections connections)
    {
        _app = app;
        Connections = connections;
        Injector = injector;
        Pins = pins;
        Registry = registry;
        State = state;
        Config = config;

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        Port = new Uri(address.Replace("[::]", "127.0.0.1", StringComparison.Ordinal)).Port;
        BaseUri = new Uri($"http://127.0.0.1:{Port}");

        _handler = new HttpClientHandler { CookieContainer = _cookies, UseCookies = true };
        Http = new HttpClient(_handler) { BaseAddress = BaseUri };
    }

    public FakeInjector Injector { get; }

    internal DeviceConnections Connections { get; }

    public PinService Pins { get; }

    public DeviceRegistry Registry { get; }

    public RemoteControlState State { get; }

    public AppConfig Config { get; }

    public HttpClient Http { get; }

    public int Port { get; }

    public Uri BaseUri { get; }

    public string Origin => $"http://127.0.0.1:{Port}";

    public static async Task<TestServer> StartAsync(int maxFailedAttempts = 5)
    {
        var config = new AppConfig
        {
            Port = 0,
            Pairing = new PairingConfig { MaxFailedAttempts = maxFailedAttempts, MaxFailuresPerPin = 50 },
        };

        var time = TimeProvider.System;
        var pins = new PinService(time, TimeSpan.FromMinutes(5), config.Pairing.MaxFailuresPerPin);
        var limiter = new PairingRateLimiter(time, maxFailedAttempts, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15));
        var registry = new DeviceRegistry(new MemoryDeviceStore(), time);
        var state = new RemoteControlState();
        var connections = new DeviceConnections();

        var injector = new FakeInjector();
        var app = ServerFactory.Build(config, pins, limiter, registry, state, connections, injector);
        await app.StartAsync();
        return new TestServer(app, pins, registry, state, config, injector, connections);
    }

    /// <summary>Issues a PIN and pairs through the real endpoint. Returns the session JSON.</summary>
    public async Task<JsonElement> PairAsync(string name = "Test phone", bool bearer = false)
    {
        var pin = Pins.Issue().Pin;
        var response = await Http.PostAsJsonAsync("/api/pair", new { pin, deviceName = name, issueBearerToken = bearer });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public string CookieValue() =>
        _cookies.GetCookies(BaseUri).Cast<Cookie>().Single(c => c.Name == DeviceAuth.CookieName).Value;

    public async Task<ClientWebSocket> ConnectAsync(string? origin, string? cookie, string? bearer = null)
    {
        var socket = new ClientWebSocket();
        if (origin is not null)
        {
            socket.Options.SetRequestHeader("Origin", origin);
        }

        if (cookie is not null)
        {
            socket.Options.SetRequestHeader("Cookie", $"{DeviceAuth.CookieName}={cookie}");
        }

        if (bearer is not null)
        {
            socket.Options.SetRequestHeader("Authorization", $"Bearer {bearer}");
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{Port}/ws"), timeout.Token);
        return socket;
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        _handler.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    public static async Task<JsonElement> ReceiveJsonAsync(WebSocket socket)
    {
        var buffer = new byte[8192];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await socket.ReceiveAsync(buffer, timeout.Token);
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        return JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(buffer, 0, result.Count));
    }

    public static Task SendTextAsync(WebSocket socket, string text) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);

    private sealed class MemoryDeviceStore : IDeviceStore
    {
        private List<PairedDevice> _devices = [];

        public IReadOnlyList<PairedDevice> Load() => _devices;

        public void Save(IReadOnlyList<PairedDevice> devices) => _devices = [.. devices];
    }
}
