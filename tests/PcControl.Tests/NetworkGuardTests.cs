using System.Net;
using PcControl.Core.Network;

namespace PcControl.Tests;

public class NetworkGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.1.20")]
    [InlineData("169.254.10.10")]
    [InlineData("::ffff:192.168.1.20")]
    [InlineData("fe80::1")]
    [InlineData("fd12:3456:789a::1")]
    public void Allows_PrivateAndLoopback(string ip)
    {
        Assert.True(NetworkGuard.IsAllowed(IPAddress.Parse(ip), allowTailscaleRange: false));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("192.169.1.1")]
    [InlineData("100.100.1.1")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("2001:4860:4860::8888")]
    public void Rejects_PublicAddresses(string ip)
    {
        Assert.False(NetworkGuard.IsAllowed(IPAddress.Parse(ip), allowTailscaleRange: false));
    }

    [Fact]
    public void Rejects_Null()
    {
        Assert.False(NetworkGuard.IsAllowed(null, allowTailscaleRange: false));
    }

    [Theory]
    [InlineData("100.64.0.1", true)]
    [InlineData("100.127.255.254", true)]
    [InlineData("100.63.255.255", false)]
    [InlineData("100.128.0.1", false)]
    public void TailscaleRange_OnlyWhenEnabled(string ip, bool expectedWhenEnabled)
    {
        var address = IPAddress.Parse(ip);

        Assert.False(NetworkGuard.IsAllowed(address, allowTailscaleRange: false));
        Assert.Equal(expectedWhenEnabled, NetworkGuard.IsAllowed(address, allowTailscaleRange: true));
    }

    [Theory]
    [InlineData("192.168.1.5", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.1.1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("fe80::1", false)]
    public void IsPrivateLan_IsIpv4PrivateOnly(string ip, bool expected)
    {
        Assert.Equal(expected, NetworkGuard.IsPrivateLan(IPAddress.Parse(ip)));
    }
}
