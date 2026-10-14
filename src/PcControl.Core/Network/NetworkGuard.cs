using System.Net;
using System.Net.Sockets;

namespace PcControl.Core.Network;

/// <summary>Decides whether a remote address belongs to the home network (or this PC).</summary>
public static class NetworkGuard
{
    public static bool IsAllowed(IPAddress? address, bool allowTailscaleRange)
    {
        if (address is null)
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPrivateV4(address.GetAddressBytes(), allowTailscaleRange),
            AddressFamily.InterNetworkV6 => address.IsIPv6LinkLocal || IsUniqueLocalV6(address.GetAddressBytes()),
            _ => false,
        };
    }

    /// <summary>True for private IPv4 addresses a phone on the home network would have.</summary>
    public static bool IsPrivateLan(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return address.AddressFamily == AddressFamily.InterNetwork
            && !IPAddress.IsLoopback(address)
            && IsPrivateV4(address.GetAddressBytes(), allowTailscaleRange: false);
    }

    private static bool IsPrivateV4(byte[] b, bool allowTailscaleRange) =>
        b[0] == 10
        || (b[0] == 172 && b[1] is >= 16 and <= 31)
        || (b[0] == 192 && b[1] == 168)
        || (b[0] == 169 && b[1] == 254)
        || (allowTailscaleRange && b[0] == 100 && b[1] is >= 64 and <= 127);

    // fc00::/7
    private static bool IsUniqueLocalV6(byte[] b) => (b[0] & 0xFE) == 0xFC;
}
