namespace PcControl.Core.Network;

/// <summary>Same-origin check used against cross-site request forgery and WebSocket hijacking.</summary>
public static class OriginPolicy
{
    /// <summary>
    /// True when <paramref name="origin"/> is an http(s) origin whose host and port equal the
    /// request's own <paramref name="hostHeader"/>. A missing or "null" origin never matches.
    /// </summary>
    public static bool IsSameOrigin(string? origin, string? hostHeader)
    {
        if (string.IsNullOrEmpty(origin) || string.IsNullOrEmpty(hostHeader))
        {
            return false;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var o)
            || (o.Scheme != Uri.UriSchemeHttp && o.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(o.UserInfo)
            || o.AbsolutePath != "/"
            || !string.IsNullOrEmpty(o.Query)
            || !string.IsNullOrEmpty(o.Fragment))
        {
            return false;
        }

        if (!Uri.TryCreate($"{o.Scheme}://{hostHeader}", UriKind.Absolute, out var h))
        {
            return false;
        }

        return string.Equals(o.Host, h.Host, StringComparison.OrdinalIgnoreCase) && o.Port == h.Port;
    }
}
