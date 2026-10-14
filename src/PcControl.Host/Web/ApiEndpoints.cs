using PcControl.Core.Config;
using PcControl.Core.Pairing;
using PcControl.Core.Protocol;

namespace PcControl.Host.Web;

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

        var secured = api.MapGroup(string.Empty).RequireAuthorization();

        secured.MapGet("/session", (HttpContext context, RemoteControlState state) =>
            new SessionResponse(context.User.DeviceId(), context.User.Identity!.Name!, state.Paused));

        secured.MapDelete("/session", Unpair);

        secured.MapGet("/capabilities", (AppConfig config) => Capabilities.Build(config));
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
