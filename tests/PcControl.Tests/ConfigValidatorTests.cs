using PcControl.Core.Config;

namespace PcControl.Tests;

public class ConfigValidatorTests
{
    [Fact]
    public void DefaultConfig_IsValid()
    {
        Assert.Empty(ConfigValidator.Validate(new AppConfig()));
        Assert.Equal(48123, new AppConfig().Port);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(80)]
    [InlineData(1023)]
    [InlineData(65536)]
    [InlineData(-1)]
    public void BadPort_IsReported(int port)
    {
        var errors = ConfigValidator.Validate(new AppConfig { Port = port });

        Assert.Contains(errors, e => e.StartsWith("port", StringComparison.Ordinal));
    }

    [Fact]
    public void BadPairingValues_AreAllReported()
    {
        var config = new AppConfig
        {
            Pairing = new PairingConfig
            {
                PinLifetimeMinutes = 0,
                MaxFailuresPerPin = 0,
                MaxFailedAttempts = 0,
                AttemptWindowMinutes = 0,
                LockoutMinutes = 0,
            },
        };

        Assert.Equal(5, ConfigValidator.Validate(config).Count);
    }
}
