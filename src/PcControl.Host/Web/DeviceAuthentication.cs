using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PcControl.Core.Pairing;

namespace PcControl.Host.Web;

internal static class DeviceAuth
{
    public const string Scheme = "Device";
    public const string CookieName = "pc_device";
    public const string TokenSourceClaim = "token_source";
    public const string BearerSource = "bearer";
    public const string CookieSource = "cookie";

    public static Guid DeviceId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

/// <summary>Accepts the device token from an Authorization: Bearer header or the pc_device cookie.</summary>
internal sealed class DeviceAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    DeviceRegistry registry)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? token = null;
        var source = DeviceAuth.CookieSource;

        var header = Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = header["Bearer ".Length..].Trim();
            source = DeviceAuth.BearerSource;
        }
        else if (Request.Cookies.TryGetValue(DeviceAuth.CookieName, out var cookie))
        {
            token = cookie;
        }

        if (token is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var device = registry.Authenticate(token);
        if (device is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("unknown device token"));
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, device.Id.ToString()),
                new Claim(ClaimTypes.Name, device.Name),
                new Claim(DeviceAuth.TokenSourceClaim, source),
            ],
            DeviceAuth.Scheme);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), DeviceAuth.Scheme)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Response.WriteAsJsonAsync(new { error = "unauthorized" });
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Response.WriteAsJsonAsync(new { error = "forbidden" });
    }
}
