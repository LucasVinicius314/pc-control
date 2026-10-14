using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using PcControl.Core.Network;

namespace PcControl.Host;

internal static class LanAddresses
{
    /// <summary>The PC's private IPv4 addresses, with real (gateway-connected) adapters first.</summary>
    public static IReadOnlyList<IPAddress> Get()
    {
        var found = new List<(IPAddress Address, bool HasGateway)>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var properties = nic.GetIPProperties();
            var hasGateway = properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                                                                 && !g.Address.Equals(IPAddress.Any));

            foreach (var unicast in properties.UnicastAddresses)
            {
                var address = unicast.Address;
                if (NetworkGuard.IsPrivateLan(address) && !address.GetAddressBytes().AsSpan().StartsWith((ReadOnlySpan<byte>)[169, 254]))
                {
                    found.Add((address, hasGateway));
                }
            }
        }

        return [.. found.OrderByDescending(f => f.HasGateway).Select(f => f.Address)];
    }
}
