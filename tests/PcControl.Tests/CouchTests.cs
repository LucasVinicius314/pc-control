using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PcControl.Core.Config;
using PcControl.Core.Input;
using PcControl.Host.Web;
using PcControl.Tests.Integration;

namespace PcControl.Tests;

public class GuideButtonDetectorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void APress_IsReportedOnce_NotWhileHeld()
    {
        var detector = new GuideButtonDetector();

        Assert.True(detector.Update(GuideButtonDetector.GuideMask, T0));
        Assert.False(detector.Update(GuideButtonDetector.GuideMask, T0.AddMilliseconds(40)));
        Assert.False(detector.Update(GuideButtonDetector.GuideMask, T0.AddSeconds(5)));
    }

    [Fact]
    public void ANewPress_AfterReleasing_IsReportedAgain()
    {
        var detector = new GuideButtonDetector();
        detector.Update(GuideButtonDetector.GuideMask, T0);
        detector.Update(0, T0.AddMilliseconds(100));

        Assert.True(detector.Update(GuideButtonDetector.GuideMask, T0.AddSeconds(1)));
    }

    [Fact]
    public void ATrembleWithinTheDebounceTime_CountsAsOnePress()
    {
        var detector = new GuideButtonDetector();
        Assert.True(detector.Update(GuideButtonDetector.GuideMask, T0));
        detector.Update(0, T0.AddMilliseconds(40));

        Assert.False(detector.Update(GuideButtonDetector.GuideMask, T0.AddMilliseconds(80)));
    }

    [Fact]
    public void OtherButtons_AreIgnored()
    {
        var detector = new GuideButtonDetector();

        Assert.False(detector.Update(0x1000 | 0x0010, T0)); // A and Start
    }

    [Fact]
    public void AGuideButtonStillHeldWhenReset_IsAPressWhenSeenAgain()
    {
        var detector = new GuideButtonDetector();
        detector.Update(GuideButtonDetector.GuideMask, T0);
        detector.Reset();

        Assert.True(detector.Update(GuideButtonDetector.GuideMask, T0.AddSeconds(2)));
    }
}

public class CouchConfigTests
{
    [Fact]
    public void ByDefault_TheControllerUiIsOn_WithTheXboxButton_ButNotAtStart()
    {
        var couch = new AppConfig().Couch;

        Assert.True(couch.Enabled);
        Assert.True(couch.GuideButton);
        Assert.False(couch.OpenAtStart);
    }

    [Fact]
    public void TheGamesSetting_DefaultsToAuto_AndIsValid()
    {
        var config = new AppConfig();

        Assert.Equal("auto", config.Games.Rpcs3Path);
        Assert.True(config.Games.Rpcs3Fullscreen);
        Assert.Empty(ConfigValidator.Validate(config));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyRpcs3Path_IsRefused(string path)
    {
        var config = new AppConfig { Games = new GamesConfig { Rpcs3Path = path } };

        Assert.NotEmpty(ConfigValidator.Validate(config));
    }
}

public class LocalPairingTests
{
    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [Fact]
    public void TheKey_IsLongRandomAndUrlSafe()
    {
        var a = new LocalPairing();
        var b = new LocalPairing();

        Assert.NotEqual(a.Key, b.Key);
        Assert.Matches("^[A-Za-z0-9_-]{40,}$", a.Key);
    }

    [Fact]
    public void OnlyTheExactKeyMatches()
    {
        var local = new LocalPairing();

        Assert.True(local.Matches(local.Key));
        Assert.False(local.Matches(local.Key + "x"));
        Assert.False(local.Matches(local.Key[..^1]));
        Assert.False(local.Matches(""));
        Assert.False(local.Matches(null));
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("192.168.0.20", false)]
    [InlineData("10.0.0.5", false)]
    [InlineData("::ffff:192.168.0.20", false)]
    public void OnlyThisPc_Counts(string address, bool expected)
    {
        Assert.Equal(expected, LocalPairing.IsThisPc(IPAddress.Parse(address)));
        Assert.False(LocalPairing.IsThisPc(null));
    }

    [Fact]
    public async Task WithTheKey_ThisPcGetsASession_ThatWorksForTheApi()
    {
        await using var server = await TestServer.StartAsync();

        var response = await server.Http.PostAsync("/api/pair/local", Json($"{{\"key\":\"{server.Local.Key}\"}}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(LocalPairing.DeviceName, session.GetProperty("deviceName").GetString());
        Assert.Equal(HttpStatusCode.OK, (await server.Http.GetAsync("/api/games")).StatusCode);
    }

    [Theory]
    [InlineData("{\"key\":\"wrong\"}")]
    [InlineData("{\"key\":\"\"}")]
    [InlineData("{}")]
    public async Task WithoutTheKey_NothingIsIssued(string body)
    {
        await using var server = await TestServer.StartAsync();

        var response = await server.Http.PostAsync("/api/pair/local", Json(body));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(server.Registry.List());
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.Http.GetAsync("/api/games")).StatusCode);
    }

    [Fact]
    public async Task Pairing_ReplacesTheDeviceFromAnEarlierRun_InsteadOfPilingUp()
    {
        await using var server = await TestServer.StartAsync();

        for (var i = 0; i < 3; i++)
        {
            var response = await server.Http.PostAsync("/api/pair/local", Json($"{{\"key\":\"{server.Local.Key}\"}}"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Single(server.Registry.List(), d => d.Name == LocalPairing.DeviceName);
    }

    [Fact]
    public async Task ThePhonesDevices_AreLeftAlone()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync("My phone");

        using var other = new HttpClient { BaseAddress = server.BaseUri };
        var response = await other.PostAsync("/api/pair/local", Json($"{{\"key\":\"{server.Local.Key}\"}}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(server.Registry.List(), d => d.Name == "My phone");
    }
}
