using PcControl.Core.Config;
using PcControl.Core.Protocol;

namespace PcControl.Tests;

public class CapabilitiesTests
{
    [Fact]
    public void ListsEveryPlannedFeature()
    {
        var map = Capabilities.Build(new AppConfig());

        Assert.Equal(Capabilities.AllFeatures.Count, map.Count);
    }

    [Fact]
    public void UnimplementedFeatures_AreOff()
    {
        var map = Capabilities.Build(new AppConfig(), new HashSet<string>());

        Assert.All(map, kv => Assert.False(kv.Value));
    }

    [Fact]
    public void ImplementedFeature_FollowsToggle()
    {
        var implemented = new HashSet<string> { "power", "trackpad" };

        var on = Capabilities.Build(new AppConfig(), implemented);
        var off = Capabilities.Build(new AppConfig { Features = new FeatureToggles { Power = false } }, implemented);

        Assert.True(on["power"]);
        Assert.True(on["trackpad"]);
        Assert.False(off["power"]);
        Assert.True(off["trackpad"]);
        Assert.False(on["screenshot"]);
    }
}
