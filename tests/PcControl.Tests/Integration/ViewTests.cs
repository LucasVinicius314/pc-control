using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using PcControl.Core.Capture;

namespace PcControl.Tests.Integration;

public class ViewTests
{
    private static async Task<ClientWebSocket> ConnectedAsync(TestServer server)
    {
        await server.PairAsync();
        var socket = await server.ConnectAsync(server.Origin, server.CookieValue());
        await TestServer.ReceiveJsonAsync(socket); // hello
        return socket;
    }

    /// <summary>Reads messages until a binary picture arrives; text messages seen on the way are collected.</summary>
    private static async Task<byte[]> NextPictureAsync(WebSocket socket, List<JsonElement>? texts = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[256 * 1024];
        while (true)
        {
            var length = 0;
            ValueWebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer.AsMemory(length), timeout.Token);
                length += result.Count;
            }
            while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                return buffer[..length];
            }

            texts?.Add(JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(buffer, 0, length)));
        }
    }

    private static Task StartAsync(WebSocket socket, string json) => TestServer.SendTextAsync(socket, json);

    private static async Task WaitUntilAsync(Func<bool> condition, string what, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"Timed out waiting for {what}.");
    }

    [Fact]
    public async Task Magnifier_StreamsPicturesAroundTheCursor()
    {
        await using var server = await TestServer.StartAsync();
        server.Screen.CursorX = 500;
        server.Screen.CursorY = 300;
        using var socket = await ConnectedAsync(server);

        await StartAsync(socket, """{"type":"view","stream":"magnifier","enabled":true,"width":200,"height":120,"fps":20}""");
        var frame = await NextPictureAsync(socket);

        Assert.True(FrameCodec.TryDecodeHeader(frame, out var channel, out _, out var x, out var y));
        Assert.Equal(ViewChannel.Magnifier, channel);
        Assert.Equal((100, 60), (x, y));
        Assert.StartsWith("JPEG 200x120", Encoding.ASCII.GetString(frame, FrameCodec.HeaderLength, frame.Length - FrameCodec.HeaderLength), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Display_StreamsTheWholeMonitor_AndFollowsAChangingScreen()
    {
        await using var server = await TestServer.StartAsync();
        using var socket = await ConnectedAsync(server);

        await StartAsync(socket, """{"type":"view","stream":"display","enabled":true,"width":640,"fps":10}""");
        var first = await NextPictureAsync(socket);
        server.Screen.PixelValue = 42;
        var second = await NextPictureAsync(socket);

        Assert.True(FrameCodec.TryDecodeHeader(first, out var channel, out _, out _, out _));
        Assert.Equal(ViewChannel.Display, channel);
        Assert.StartsWith("JPEG 640x360", Encoding.ASCII.GetString(first, FrameCodec.HeaderLength, first.Length - FrameCodec.HeaderLength), StringComparison.Ordinal);
        Assert.NotNull(second);
    }

    [Fact]
    public async Task BothStreams_CanRunAtTheSameTime()
    {
        await using var server = await TestServer.StartAsync();
        using var socket = await ConnectedAsync(server);

        await StartAsync(socket, """{"type":"view","stream":"magnifier","enabled":true,"fps":20}""");
        await StartAsync(socket, """{"type":"view","stream":"display","enabled":true,"fps":10}""");

        var seen = new HashSet<ViewChannel>();
        for (var i = 0; i < 20 && seen.Count < 2; i++)
        {
            var frame = await NextPictureAsync(socket);
            Assert.True(FrameCodec.TryDecodeHeader(frame, out var channel, out _, out _, out _));
            seen.Add(channel);
            server.Screen.PixelValue++;
        }

        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public async Task TurningAStreamOff_StopsCapturing_AndTheSocketStaysOpen()
    {
        await using var server = await TestServer.StartAsync();
        using var socket = await ConnectedAsync(server);
        await StartAsync(socket, """{"type":"view","stream":"magnifier","enabled":true,"fps":20}""");
        await NextPictureAsync(socket);

        await StartAsync(socket, """{"type":"view","stream":"magnifier","enabled":false}""");
        await Task.Delay(300);
        var captures = server.Screen.Captures.Count;
        await Task.Delay(300);

        Assert.Equal(captures, server.Screen.Captures.Count);
        await TestServer.SendTextAsync(socket, """{"type":"ping"}""");
        // Pictures already in flight may still arrive before the pong.
        for (var i = 0; i < 50; i++)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var buffer = new byte[256 * 1024];
            var result = await socket.ReceiveAsync(buffer, timeout.Token);
            if (result.MessageType == WebSocketMessageType.Text && Encoding.UTF8.GetString(buffer, 0, result.Count).Contains("pong", StringComparison.Ordinal))
            {
                return;
            }
        }

        Assert.Fail("never got the pong");
    }

    [Fact]
    public async Task AskingAgain_ReplacesTheStream_WithTheNewSettings()
    {
        await using var server = await TestServer.StartAsync();
        using var socket = await ConnectedAsync(server);
        await StartAsync(socket, """{"type":"view","stream":"magnifier","enabled":true,"width":200,"height":120,"fps":20}""");
        await NextPictureAsync(socket);

        await StartAsync(socket, """{"type":"view","stream":"magnifier","enabled":true,"width":400,"height":240,"fps":20}""");

        await WaitUntilAsync(() => server.Screen.Captures.Any(c => c.Width == 400 && c.Height == 240), "a capture at the new size");
    }

    [Fact]
    public async Task ClosingTheSocket_StopsTheStreams()
    {
        await using var server = await TestServer.StartAsync();
        var socket = await ConnectedAsync(server);
        await StartAsync(socket, """{"type":"view","stream":"display","enabled":true,"fps":10}""");
        await NextPictureAsync(socket);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        socket.Dispose();
        await Task.Delay(500);
        var captures = server.Screen.Captures.Count;
        await Task.Delay(500);

        Assert.Equal(captures, server.Screen.Captures.Count);
    }

    [Fact]
    public async Task KillSwitch_StopsPictures_AndTellsThePhone_ThenResumes()
    {
        await using var server = await TestServer.StartAsync();
        using var socket = await ConnectedAsync(server);
        var texts = new List<JsonElement>();
        await StartAsync(socket, """{"type":"view","stream":"magnifier","enabled":true,"fps":20}""");
        await NextPictureAsync(socket, texts);

        server.State.Paused = true;
        await Task.Delay(800);
        var captures = server.Screen.Captures.Count;
        await Task.Delay(400);
        Assert.Equal(captures, server.Screen.Captures.Count);

        server.State.Paused = false;
        server.Screen.PixelValue = 77;
        await NextPictureAsync(socket, texts);

        var states = texts.Where(t => t.GetProperty("type").GetString() == "viewStatus").Select(t => t.GetProperty("state").GetString()).ToList();
        Assert.Contains("paused", states);
        Assert.Contains("running", states);
    }

    [Fact]
    public async Task ALockedScreen_IsReported_AndNoPicturesAreCaptured()
    {
        await using var server = await TestServer.StartAsync();
        server.Screen.DesktopAvailable = false;
        using var socket = await ConnectedAsync(server);

        await StartAsync(socket, """{"type":"view","stream":"display","enabled":true,"fps":10}""");
        var status = await TestServer.ReceiveJsonAsync(socket);

        Assert.Equal("viewStatus", status.GetProperty("type").GetString());
        Assert.Equal("locked", status.GetProperty("state").GetString());
        Assert.Equal("display", status.GetProperty("stream").GetString());
        Assert.Empty(server.Screen.Captures);
    }

    [Fact]
    public async Task WhenScreenshotsAreSwitchedOff_ViewRequestsAreRefused()
    {
        await using var server = await TestServer.StartAsync(screenshotsEnabled: false);
        using var socket = await ConnectedAsync(server);

        await StartAsync(socket, """{"type":"view","stream":"display","enabled":true}""");
        var error = await TestServer.ReceiveJsonAsync(socket);

        Assert.Equal("feature_disabled", error.GetProperty("code").GetString());
        Assert.Empty(server.Screen.Captures);
    }

    [Fact]
    public async Task MalformedViewRequests_GetAnError()
    {
        await using var server = await TestServer.StartAsync();
        using var socket = await ConnectedAsync(server);

        await StartAsync(socket, """{"type":"view","stream":"audio","enabled":true}""");

        Assert.Equal("invalid_message", (await TestServer.ReceiveJsonAsync(socket)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Capabilities_ReportThePreviewFeatures_AndHonourTheToggle()
    {
        await using var on = await TestServer.StartAsync();
        await on.PairAsync();
        var capsOn = await on.Http.GetFromJsonAsync<Dictionary<string, bool>>("/api/capabilities");

        await using var off = await TestServer.StartAsync(screenshotsEnabled: false);
        await off.PairAsync();
        var capsOff = await off.Http.GetFromJsonAsync<Dictionary<string, bool>>("/api/capabilities");

        Assert.True(capsOn!["magnifier"]);
        Assert.True(capsOn["screenshot"]);
        Assert.False(capsOff!["magnifier"]);
        Assert.False(capsOff["screenshot"]);
    }

    // ---- REST ----

    [Fact]
    public async Task Monitors_ListsEachScreen()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var monitors = await server.Http.GetFromJsonAsync<JsonElement>("/api/monitors");

        Assert.Equal(2, monitors.GetArrayLength());
        Assert.Equal("Left", monitors[0].GetProperty("name").GetString());
        Assert.True(monitors[0].GetProperty("primary").GetBoolean());
        Assert.Equal(2560, monitors[1].GetProperty("width").GetInt32());
        Assert.Equal(1920, monitors[1].GetProperty("x").GetInt32());
    }

    [Fact]
    public async Task Screenshot_ReturnsAJpegOfTheMonitorUnderTheCursor_WithMarkerHeaders()
    {
        await using var server = await TestServer.StartAsync();
        server.Screen.CursorX = 960;
        server.Screen.CursorY = 540;
        await server.PairAsync();

        var response = await server.Http.GetAsync("/api/screenshot?width=640");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("320", response.Headers.GetValues("X-Cursor-X").Single());
        Assert.Equal("180", response.Headers.GetValues("X-Cursor-Y").Single());
        Assert.StartsWith("JPEG 640x360", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Screenshot_CanPickAMonitor()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var response = await server.Http.GetAsync("/api/screenshot?monitor=1&width=1280");

        // Monitor 1 is 2560x1440, so 1280 wide is 720 tall.
        Assert.StartsWith("JPEG 1280x720", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(new Rect(1920, 0, 2560, 1440), server.Screen.Captures.Last().Source);
    }

    [Fact]
    public async Task Screenshot_ClampsAnAbsurdWidth()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        await server.Http.GetAsync("/api/screenshot?monitor=1&width=999999");

        // 2560x1440 limited to 900 tall keeps its shape: 1600x900.
        var capture = server.Screen.Captures.Last();
        Assert.Equal((1600, ViewOptions.MaxDisplayHeight), (capture.Width, capture.Height));
        Assert.True(capture.Width <= ViewOptions.MaxDisplayWidth);
    }

    [Fact]
    public async Task Screenshot_OfALockedScreen_Is503_NotABlackPicture()
    {
        await using var server = await TestServer.StartAsync();
        server.Screen.DesktopAvailable = false;
        await server.PairAsync();

        var response = await server.Http.GetAsync("/api/screenshot");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("screen_unavailable", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/screenshot")]
    [InlineData("/api/monitors")]
    public async Task ScreenEndpoints_AreRefused_WhenSwitchedOff(string path)
    {
        await using var server = await TestServer.StartAsync(screenshotsEnabled: false);
        await server.PairAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await server.Http.GetAsync(path)).StatusCode);
        Assert.Empty(server.Screen.Captures);
    }

    [Theory]
    [InlineData("/api/screenshot")]
    [InlineData("/api/monitors")]
    public async Task ScreenEndpoints_RequireAToken(string path)
    {
        await using var server = await TestServer.StartAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await server.Http.GetAsync(path)).StatusCode);
        Assert.Empty(server.Screen.Captures);
    }
}
