using System.Net;

namespace PcControl.Core.Network;

/// <summary>
/// Accepts only the kinds of Host header a phone on the home network would use. This blocks DNS
/// rebinding, where a hostile website points its own domain at the PC's LAN address.
/// </summary>
public static class HostPolicy
{
    public static bool IsAllowedHost(string? hostHeader, bool allowTailscaleNames)
    {
        if (string.IsNullOrWhiteSpace(hostHeader) || hostHeader.Length > 255)
        {
            return false;
        }

        if (!Uri.TryCreate($"http://{hostHeader}", UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath != "/")
        {
            return false;
        }

        var host = uri.Host;
        if (IPAddress.TryParse(host.Trim('[', ']'), out _))
        {
            return true;
        }

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || !host.Contains('.', StringComparison.Ordinal))
        {
            return true;
        }

        return allowTailscaleNames && host.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase);
    }
}
