using PcControl.Core.Capture;
using PcControl.Core.Config;
using PcControl.Core.Display;
using PcControl.Core.Pairing;
using PcControl.Core.Protocol;
using PcControl.Core.Theme;

namespace PcControl.Host.Web;

internal sealed record LocalPairRequest(string? Key);

internal sealed record PairRequest(string? Pin, string? DeviceName, bool IssueBearerToken = false);

internal sealed record SessionResponse(
    Guid DeviceId,
    string DeviceName,
    bool Paused,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? Token = null);

internal static class ApiEndpoints
{
    private const int MaxPinLength = 16;

    public static void Map(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/info", (RemoteControlState state) =>
            new { app = ProtocolInfo.AppName, protocolVersion = ProtocolInfo.Version, paused = state.Paused });

        api.MapPost("/pair", Pair);
        api.MapPost("/pair/local", PairLocal);

        var secured = api.MapGroup(string.Empty).RequireAuthorization();

        secured.MapGet("/session", (HttpContext context, RemoteControlState state) =>
            new SessionResponse(context.User.DeviceId(), context.User.Identity!.Name!, state.Paused));

        secured.MapDelete("/session", Unpair);

        secured.MapGet("/capabilities", (AppConfig config, DpiToggleService dpi, PcControl.Core.Games.GamesService games) =>
        {
            // The DPI toggle only counts as available if its startup self-test passed.
            var implemented = new HashSet<string>(Capabilities.Implemented, StringComparer.Ordinal);
            if (dpi.Available)
            {
                implemented.Add("dpiToggle");
            }

            // Games only count when the emulator was found.
            if (games.Available)
            {
                implemented.Add("gameLauncher");
            }

            return Capabilities.Build(config, implemented);
        });

        AppEndpoints.Map(secured);
        GameEndpoints.Map(secured);
        SystemEndpoints.Map(secured);

        secured.MapGet("/theme", (IThemeProvider theme) =>
        {
            var current = theme.Read();
            return Results.Json(new { accent = current.Accent, dark = current.Dark });
        });

        secured.MapGet("/couch/backdrop", (CouchBackdrop backdrop, HttpContext context) =>
        {
            var jpeg = backdrop.Jpeg;
            if (jpeg is null)
            {
                return Results.Json(new { error = "no_image" }, statusCode: StatusCodes.Status404NotFound);
            }

            context.Response.Headers.CacheControl = "no-store";
            return Results.File(jpeg, "image/jpeg");
        });

        secured.MapGet("/dpi", GetDpi);
        secured.MapPost("/dpi/toggle", ToggleDpi);

