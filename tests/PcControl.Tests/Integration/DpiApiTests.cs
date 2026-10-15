using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PcControl.Tests.Integration;

public class DpiApiTests
{
    [Fact]
    public async Task Get_ReportsTheCurrentScaleAndTheTarget()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var dpi = await server.Http.GetFromJsonAsync<JsonElement>("/api/dpi");

        Assert.True(dpi.GetProperty("available").GetBoolean());
        Assert.Equal(100, dpi.GetProperty("percent").GetInt32());
        Assert.Equal(100, dpi.GetProperty("lowPercent").GetInt32());
        Assert.Equal(200, dpi.GetProperty("highPercent").GetInt32());
        Assert.Equal(0, dpi.GetProperty("monitor").GetProperty("index").GetInt32());
        Assert.Equal("fake", dpi.GetProperty("backend").GetString());
    }

    [Fact]
    public async Task Get_ReadsTheScaleFresh_EachTime()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        server.Scale.Scale[0] = 150; // changed in Windows Settings, not through us
        var dpi = await server.Http.GetFromJsonAsync<JsonElement>("/api/dpi");

        Assert.Equal(150, dpi.GetProperty("percent").GetInt32());
    }

    [Fact]
    public async Task Toggle_FlipsTheScale_AndSaysSomeAppsStayBlurry()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var response = await server.Http.PostAsync("/api/dpi/toggle", null);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(100, body.GetProperty("previous").GetInt32());
        Assert.Equal(200, body.GetProperty("percent").GetInt32());
        Assert.Contains("blurry", body.GetProperty("note").GetString(), StringComparison.Ordinal);
        Assert.Equal(200, server.Scale.Scale[0]);
    }

    [Fact]
    public async Task Toggle_Twice_ComesBack()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        await server.Http.PostAsync("/api/dpi/toggle", null);
        var back = await (await server.Http.PostAsync("/api/dpi/toggle", null)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(100, back.GetProperty("percent").GetInt32());
    }

    [Fact]
    public async Task Toggle_OnAMonitorThatCannotGoThatHigh_Is422_WithTheReason()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        server.Scale.Maximum = 175;

        var response = await server.Http.PostAsync("/api/dpi/toggle", null);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("scale_not_supported", body.GetProperty("error").GetString());
        Assert.Contains("175%", body.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(100, body.GetProperty("percent").GetInt32());
        Assert.Empty(server.Scale.Requests);
    }

    [Fact]
    public async Task Toggle_WhenWindowsIgnoresTheRequest_Is502_NotASuccess()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        server.Scale.ChangeTakesEffect = false;

        var response = await server.Http.PostAsync("/api/dpi/toggle", null);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("dpi_failed", body.GetProperty("error").GetString());
        Assert.Equal(100, body.GetProperty("percent").GetInt32());
    }

    [Fact]
    public async Task WhenTheSelfTestFailed_TheToggleIsOff_Everywhere()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        server.Scale.CanRead = false;
        server.Dpi.Check();

        var dpi = await server.Http.GetFromJsonAsync<JsonElement>("/api/dpi");
        var toggle = await server.Http.PostAsync("/api/dpi/toggle", null);
        var caps = await server.Http.GetFromJsonAsync<Dictionary<string, bool>>("/api/capabilities");

        Assert.False(dpi.GetProperty("available").GetBoolean());
        Assert.Contains("Windows did not answer", dpi.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, toggle.StatusCode);
        Assert.False(caps!["dpiToggle"]);
        Assert.Empty(server.Scale.Requests);
    }

    [Fact]
    public async Task TheCapability_IsOn_WhenTheSelfTestPassed()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var caps = await server.Http.GetFromJsonAsync<Dictionary<string, bool>>("/api/capabilities");

        Assert.True(caps!["dpiToggle"]);
    }

    [Theory]
    [InlineData("GET", "/api/dpi")]
    [InlineData("POST", "/api/dpi/toggle")]
    public async Task DpiEndpoints_RequireAToken(string method, string path)
    {
        await using var server = await TestServer.StartAsync();

        var response = await server.Http.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(server.Scale.Requests);
    }

    [Fact]
    public async Task Toggle_FromAForeignOrigin_IsRefused()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/dpi/toggle");
        request.Headers.Add("Origin", "http://evil.example.com");

        var response = await server.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(server.Scale.Requests);
    }

    [Fact]
    public async Task TwoQuickToggles_DoNotUndoEachOther()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        server.Scale.ReadsUntilApplied = 3;

        var results = await Task.WhenAll(
            server.Http.PostAsync("/api/dpi/toggle", null),
            server.Http.PostAsync("/api/dpi/toggle", null));

        // Whatever the timing, the scale was asked to change only once or both finished in turn:
        // it must never end up with two overlapping changes cancelling each other out mid-way.
        var codes = results.Select(r => r.StatusCode).OrderBy(c => c).ToList();
        Assert.Contains(HttpStatusCode.OK, codes);
        Assert.True(codes.Count(c => c == HttpStatusCode.OK) == 1 ? codes.Contains(HttpStatusCode.Conflict) : server.Scale.Scale[0] == 100,
            $"statuses: {string.Join(", ", codes)}");
    }
}
