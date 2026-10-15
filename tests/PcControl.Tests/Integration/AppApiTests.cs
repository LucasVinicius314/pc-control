using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PcControl.Core.Apps;

namespace PcControl.Tests.Integration;

public class AppApiTests
{
    private static string Id(string launchId) => AppId.From(launchId);

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task TheList_HasEveryApp_Alphabetically_WithTheirIds()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var body = await server.Http.GetFromJsonAsync<JsonElement>("/api/apps");

        var names = body.GetProperty("apps").EnumerateArray().Select(a => a.GetProperty("name").GetString()).ToList();
        Assert.Equal(["Calculator", "Édge", "Notepad"], names);
        Assert.Equal(Id("notepad-id"), body.GetProperty("apps")[2].GetProperty("id").GetString());
        Assert.Empty(body.GetProperty("recent").EnumerateArray());
    }

    [Fact]
    public async Task TheList_NeverContainsALaunchId()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var text = await server.Http.GetStringAsync("/api/apps");

        Assert.DoesNotContain("notepad-id", text, StringComparison.Ordinal);
        Assert.DoesNotContain("calc-id", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheList_IsReadFreshEachTime_SoNewAppsShowUp()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        await server.Http.GetStringAsync("/api/apps");

        server.Catalog.Apps.Add(AppEntry.Create("Brand new", "new-id"));
        var body = await server.Http.GetFromJsonAsync<JsonElement>("/api/apps");

        Assert.Contains(body.GetProperty("apps").EnumerateArray(), a => a.GetProperty("name").GetString() == "Brand new");
    }

    [Fact]
    public async Task Launching_StartsTheApp_AndItAppearsInRecent()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var response = await server.Http.PostAsync($"/api/apps/{Id("calc-id")}/launch", null);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var list = await server.Http.GetFromJsonAsync<JsonElement>("/api/apps");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Calculator", body.GetProperty("name").GetString());
        Assert.Equal(["calc-id"], server.Catalog.Launched);
        Assert.Equal([Id("calc-id")], list.GetProperty("recent").EnumerateArray().Select(r => r.GetString()));
    }

    [Theory]
    [InlineData("0000000000000000")]
    [InlineData("calc-id")] // a raw launch id must not work in place of the opaque id
    [InlineData("steam%3A%2F%2Frungameid%2F1")]
    [InlineData("..%2F..%2Fwindows")]
    public async Task LaunchingAnUnknownId_Is404_AndStartsNothing(string id)
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var response = await server.Http.PostAsync($"/api/apps/{id}/launch", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(server.Catalog.Launched);
    }

    [Fact]
    public async Task AnAppWindowsCannotStart_Is502_AndIsNotRemembered()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        server.Catalog.LaunchSucceeds = false;

        var response = await server.Http.PostAsync($"/api/apps/{Id("calc-id")}/launch", null);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var list = await server.Http.GetFromJsonAsync<JsonElement>("/api/apps");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("launch_failed", body.GetProperty("error").GetString());
        Assert.Contains("Calculator", body.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(list.GetProperty("recent").EnumerateArray());
    }

    [Fact]
    public async Task WhileTheKillSwitchIsOn_NothingIsLaunched_ButTheListStillLoads()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        server.State.Paused = true;

        var launch = await server.Http.PostAsync($"/api/apps/{Id("calc-id")}/launch", null);
        var body = await launch.Content.ReadFromJsonAsync<JsonElement>();
        var list = await server.Http.GetAsync("/api/apps");

        Assert.Equal(HttpStatusCode.Conflict, launch.StatusCode);
        Assert.Equal("paused", body.GetProperty("error").GetString());
        Assert.Empty(server.Catalog.Launched);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task Favorites_CanBeSetAndCleared_AndAreInTheList()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var set = await server.Http.PutAsync($"/api/apps/{Id("edge-id")}/favorite", Json("""{"favorite":true}"""));
        var after = await server.Http.GetFromJsonAsync<JsonElement>("/api/apps");
        var favorites = after.GetProperty("apps").EnumerateArray().Where(a => a.GetProperty("favorite").GetBoolean()).Select(a => a.GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
        Assert.Equal(["Édge"], favorites);

        await server.Http.PutAsync($"/api/apps/{Id("edge-id")}/favorite", Json("""{"favorite":false}"""));
        var cleared = await server.Http.GetFromJsonAsync<JsonElement>("/api/apps");
        Assert.DoesNotContain(cleared.GetProperty("apps").EnumerateArray(), a => a.GetProperty("favorite").GetBoolean());
    }

    [Fact]
    public async Task Favorites_AreSavedToTheStore()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        await server.Http.PutAsync($"/api/apps/{Id("edge-id")}/favorite", Json("""{"favorite":true}"""));

        Assert.Equal(["edge-id"], server.AppsStore.State.Favorites);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"favorite":"yes"}""")]
    [InlineData("""{"favorite":null}""")]
    public async Task AMalformedFavoriteRequest_Is400_NotASilentUnfavorite(string json)
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        server.AppsStore.State = new AppsState(["edge-id"], []);

        var response = await server.Http.PutAsync($"/api/apps/{Id("edge-id")}/favorite", Json(json));

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest);
        Assert.Equal(["edge-id"], server.AppsStore.State.Favorites);
    }

    [Fact]
    public async Task FavoritingAnUnknownApp_Is404()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var response = await server.Http.PutAsync("/api/apps/0000000000000000/favorite", Json("""{"favorite":true}"""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Favorites_StillWorkWhileTheKillSwitchIsOn()
    {
        // Marking a favorite changes nothing on the PC; only actions are blocked.
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        server.State.Paused = true;

        var response = await server.Http.PutAsync($"/api/apps/{Id("edge-id")}/favorite", Json("""{"favorite":true}"""));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Icons_ArePngs_ThatThePhoneMayKeepForAWhile()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var response = await server.Http.GetAsync($"/api/apps/{Id("calc-id")}/icon");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("max-age=3600", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.Equal(server.Catalog.Icon, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AnAppWithoutAnIcon_Is404()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        server.Catalog.Icon = null;

        Assert.Equal(HttpStatusCode.NotFound, (await server.Http.GetAsync($"/api/apps/{Id("calc-id")}/icon")).StatusCode);
    }

    [Fact]
    public async Task IconsOfUnknownApps_Are404()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await server.Http.GetAsync("/api/apps/0000000000000000/icon")).StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/apps")]
    [InlineData("GET", "/api/apps/0123456789abcdef/icon")]
    [InlineData("POST", "/api/apps/0123456789abcdef/launch")]
    [InlineData("PUT", "/api/apps/0123456789abcdef/favorite")]
    [InlineData("POST", "/api/powertoys-run")]
    public async Task EveryLauncherEndpoint_RequiresAToken(string method, string path)
    {
        await using var server = await TestServer.StartAsync();

        var response = await server.Http.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(server.Catalog.Launched);
        Assert.Empty(server.Injector.Events);
    }

    [Fact]
    public async Task Launching_FromAForeignOrigin_IsRefused()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/apps/{Id("calc-id")}/launch");
        request.Headers.Add("Origin", "http://evil.example.com");

        var response = await server.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(server.Catalog.Launched);
    }

    [Fact]
    public async Task TheCapabilities_ListTheLauncherFeatures()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var caps = await server.Http.GetFromJsonAsync<Dictionary<string, bool>>("/api/capabilities");

        Assert.True(caps!["appLauncher"]);
        Assert.True(caps["search"]);
        Assert.True(caps["powerToysRun"]);
    }

    // ---- PowerToys Run button ----

    [Fact]
    public async Task TheButton_PressesAltSpace_WhenPowerToysIsNotInstalled()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var response = await server.Http.PostAsync("/api/powertoys-run", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["key A4 down", "key 20 down", "key 20 up", "key A4 up"], server.Injector.Events);
    }

    [Fact]
    public async Task TheButton_PressesWhateverPowerToysIsSetTo()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        server.PowerToys.Json = """{"properties":{"open_powerlauncher":{"win":true,"ctrl":false,"alt":false,"shift":true,"code":32}}}""";

        await server.Http.PostAsync("/api/powertoys-run", null);

        Assert.Equal(["key A0 down", "key 5B down", "key 20 down", "key 20 up", "key 5B up", "key A0 up"], server.Injector.Events);
    }

    [Fact]
    public async Task TheButton_FollowsAChangeMadeInPowerToys_WithoutARestart()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        await server.Http.PostAsync("/api/powertoys-run", null);
        server.Injector.Clear();
        server.PowerToys.Json = """{"properties":{"open_powerlauncher":{"win":false,"ctrl":true,"alt":false,"shift":false,"code":81}}}""";
        await server.Http.PostAsync("/api/powertoys-run", null);

        Assert.Equal(["key A2 down", "key 51 down", "key 51 up", "key A2 up"], server.Injector.Events);
    }

    [Fact]
    public async Task TheList_SaysWhichShortcutTheButtonUses_AndWhereItCameFrom()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var without = (await server.Http.GetFromJsonAsync<JsonElement>("/api/apps")).GetProperty("launcher");
        server.PowerToys.Json = """{"properties":{"open_powerlauncher":{"win":true,"ctrl":false,"alt":false,"shift":true,"code":32}}}""";
        var with = (await server.Http.GetFromJsonAsync<JsonElement>("/api/apps")).GetProperty("launcher");

        Assert.Equal("Alt+Space", without.GetProperty("powerToysRunShortcut").GetString());
        Assert.False(without.GetProperty("powerToysDetected").GetBoolean());
        Assert.Equal("Shift+Win+Space", with.GetProperty("powerToysRunShortcut").GetString());
        Assert.True(with.GetProperty("powerToysDetected").GetBoolean());
    }

    [Fact]
    public async Task WhileTheKillSwitchIsOn_TheButtonPressesNothing()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        server.State.Paused = true;

        var response = await server.Http.PostAsync("/api/powertoys-run", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(server.Injector.Events);
    }

    [Fact]
    public async Task TheDisplayScaleToggle_AlsoStopsWhileTheKillSwitchIsOn()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        server.State.Paused = true;

        var response = await server.Http.PostAsync("/api/dpi/toggle", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(server.Scale.Requests);
    }
}
