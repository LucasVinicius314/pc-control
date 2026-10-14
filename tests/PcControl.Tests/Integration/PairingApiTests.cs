using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PcControl.Tests.Integration;

public class PairingApiTests
{
    [Fact]
    public async Task Info_IsPublic()
    {
        await using var server = await TestServer.StartAsync();

        var info = await server.Http.GetFromJsonAsync<JsonElement>("/api/info");

        Assert.Equal("pc-control", info.GetProperty("app").GetString());
        Assert.Equal(1, info.GetProperty("protocolVersion").GetInt32());
    }

    [Fact]
    public async Task Page_IsServed_AndSecurityHeadersAreSet()
    {
        await using var server = await TestServer.StartAsync();

        var response = await server.Http.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("PC Control", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Theory]
    [InlineData("/api/session")]
    [InlineData("/api/capabilities")]
    public async Task SecuredEndpoints_RequireAToken(string path)
    {
        await using var server = await TestServer.StartAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await server.Http.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Pair_WithCorrectPin_SetsHardenedCookie_AndOmitsTokenFromBody()
    {
        await using var server = await TestServer.StartAsync();
        var pin = server.Pins.Issue().Pin;

        var response = await server.Http.PostAsJsonAsync("/api/pair", new { pin, deviceName = "Pixel" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var setCookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pixel", body.GetProperty("deviceName").GetString());
        Assert.False(body.TryGetProperty("token", out _));

        var session = await server.Http.GetFromJsonAsync<JsonElement>("/api/session");
        Assert.Equal("Pixel", session.GetProperty("deviceName").GetString());
    }

    [Fact]
    public async Task Pair_PinIsSingleUse()
    {
        await using var server = await TestServer.StartAsync();
        var pin = server.Pins.Issue().Pin;

        var first = await server.Http.PostAsJsonAsync("/api/pair", new { pin });
        var second = await server.Http.PostAsJsonAsync("/api/pair", new { pin });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
    }

    [Fact]
    public async Task Pair_WithoutAnActivePin_Fails()
    {
        await using var server = await TestServer.StartAsync();

        var response = await server.Http.PostAsJsonAsync("/api/pair", new { pin = "123456" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Pair_LocksOutAfterRepeatedFailures()
    {
        await using var server = await TestServer.StartAsync(maxFailedAttempts: 3);
        server.Pins.Issue();

        for (var i = 0; i < 3; i++)
        {
            var wrong = await server.Http.PostAsJsonAsync("/api/pair", new { pin = "bad" });
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        var correct = server.Pins.Issue().Pin;
        var locked = await server.Http.PostAsJsonAsync("/api/pair", new { pin = correct });

        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.True(locked.Headers.TryGetValues("Retry-After", out var retry));
        Assert.True(int.Parse(retry.Single(), System.Globalization.CultureInfo.InvariantCulture) > 0);
    }

    [Fact]
    public async Task Pair_ForNativeClients_ReturnsBearerToken_ThatAuthenticates()
    {
        await using var server = await TestServer.StartAsync();

        var body = await server.PairAsync(bearer: true);
        var token = body.GetProperty("token").GetString();
        Assert.False(string.IsNullOrEmpty(token));

        using var plain = new HttpClient { BaseAddress = server.BaseUri };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/session");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await plain.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Capabilities_ListsFeatures_AllOffInMilestone1()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var caps = await server.Http.GetFromJsonAsync<Dictionary<string, bool>>("/api/capabilities");

        Assert.NotNull(caps);
        Assert.Contains("trackpad", caps.Keys);
        Assert.All(caps, kv => Assert.False(kv.Value));
    }

    [Fact]
    public async Task DeleteSession_RemovesTheDevice()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var response = await server.Http.DeleteAsync("/api/session");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(server.Registry.List());
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.Http.GetAsync("/api/session")).StatusCode);
    }

    [Fact]
    public async Task ForeignOrigin_IsRejectedOnStateChangingRequests()
    {
        await using var server = await TestServer.StartAsync();
        var pin = server.Pins.Issue().Pin;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/pair")
        {
            Content = JsonContent.Create(new { pin }),
        };
        request.Headers.Add("Origin", "http://evil.example.com");

        var response = await server.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotNull(server.Pins.Current);
    }

    [Fact]
    public async Task UnexpectedHostHeader_IsRejected_AgainstDnsRebinding()
    {
        await using var server = await TestServer.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/info");
        request.Headers.Host = "attacker.example.com";

        var response = await server.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OversizedBody_IsRejected()
    {
        await using var server = await TestServer.StartAsync();
        var huge = new string('1', 200_000);

        var response = await server.Http.PostAsJsonAsync("/api/pair", new { pin = huge });

        Assert.True(response.StatusCode is HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.BadRequest);
    }
}
