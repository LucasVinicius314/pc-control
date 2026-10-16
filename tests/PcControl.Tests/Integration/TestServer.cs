using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using PcControl.Core.Actions;
using PcControl.Core.Apps;
using PcControl.Core.Config;
using PcControl.Core.Display;
using PcControl.Core.Input;
using PcControl.Core.Pairing;
using PcControl.Core.Theme;
using PcControl.Host;
using PcControl.Host.Web;

namespace PcControl.Tests.Integration;

/// <summary>Runs the real server (Kestrel, auth, WebSocket) on a random loopback port.</summary>
internal sealed class TestServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly CookieContainer _cookies = new();
    private readonly HttpClientHandler _handler;

    private TestServer(WebApplication app, ServerDependencies deps, FakeInjector injector, FakeScreen screen, FakeScaleBackend scale, FakeCatalog catalog, MemoryStateStore appsStore, PowerToysSettingsHolder powerToysSettings, FakeUrlOpener urls, FakePowerBackend powerBackend, FakeProcessCatalog processes, FakeTheme theme)
    {
        _app = app;
        Scale = scale;
        Dpi = deps.Dpi;
        Screen = screen;
        Connections = deps.Connections;
        Injector = injector;
        Pins = deps.Pins;
        Registry = deps.Registry;
        State = deps.State;
        Config = deps.Config;
        Catalog = catalog;
        AppsStore = appsStore;
        PowerToys = powerToysSettings;
        Urls = urls;
        PowerBackend = powerBackend;
        Processes = processes;
        Theme = theme;

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        Port = new Uri(address.Replace("[::]", "127.0.0.1", StringComparison.Ordinal)).Port;
        BaseUri = new Uri($"http://127.0.0.1:{Port}");

        _handler = new HttpClientHandler { CookieContainer = _cookies, UseCookies = true };
        Http = new HttpClient(_handler) { BaseAddress = BaseUri };
    }

    public FakeInjector Injector { get; }

    public FakeScreen Screen { get; }

    public FakeScaleBackend Scale { get; }

    public FakeCatalog Catalog { get; }

    public MemoryStateStore AppsStore { get; }

    public FakeUrlOpener Urls { get; }

    public FakePowerBackend PowerBackend { get; }

    public FakeProcessCatalog Processes { get; }

    public FakeTheme Theme { get; }

    /// <summary>The text of PowerToys' settings file as the server will read it; null means PowerToys is not installed.</summary>
    public PowerToysSettingsHolder PowerToys { get; }

    public sealed class PowerToysSettingsHolder
    {
        public string? Json { get; set; }
    }

    public DpiToggleService Dpi { get; }

    internal DeviceConnections Connections { get; }

    public PinService Pins { get; }

    public DeviceRegistry Registry { get; }

    public RemoteControlState State { get; }

    public AppConfig Config { get; }

    public HttpClient Http { get; }

    public int Port { get; }

    public Uri BaseUri { get; }

    public string Origin => $"http://127.0.0.1:{Port}";

    public static async Task<TestServer> StartAsync(int maxFailedAttempts = 5, bool screenshotsEnabled = true)
    {
        var config = new AppConfig
        {
            Port = 0,
            Features = new FeatureToggles { Screenshot = screenshotsEnabled },
            Pairing = new PairingConfig { MaxFailedAttempts = maxFailedAttempts, MaxFailuresPerPin = 50 },
        };

        var time = TimeProvider.System;
        var pins = new PinService(time, TimeSpan.FromMinutes(5), config.Pairing.MaxFailuresPerPin);
        var limiter = new PairingRateLimiter(time, maxFailedAttempts, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15));
        var registry = new DeviceRegistry(new MemoryDeviceStore(), time);
        var state = new RemoteControlState();
        var connections = new DeviceConnections();

        var injector = new FakeInjector();
        var screen = new FakeScreen();
        var scale = new FakeScaleBackend();
        var dpi = new DpiToggleService(screen, [scale], config.Dpi, _ => Task.CompletedTask);
        dpi.Check();
        var catalog = new FakeCatalog
        {
            Apps =
            [
                AppEntry.Create("Notepad", "notepad-id"),
                AppEntry.Create("Calculator", "calc-id"),
                AppEntry.Create("Édge", "edge-id"),
            ],
        };
        var appsStore = new MemoryStateStore();
        var apps = new AppsService(catalog, appsStore, time, config.Launcher.RecentCount, cacheLifetime: TimeSpan.Zero);
        var powerToysSettings = new PowerToysSettingsHolder();
        var powerToys = new PowerToysShortcutResolver(config.Launcher, () => powerToysSettings.Json);

        var theme = new FakeTheme();
        var urls = new FakeUrlOpener();
        var powerBackend = new FakePowerBackend();
        var processes = new FakeProcessCatalog
        {
            Apps =
            [
                new RunningApp(100, 1, "notepad", "Untitled - Notepad"),
                new RunningApp(200, 2, "chrome", "Anime - Chrome"),
                new RunningApp(300, 3, "explorer", "File Explorer"),
            ],
        };

        var deps = new ServerDependencies(
            config, pins, limiter, registry, state, connections, injector, screen, screen, dpi, apps, powerToys,
            urls, new PowerService(powerBackend, time), new ProcessService(processes, ownPid: 999), theme);
        var app = ServerFactory.Build(deps);
        await app.StartAsync();
        return new TestServer(app, deps, injector, screen, scale, catalog, appsStore, powerToysSettings, urls, powerBackend, processes, theme);
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
        Dpi.Dispose();
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
