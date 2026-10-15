using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.FileProviders;
using PcControl.Core.Capture;
using PcControl.Core.Config;
using PcControl.Core.Display;
using PcControl.Core.Input;
using PcControl.Core.Pairing;
using PcControl.Core.Protocol;
using Serilog;

namespace PcControl.Host.Web;

internal static class ServerFactory
{
    public static WebApplication Build(
        AppConfig config,
        PinService pins,
        PairingRateLimiter limiter,
        DeviceRegistry registry,
        RemoteControlState state,
        DeviceConnections connections,
        IInputInjector injector,
        IScreenCapturer capturer,
        IJpegEncoder encoder,
        DpiToggleService dpi)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Host.UseSerilog(Log.Logger, dispose: false);

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            // Dual-stack on all interfaces; GuardMiddleware turns away anything not on the home network.
            kestrel.ListenAnyIP(config.Port);
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = 64 * 1024;
        });

        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(pins);
        builder.Services.AddSingleton(limiter);
        builder.Services.AddSingleton(registry);
        builder.Services.AddSingleton(state);
        builder.Services.AddSingleton(connections);
        builder.Services.AddSingleton(injector);
        builder.Services.AddSingleton(capturer);
        builder.Services.AddSingleton(encoder);
        builder.Services.AddSingleton(dpi);
        builder.Services.AddSingleton(new PointerCurve(config.Input.PointerGain));

        builder.Services.AddAuthentication(DeviceAuth.Scheme)
            .AddScheme<AuthenticationSchemeOptions, DeviceAuthenticationHandler>(DeviceAuth.Scheme, null);
        builder.Services.AddAuthorization();

        var app = builder.Build();

        registry.DeviceRevoked += (_, device) => connections.Revoke(device.Id);
        state.Changed += (_, _) =>
        {
            // Pausing must stop everything at once, including keys and buttons already held down.
            if (state.Paused)
            {
                connections.ReleaseAllInput();
            }

            _ = connections.BroadcastAsync(new { type = WsMessageTypes.Paused, paused = state.Paused });
        };

        app.UseMiddleware<GuardMiddleware>();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
        app.UseAuthentication();
        app.UseAuthorization();

        var web = new ManifestEmbeddedFileProvider(typeof(ServerFactory).Assembly, "web");
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = web });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = web,
            OnPrepareResponse = context =>
            {
                // Vite fingerprints everything under /assets, so only the entry files must revalidate.
                var immutable = context.Context.Request.Path.StartsWithSegments("/assets");
                context.Context.Response.Headers.CacheControl = immutable
                    ? "public, max-age=31536000, immutable"
                    : "no-cache";
            },
        });

        ApiEndpoints.Map(app);
        WebSocketEndpoint.Map(app);

        return app;
    }
}
