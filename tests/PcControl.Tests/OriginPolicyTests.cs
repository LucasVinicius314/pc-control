using PcControl.Core.Network;

namespace PcControl.Tests;

public class OriginPolicyTests
{
    [Theory]
    [InlineData("http://192.168.1.5:48123", "192.168.1.5:48123")]
    [InlineData("http://192.168.1.5:48123", "192.168.1.5:48123 ")]
    [InlineData("HTTP://PC.local:48123", "pc.local:48123")]
    [InlineData("https://pc.local", "pc.local:443")]
    [InlineData("http://[fe80::1]:48123", "[fe80::1]:48123")]
    public void SameOrigin_Matches(string origin, string host)
    {
        Assert.True(OriginPolicy.IsSameOrigin(origin, host.Trim()));
    }

    [Theory]
    [InlineData("http://evil.example", "192.168.1.5:48123")]
    [InlineData("http://192.168.1.5:9999", "192.168.1.5:48123")]
    [InlineData("http://192.168.1.6:48123", "192.168.1.5:48123")]
    [InlineData("null", "192.168.1.5:48123")]
    [InlineData("", "192.168.1.5:48123")]
    [InlineData(null, "192.168.1.5:48123")]
    [InlineData("http://192.168.1.5:48123", null)]
    [InlineData("http://192.168.1.5:48123", "")]
    [InlineData("ftp://192.168.1.5:48123", "192.168.1.5:48123")]
    [InlineData("http://user@192.168.1.5:48123", "192.168.1.5:48123")]
    [InlineData("http://192.168.1.5:48123/path", "192.168.1.5:48123")]
    [InlineData("http://192.168.1.5:48123/?q=1", "192.168.1.5:48123")]
    [InlineData("http://192.168.1.5.evil.example:48123", "192.168.1.5:48123")]
    public void DifferentOrigin_IsRejected(string? origin, string? host)
    {
        Assert.False(OriginPolicy.IsSameOrigin(origin, host));
    }
}
