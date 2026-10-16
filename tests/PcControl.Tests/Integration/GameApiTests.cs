using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PcControl.Core.Games;

namespace PcControl.Tests.Integration;

public class GameApiTests
{
    [Fact]
    public async Task EveryGameEndpoint_NeedsAToken()
    {
        await using var server = await TestServer.StartAsync();

        foreach (var (method, path) in new[] { ("GET", "/api/games"), ("GET", "/api/games/0123456789abcdef/cover"), ("GET", "/api/games/0123456789abcdef/background"), ("POST", "/api/games/0123456789abcdef/launch") })
        {
            var response = await server.Http.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task TheList_HasTitlesSerialsAndIds_ButNeverAPath()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var text = await server.Http.GetStringAsync("/api/games");
        var body = JsonSerializer.Deserialize<JsonElement>(text);

        Assert.True(body.GetProperty("available").GetBoolean());
        Assert.Equal(["Bleach", "Édition spéciale", "Sonic Unleashed"], body.GetProperty("games").EnumerateArray().Select(g => g.GetProperty("title").GetString()));
        Assert.DoesNotContain(@"C:\\games", text, StringComparison.Ordinal);
        Assert.DoesNotContain("games\\\\", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutRpcs3_TheListSaysSo_AndTheCapabilityIsOff()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        server.GameLibrary.Available = false;
        server.GameLibrary.UnavailableReason = "RPCS3 was not found.";

        var body = await server.Http.GetFromJsonAsync<JsonElement>("/api/games");
        var caps = await server.Http.GetFromJsonAsync<Dictionary<string, bool>>("/api/capabilities");

        Assert.False(body.GetProperty("available").GetBoolean());
        Assert.Equal("RPCS3 was not found.", body.GetProperty("reason").GetString());
        Assert.Empty(body.GetProperty("games").EnumerateArray());
        Assert.False(caps!["gameLauncher"]);
    }

    [Fact]
    public async Task Launching_StartsTheGame_AndItShowsInRecent()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        var list = await server.Http.GetFromJsonAsync<JsonElement>("/api/games");
        var bleach = list.GetProperty("games").EnumerateArray().Single(g => g.GetProperty("title").GetString() == "Bleach").GetProperty("id").GetString();

        var response = await server.Http.PostAsync($"/api/games/{bleach}/launch", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([@"C:\games\Bleach"], server.GameLibrary.Launched);
        var after = await server.Http.GetFromJsonAsync<JsonElement>("/api/games");
        Assert.Equal([bleach], after.GetProperty("recent").EnumerateArray().Select(r => r.GetString()));
    }

    [Theory]
    [InlineData("0000000000000000")]
    [InlineData("..%2F..%2Fwindows")]
    [InlineData("BLUS30244")]
    public async Task AnUnknownId_IsA404_AndStartsNothing(string id)
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var response = await server.Http.PostAsync($"/api/games/{id}/launch", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(server.GameLibrary.Launched);
    }

    [Fact]
    public async Task AGameThatDoesNotStart_Is502()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        server.GameLibrary.LaunchSucceeds = false;
        var list = await server.Http.GetFromJsonAsync<JsonElement>("/api/games");
        var id = list.GetProperty("games")[0].GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.BadGateway, (await server.Http.PostAsync($"/api/games/{id}/launch", null)).StatusCode);
    }

    [Fact]
    public async Task WhilePaused_NoGameStarts_ButTheListStillLoads()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        var list = await server.Http.GetFromJsonAsync<JsonElement>("/api/games");
        var id = list.GetProperty("games")[0].GetProperty("id").GetString();
        server.State.Paused = true;

        Assert.Equal(HttpStatusCode.Conflict, (await server.Http.PostAsync($"/api/games/{id}/launch", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await server.Http.GetAsync("/api/games")).StatusCode);
        Assert.Empty(server.GameLibrary.Launched);
    }

    [Fact]
    public async Task Images_ArePngs_ThatAreKeptForAWhile()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        var list = await server.Http.GetFromJsonAsync<JsonElement>("/api/games");
        var id = list.GetProperty("games")[0].GetProperty("id").GetString();

        var cover = await server.Http.GetAsync($"/api/games/{id}/cover");
        var background = await server.Http.GetAsync($"/api/games/{id}/background");

        Assert.Equal("image/png", cover.Content.Headers.ContentType?.MediaType);
        Assert.Contains("max-age", cover.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, background.StatusCode);
    }

    [Fact]
    public async Task AGameWithoutAPicture_Is404()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        server.GameLibrary.Image = null;
        var list = await server.Http.GetFromJsonAsync<JsonElement>("/api/games");
        var id = list.GetProperty("games")[0].GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.NotFound, (await server.Http.GetAsync($"/api/games/{id}/cover")).StatusCode);
    }
}
