using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PcControl.Core.Theme;
using PcControl.Tests.Integration;
using PcControl.Windows;

namespace PcControl.Tests;

public sealed class FakeTheme : IThemeProvider
{
    public ThemeInfo Current { get; set; } = new("#0078d4", true);

    public ThemeInfo Read() => Current;
}

public class ThemeInfoTests
{
    [Theory]
    [InlineData(0, 120, 212, "#0078d4")]
    [InlineData(255, 255, 255, "#ffffff")]
    [InlineData(0, 0, 0, "#000000")]
    [InlineData(1, 2, 3, "#010203")]
    public void Colours_AreFormattedAsLowerCaseHex(int r, int g, int b, string expected)
    {
        Assert.Equal(expected, ThemeInfo.FormatHex((byte)r, (byte)g, (byte)b));
    }

    [Fact]
    public void TheDefault_IsTheDarkBlueThePageAlwaysHad()
    {
        Assert.Equal("#5aa9ff", ThemeInfo.Default.Accent);
        Assert.True(ThemeInfo.Default.Dark);
    }
}

public class ThemeApiTests
{
    [Fact]
    public async Task TheTheme_NeedsPairing()
    {
        await using var server = await TestServer.StartAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await server.Http.GetAsync("/api/theme")).StatusCode);
    }

    [Fact]
    public async Task TheTheme_IsReadFreshEachTime()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var first = await server.Http.GetFromJsonAsync<JsonElement>("/api/theme");
        server.Theme.Current = new ThemeInfo("#e81123", false);
        var second = await server.Http.GetFromJsonAsync<JsonElement>("/api/theme");

        Assert.Equal("#0078d4", first.GetProperty("accent").GetString());
        Assert.True(first.GetProperty("dark").GetBoolean());
        Assert.Equal("#e81123", second.GetProperty("accent").GetString());
        Assert.False(second.GetProperty("dark").GetBoolean());
    }
}

/// <summary>Reads the real Windows settings. Off by default: <c>$env:PCCONTROL_REAL_INPUT = "1"</c>.</summary>
public class WindowsThemeProviderTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void TheRealTheme_IsAValidAccentAndMode()
    {
        if (Environment.GetEnvironmentVariable("PCCONTROL_REAL_INPUT") != "1")
        {
            return;
        }

        var theme = new WindowsThemeProvider().Read();

        output.WriteLine($"accent {theme.Accent}, dark apps: {theme.Dark}");
        Assert.Matches("^#[0-9a-f]{6}$", theme.Accent);
    }
}
