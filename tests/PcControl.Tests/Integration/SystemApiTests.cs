using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PcControl.Core.Actions;

namespace PcControl.Tests.Integration;

public class SystemApiTests
{
    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task Capabilities_NowIncludeTheThreeSystemFeatures()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var caps = await server.Http.GetFromJsonAsync<Dictionary<string, bool>>("/api/capabilities");

        Assert.True(caps!["sendUrl"]);
        Assert.True(caps["power"]);
        Assert.True(caps["processKiller"]);
    }

    [Fact]
    public async Task Everything_NeedsPairing()
    {
        await using var server = await TestServer.StartAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await server.Http.PostAsync("/api/url", Json("{\"url\":\"example.com\"}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.Http.PostAsync("/api/power", Json("{\"action\":\"lock\"}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.Http.GetAsync("/api/processes")).StatusCode);
        Assert.Empty(server.Urls.Opened);
        Assert.Empty(server.PowerBackend.Performed);
    }

    [Fact]
    public async Task OpeningALink_GoesToTheBrowser()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var response = await server.Http.PostAsync("/api/url", Json("{\"url\":\"example.com/watch\"}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["https://example.com/watch"], server.Urls.Opened.Select(u => u.AbsoluteUri));
    }

    [Theory]
    [InlineData("{\"url\":\"file:///C:/Windows/notepad.exe\"}")]
    [InlineData("{\"url\":\"ms-settings:display\"}")]
    [InlineData("{\"url\":\"\"}")]
    [InlineData("{}")]
    public async Task OtherThanWebLinks_AreRefused(string body)
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var response = await server.Http.PostAsync("/api/url", Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(server.Urls.Opened);
    }

    [Fact]
    public async Task WhenWindowsCannotOpenIt_TheAnswerSaysSo()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        server.Urls.Succeeds = false;

        var response = await server.Http.PostAsync("/api/url", Json("{\"url\":\"example.com\"}"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task WhilePaused_NothingHappens()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        var list = await server.Http.GetFromJsonAsync<JsonElement>("/api/processes");
        var id = list[0].GetProperty("id").GetString();
        server.State.Paused = true;

        Assert.Equal(HttpStatusCode.Conflict, (await server.Http.PostAsync("/api/url", Json("{\"url\":\"example.com\"}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await server.Http.PostAsync("/api/power", Json("{\"action\":\"lock\"}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await server.Http.PostAsync($"/api/processes/{id}/close", Json("{}"))).StatusCode);

        Assert.Empty(server.Urls.Opened);
        Assert.Empty(server.PowerBackend.Performed);
        Assert.Empty(server.Processes.Closed);
    }

    [Fact]
    public async Task Locking_NeedsNoConfirmation()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var response = await server.Http.PostAsync("/api/power", Json("{\"action\":\"lock\"}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([(PowerAction.Lock, 0)], server.PowerBackend.Performed);
    }

    [Theory]
    [InlineData("restart")]
    [InlineData("shutdown")]
    [InlineData("signOut")]
    public async Task EndingTheSession_NeedsConfirmation(string action)
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var response = await server.Http.PostAsync("/api/power", Json($"{{\"action\":\"{action}\"}}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("confirmation_required", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Empty(server.PowerBackend.Performed);
    }

    [Fact]
    public async Task AConfirmedShutdown_CountsDown_AndCanBeCancelled()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var response = await server.Http.PostAsync("/api/power", Json("{\"action\":\"shutdown\",\"confirm\":true}"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(PowerService.DefaultDelaySeconds, body.GetProperty("delaySeconds").GetInt32());

        var status = await server.Http.GetFromJsonAsync<JsonElement>("/api/power");
        Assert.Equal("shutdown", status.GetProperty("pending").GetProperty("action").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await server.Http.DeleteAsync("/api/power/pending")).StatusCode);
        Assert.Equal(1, server.PowerBackend.Cancels);
        Assert.Equal(HttpStatusCode.NotFound, (await server.Http.DeleteAsync("/api/power/pending")).StatusCode);
    }

    [Fact]
    public async Task CancellingStaysPossible_WhilePaused()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        await server.Http.PostAsync("/api/power", Json("{\"action\":\"restart\",\"confirm\":true}"));
        server.State.Paused = true;

        Assert.Equal(HttpStatusCode.NoContent, (await server.Http.DeleteAsync("/api/power/pending")).StatusCode);
    }

    [Fact]
    public async Task AnUnknownAction_IsRefused()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var response = await server.Http.PostAsync("/api/power", Json("{\"action\":\"hibernate\",\"confirm\":true}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(server.PowerBackend.Performed);
    }

    [Fact]
    public async Task TheProcessList_HasOnlyUserPrograms_WithoutProcessNumbers()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var text = await server.Http.GetStringAsync("/api/processes");

        Assert.Contains("Untitled - Notepad", text, StringComparison.Ordinal);
        Assert.DoesNotContain("File Explorer", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"pid\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClosingAProgram_UsesItsIdFromTheList()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        var list = await server.Http.GetFromJsonAsync<JsonElement>("/api/processes");
        var chrome = list.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "chrome").GetProperty("id").GetString();

        var response = await server.Http.PostAsync($"/api/processes/{chrome}/close", Json("{\"force\":true}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([(200, true)], server.Processes.Closed);
    }

    [Fact]
    public async Task ClosingWithAnEmptyBody_IsAGentleClose()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        var list = await server.Http.GetFromJsonAsync<JsonElement>("/api/processes");
        var id = list[0].GetProperty("id").GetString();

        var response = await server.Http.PostAsync($"/api/processes/{id}/close", Json("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(server.Processes.Closed.Single().Force);
    }

    [Fact]
    public async Task SwitchingToAProgram_BringsItToTheFront()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        var list = await server.Http.GetFromJsonAsync<JsonElement>("/api/processes");
        var chrome = list.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "chrome").GetProperty("id").GetString();

        var response = await server.Http.PostAsync($"/api/processes/{chrome}/focus", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([200], server.Processes.Focused);
        Assert.Empty(server.Processes.Closed);
    }

    [Fact]
    public async Task SwitchingToAnUnlistedProgram_OrWhilePaused_DoesNothing()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        var list = await server.Http.GetFromJsonAsync<JsonElement>("/api/processes");
        var id = list[0].GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.NotFound, (await server.Http.PostAsync("/api/processes/0000000000000000/focus", null)).StatusCode);
        server.State.Paused = true;
        Assert.Equal(HttpStatusCode.Conflict, (await server.Http.PostAsync($"/api/processes/{id}/focus", null)).StatusCode);
        Assert.Empty(server.Processes.Focused);
    }

    [Theory]
    [InlineData("100")]
    [InlineData("0000000000000000")]
    [InlineData("explorer")]
    public async Task AnythingNotFromTheList_IsRefused(string id)
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();
        await server.Http.GetStringAsync("/api/processes");

        var response = await server.Http.PostAsync($"/api/processes/{id}/close", Json("{\"force\":true}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(server.Processes.Closed);
    }
}
