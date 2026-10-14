using PcControl.Core.Network;

namespace PcControl.Tests;

public class HostPolicyTests
{
    [Theory]
    [InlineData("192.168.1.5:48123")]
    [InlineData("192.168.1.5")]
    [InlineData("[fe80::1]:48123")]
    [InlineData("localhost:48123")]
    [InlineData("MYPC:48123")]
    [InlineData("mypc.local:48123")]
    public void Allows_LanHosts(string host)
    {
        Assert.True(HostPolicy.IsAllowedHost(host, allowTailscaleNames: false));
    }

    [Theory]
    [InlineData("evil.example.com")]
    [InlineData("evil.example.com:48123")]
    [InlineData("192.168.1.5.evil.com")]
    [InlineData("user@192.168.1.5")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("a b")]
    public void Rejects_OtherHosts(string? host)
    {
        Assert.False(HostPolicy.IsAllowedHost(host, allowTailscaleNames: false));
    }

    [Fact]
    public void TailscaleNames_OnlyWhenEnabled()
    {
        Assert.False(HostPolicy.IsAllowedHost("pc.tail1234.ts.net", allowTailscaleNames: false));
        Assert.True(HostPolicy.IsAllowedHost("pc.tail1234.ts.net", allowTailscaleNames: true));
    }
}
