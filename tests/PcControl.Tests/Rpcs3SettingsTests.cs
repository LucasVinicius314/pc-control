using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PcControl.Core.Games;
using PcControl.Host;
using PcControl.Windows;

namespace PcControl.Tests;

public sealed class Rpcs3SettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pc-control-rpcs3set-" + Guid.NewGuid().ToString("N"));
    private readonly string _config;
    private readonly string _rpcs3;
    private readonly Rpcs3Library _library;
    private readonly GamesService _games;
    private readonly EmulatorSettings _settings;

    public Rpcs3SettingsTests()
    {
        Directory.CreateDirectory(_root);
        _config = Path.Combine(_root, "config.json");

        // A fake RPCS3 with one game.
        _rpcs3 = Path.Combine(_root, "emu");
        Directory.CreateDirectory(Path.Combine(_rpcs3, "config"));
        Directory.CreateDirectory(Path.Combine(_rpcs3, "games", "G1", "PS3_GAME"));
        File.WriteAllBytes(Path.Combine(_rpcs3, "rpcs3.exe"), []);
        File.WriteAllBytes(Path.Combine(_rpcs3, "games", "G1", "PS3_GAME", "PARAM.SFO"), SfoBuilder.Build(("TITLE", "Game One")));
        File.WriteAllText(Path.Combine(_rpcs3, "config", "games.yml"), "BLUS1: " + Path.Combine(_rpcs3, "games", "G1").Replace('\\', '/') + "/\n");

        // Automatic detection looks nowhere (the test must not find the real RPCS3 on this PC).
        _library = new Rpcs3Library("auto", fullscreen: true, start: null, extraCandidates: () => []);
        _games = new GamesService(_library, new MemoryGamesStore(), new FakeTimeProvider(), TimeSpan.FromHours(1));
        _settings = new EmulatorSettings(_library, _games, _config, "rpcs3Path");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void WhenNothingIsFound_TheStatusSaysSo()
    {
        var status = _settings.GetStatus();

        Assert.Null(status.ExePath);
        Assert.True(status.Automatic);
        Assert.Equal(0, status.GameCount);
        Assert.NotNull(status.Problem);
    }

    [Fact]
    public void ChoosingTheExe_FindsIt_ListsItsGames_AndSavesTheChoice()
    {
        var problem = _settings.Apply(Path.Combine(_rpcs3, "rpcs3.exe"));

        Assert.Null(problem);
        var status = _settings.GetStatus();
        Assert.Equal(Path.Combine(_rpcs3, "rpcs3.exe"), status.ExePath);
        Assert.False(status.Automatic);
        Assert.Equal(1, status.GameCount);
        Assert.Equal(Path.Combine(_rpcs3, "rpcs3.exe"), SavedSetting());
    }

    [Fact]
    public void AFolder_IsAcceptedToo()
    {
        Assert.Null(_settings.Apply(_rpcs3));

        Assert.Equal(Path.Combine(_rpcs3, "rpcs3.exe"), _settings.GetStatus().ExePath);
    }

    [Theory]
    [InlineData(@"C:\definitely\not\here\rpcs3.exe")]
    [InlineData(@"C:\Windows")]
    public void APathWithoutRpcs3_IsRefused_AndNothingChanges(string path)
    {
        File.WriteAllText(_config, "{ \"port\": 48123 }");

        var problem = _settings.Apply(path);

        Assert.NotNull(problem);
        Assert.Equal("auto", _library.Setting);
        Assert.Equal("{ \"port\": 48123 }", File.ReadAllText(_config)); // the file was not touched
    }

    [Fact]
    public void TooLongAPath_IsRefused()
    {
        Assert.NotNull(_settings.Apply(new string('a', 300)));
    }

    [Fact]
    public void TheOtherSettings_AreKept_WhenTheChoiceIsSaved()
    {
        File.WriteAllText(_config, "{ \"port\": 50000, // my port\n \"couch\": { \"sounds\": false }, \"games\": { \"rpcs3Fullscreen\": false } }");

        _settings.Apply(Path.Combine(_rpcs3, "rpcs3.exe"));

        using var saved = JsonDocument.Parse(File.ReadAllText(_config));
        Assert.Equal(50000, saved.RootElement.GetProperty("port").GetInt32());
        Assert.False(saved.RootElement.GetProperty("couch").GetProperty("sounds").GetBoolean());
        Assert.False(saved.RootElement.GetProperty("games").GetProperty("rpcs3Fullscreen").GetBoolean());
        Assert.Equal(Path.Combine(_rpcs3, "rpcs3.exe"), saved.RootElement.GetProperty("games").GetProperty("rpcs3Path").GetString());
    }

    [Fact]
    public void ASavedFile_StillLoadsAsAValidConfig()
    {
        _settings.Apply(Path.Combine(_rpcs3, "rpcs3.exe"));

        var config = ConfigLoader.LoadOrCreate(_config);

        Assert.Equal(Path.Combine(_rpcs3, "rpcs3.exe"), config.Games.Rpcs3Path);
    }

    [Fact]
    public void WithoutAConfigFile_OneIsCreated()
    {
        Assert.False(File.Exists(_config));

        _settings.Apply(_rpcs3);

        Assert.True(File.Exists(_config));
    }

    [Fact]
    public void GoingBackToAutomatic_IsSaved_AndForgetsTheChoice()
    {
        _settings.Apply(_rpcs3);

        Assert.Null(_settings.Apply(null));

        Assert.Equal("auto", SavedSetting());
        Assert.True(_settings.GetStatus().Automatic);
        Assert.Null(_settings.GetStatus().ExePath); // nothing to find automatically in this test
    }

    [Fact]
    public void LookingAgain_FindsRpcs3ThatAppearedLater()
    {
        // The setting points at a folder that has no RPCS3 yet.
        var later = Path.Combine(_root, "later");
        Directory.CreateDirectory(later);
        _library.Configure(later);
        Assert.Null(_settings.GetStatus().ExePath);

        File.WriteAllBytes(Path.Combine(later, "rpcs3.exe"), []);

        Assert.Equal(Path.Combine(later, "rpcs3.exe"), _settings.Rescan().ExePath);
    }

    private string? SavedSetting()
    {
        using var saved = JsonDocument.Parse(File.ReadAllText(_config));
        return saved.RootElement.GetProperty("games").GetProperty("rpcs3Path").GetString();
    }
}

public class GamesServiceInvalidateTests
{
    [Fact]
    public void Invalidate_MakesTheNextListReadAgain()
    {
        var library = new FakeGameLibrary();
        var service = new GamesService(library, new MemoryGamesStore(), new FakeTimeProvider(), TimeSpan.FromHours(1));
        Assert.Equal(3, service.GetList().Games.Count);

        library.Games = [GameEntry.Create("Only one", "S1", @"C:\g\1")];
        Assert.Equal(3, service.GetList().Games.Count); // still the cached list

        service.Invalidate();

        Assert.Single(service.GetList().Games);
    }
}