        secured.MapGet("/monitors", Monitors);
        secured.MapGet("/screenshot", Screenshot);
    }

    private static IResult GetDpi(DpiToggleService dpi)
    {
        var status = dpi.Available ? dpi.Read() : dpi.Status;
        return Results.Json(new
        {
            available = status.Available,
            reason = status.Reason,
            percent = status.Percent,
            lowPercent = dpi.LowPercent,
            highPercent = dpi.HighPercent,
            monitor = status.Monitor is { } m ? new { index = m.Index, name = m.Name } : null,
            backend = status.Backend,
        });
    }

    /// <summary>
    /// The answer for any action that changes the PC while the tray's kill switch is on, or null when
    /// the action may go ahead. Reading things (lists, previews) stays allowed; doing things does not.
    /// </summary>
    internal static IResult? RefusedWhilePaused(RemoteControlState state) =>
        state.Paused
            ? Results.Json(
                new { error = "paused", message = "Remote control is paused on the PC." },
                statusCode: StatusCodes.Status409Conflict)
            : null;

    private static async Task<IResult> ToggleDpi(HttpContext context, DpiToggleService dpi, RemoteControlState state, ILogger<PairRequest> logger)
    {
        if (RefusedWhilePaused(state) is { } paused)
        {
            return paused;
        }

        var result = await dpi.ToggleAsync();
        var monitor = result.Monitor is { } m ? new { index = m.Index, name = m.Name } : null;

        switch (result.Outcome)
        {
            case DpiOutcome.Ok:
                logger.LogInformation(
                    "{DeviceName} switched the display scale of {Monitor} from {Previous}% to {Current}%",
                    context.User.Identity!.Name,
                    result.Monitor!.Name,
                    result.Previous,
                    result.Current);
                return Results.Json(new
                {
                    previous = result.Previous,
                    percent = result.Current,
                    monitor,
                    note = "Some apps stay blurry until they are restarted.",
                });

            case DpiOutcome.Busy:
                return Results.Json(new { error = "busy", message = result.Message }, statusCode: StatusCodes.Status409Conflict);

            case DpiOutcome.Unavailable:
                return Results.Json(new { error = "dpi_unavailable", message = result.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);

            case DpiOutcome.NotSupported:
                return Results.Json(
                    new { error = "scale_not_supported", message = result.Message, percent = result.Current, monitor },
                    statusCode: StatusCodes.Status422UnprocessableEntity);

            default:
                logger.LogWarning("Switching the display scale failed: {Message}", result.Message);
                return Results.Json(
                    new { error = "dpi_failed", message = result.Message, percent = result.Current, monitor },
                    statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static IResult Monitors(AppConfig config, IScreenCapturer capturer)
    {
        if (!config.Features.Screenshot)
        {
            return Results.Json(new { error = "feature_disabled" }, statusCode: StatusCodes.Status403Forbidden);
        }

        return Results.Json(capturer.GetMonitors().Select(m => new
        {
            index = m.Index,
            name = m.Name,
            x = m.Bounds.X,
            y = m.Bounds.Y,
            width = m.Bounds.Width,
            height = m.Bounds.Height,
            primary = m.IsPrimary,
        }));
    }

    private static IResult Screenshot(
        HttpContext context,
        AppConfig config,
        IScreenCapturer capturer,
        IJpegEncoder encoder,
        ILogger<PairRequest> logger,
        int? monitor,
        int? width)
    {
        if (!config.Features.Screenshot)
        {
            return Results.Json(new { error = "feature_disabled" }, statusCode: StatusCodes.Status403Forbidden);
        }

        // Locked screens and UAC prompts cannot be read; say so instead of returning a black picture.
        var unavailable = Results.Json(new { error = "screen_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        if (!capturer.IsDesktopAvailable())
        {
            return unavailable;
        }

        if (!capturer.TryGetCursor(out var cursorX, out var cursorY))
        {
            (cursorX, cursorY) = (RegionPlanner.NoCursor, RegionPlanner.NoCursor);
        }

        var chosen = RegionPlanner.PickMonitor(capturer.GetMonitors(), monitor ?? ViewOptions.FollowCursor, cursorX, cursorY);
        if (chosen is null)
        {
            return unavailable;
        }

        var plan = RegionPlanner.Display(chosen.Bounds, cursorX, cursorY, Math.Clamp(width ?? ViewOptions.DefaultDisplayWidth, 160, ViewOptions.MaxDisplayWidth));
        var frame = capturer.Capture(plan.Source, plan.OutputWidth, plan.OutputHeight);
        if (frame is null)
        {
            return unavailable;
        }

        logger.LogInformation("{DeviceName} took a screenshot of monitor {Monitor}", context.User.Identity!.Name, chosen.Index);

        // The picture has no mouse cursor; these headers say where it was so the phone can draw one.
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Cursor-X"] = plan.CursorX.ToString(System.Globalization.CultureInfo.InvariantCulture);
        context.Response.Headers["X-Cursor-Y"] = plan.CursorY.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Results.File(encoder.Encode(frame, ViewStreamer.JpegQuality), "image/jpeg");
    }

    private static IResult Pair(
        PairRequest request,
        HttpContext context,
        PinService pins,
        PairingRateLimiter limiter,
        DeviceRegistry registry,
        RemoteControlState state,
        ILogger<PairRequest> logger)
    {
        var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (limiter.IsLocked(client, out var retryAfter))
        {
            logger.LogWarning("Pairing refused for {Client}: locked out for another {Seconds:0} s", client, retryAfter.TotalSeconds);
            context.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Results.Json(new { error = "locked_out" }, statusCode: StatusCodes.Status429TooManyRequests);
        }

        var pin = request.Pin;
        if (pin is null || pin.Length > MaxPinLength || !pins.TryConsume(pin))
        {
            limiter.RecordFailure(client);
            logger.LogWarning("Pairing failed from {Client}: wrong or expired PIN", client);
            return Results.Json(new { error = "invalid_pin" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        limiter.RecordSuccess(client);
        var result = registry.Pair(request.DeviceName);
        logger.LogInformation("Paired device {DeviceName} ({DeviceId}) from {Client}", result.Device.Name, result.Device.Id, client);

        context.Response.Cookies.Append(DeviceAuth.CookieName, result.Token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            Path = "/",
            MaxAge = TimeSpan.FromDays(365),
            IsEssential = true,
        });

        // The token is only put in the body for native clients that cannot use cookies.
        return Results.Json(new SessionResponse(
            result.Device.Id,
            result.Device.Name,
            state.Paused,
            request.IssueBearerToken ? result.Token : null));
    }

    private static IResult PairLocal(
        LocalPairRequest request,
        HttpContext context,
        LocalPairing local,
        DeviceRegistry registry,
        RemoteControlState state,
        ILogger<LocalPairing> logger)
    {
        // Wrong place or wrong key looks the same, so nothing is learned from the answer.
        if (!LocalPairing.IsThisPc(context.Connection.RemoteIpAddress) || !local.Matches(request.Key))
        {
            logger.LogWarning("Refused a local pairing attempt from {Client}", context.Connection.RemoteIpAddress);
            return Results.Json(new { error = "invalid_pin" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        // One controller-UI device is enough; drop the ones from earlier runs.
        foreach (var old in registry.List().Where(d => d.Name == LocalPairing.DeviceName).ToList())
        {
            registry.Revoke(old.Id);
        }

        var result = registry.Pair(LocalPairing.DeviceName);
        context.Response.Cookies.Append(DeviceAuth.CookieName, result.Token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            Path = "/",
            MaxAge = TimeSpan.FromDays(365),
            IsEssential = true,
        });
        return Results.Json(new SessionResponse(result.Device.Id, result.Device.Name, state.Paused));
    }

    private static IResult Unpair(HttpContext context, DeviceRegistry registry, ILogger<PairRequest> logger)
    {
        var id = context.User.DeviceId();
        var name = context.User.Identity!.Name;

        if (registry.Revoke(id))
        {
            logger.LogInformation("Device {DeviceName} ({DeviceId}) removed itself", name, id);
        }

        context.Response.Cookies.Delete(DeviceAuth.CookieName, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            Path = "/",
        });
        return Results.NoContent();
    }
}
