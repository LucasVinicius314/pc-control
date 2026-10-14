using PcControl.Core.Config;
using PcControl.Core.Network;

namespace PcControl.Host.Web;

/// <summary>
/// First line of defence for every request: private-network clients only, a plausible Host
/// header, and same-origin for anything that changes state.
/// </summary>
internal sealed class GuardMiddleware(RequestDelegate next, AppConfig config, ILogger<GuardMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        if (!NetworkGuard.IsAllowed(remote, config.AllowTailscaleRange))
        {
            logger.LogWarning("Rejected request from non-private address {Remote}", remote);
            await Reject(context, StatusCodes.Status403Forbidden, "forbidden");
            return;
        }

        if (!HostPolicy.IsAllowedHost(context.Request.Host.Value, config.AllowTailscaleRange))
        {
            logger.LogWarning("Rejected request with unexpected Host header from {Remote}", remote);
            await Reject(context, StatusCodes.Status403Forbidden, "forbidden");
            return;
        }

        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)
            && !HttpMethods.IsOptions(context.Request.Method))
        {
            // Browsers always send Origin on cross-site POSTs, so a mismatch means a hostile page.
            // Native clients send none and authenticate with a bearer token instead of a cookie.
            var origin = context.Request.Headers.Origin.ToString();
            if (origin.Length > 0 && !OriginPolicy.IsSameOrigin(origin, context.Request.Host.Value))
            {
                logger.LogWarning("Rejected cross-origin {Method} from {Remote} (origin {Origin})", context.Request.Method, remote, origin);
                await Reject(context, StatusCodes.Status403Forbidden, "forbidden");
                return;
            }
        }

        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Content-Security-Policy"] =
            "default-src 'self'; img-src 'self' data: blob:; style-src 'self'; script-src 'self'; " +
            "connect-src 'self' ws: wss:; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";

        await next(context);
    }

    private static Task Reject(HttpContext context, int status, string error)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { error });
    }
}
