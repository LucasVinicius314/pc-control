using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using PcControl.Core.Apps;
using PcControl.Core.Games;
using PcControl.Windows;

namespace PcControl.Tests;

public sealed class FakeGameLibrary : IGameLibrary
{
    public string Source { get; set; } = GameSources.Rpcs3;

    public bool Available { get; set; } = true;

    public string? UnavailableReason { get; set; }

    public List<GameEntry> Games { get; set; } =
    [
        GameEntry.Create("Sonic Unleashed", "BLUS30244", @"C:\games\Sonic"),
        GameEntry.Create("Bleach", "BLUS30769", @"C:\games\Bleach"),
        GameEntry.Create("Édition spéciale", "BLES00001", @"C:\games\Special"),
    ];

    public bool LaunchSucceeds { get; set; } = true;

    /// <summary>When set, the library reports that games cannot start, with this sentence.</summary>
    public string? Problem { get; set; }

    public string? LaunchProblem(GameEntry game) => Problem;

    public List<string> Launched { get; } = [];

    public byte[]? Image { get; set; } = [0x89, 0x50, 0x4E, 0x47];

    public IReadOnlyList<GameEntry> GetGames() => Games;

    public byte[]? GetImage(GameEntry game, GameImage image) => Image;

    public bool Launch(GameEntry game)
    {
        Launched.Add(game.LaunchPath);
        return LaunchSucceeds;
    }
}

public sealed class MemoryGamesStore : IGamesStateStore
{
    public List<string> Recent { get; set; } = [];

    public IReadOnlyList<string> LoadRecent() => Recent;

    public void SaveRecent(IReadOnlyList<string> launchPaths) => Recent = [.. launchPaths];
}

/// <summary>Builds a PARAM.SFO the way the PS3 does, so the parser is tested against the real layout.</summary>
internal static class SfoBuilder
{
    public static byte[] Build(params (string Key, string Value)[] strings)
    {
        var keys = new MemoryStream();
        var data = new MemoryStream();
        var entries = new MemoryStream();
        foreach (var (key, value) in strings)
        {
            var keyOffset = (ushort)keys.Length;
            keys.Write(Encoding.UTF8.GetBytes(key));
            keys.WriteByte(0);

            var bytes = Encoding.UTF8.GetBytes(value);
            var dataOffset = (uint)data.Length;
            data.Write(bytes);
            data.WriteByte(0);
            var max = (uint)bytes.Length + 1;

            entries.Write(BitConverter.GetBytes(keyOffset));
            entries.Write(BitConverter.GetBytes((ushort)0x0204));
            entries.Write(BitConverter.GetBytes(max));
            entries.Write(BitConverter.GetBytes(max));
            entries.Write(BitConverter.GetBytes(dataOffset));
        }

        var keyTable = 20 + (uint)entries.Length;
        var dataTable = keyTable + (uint)keys.Length;
        var file = new MemoryStream();
        file.Write([0, (byte)'P', (byte)'S', (byte)'F']);
        file.Write(BitConverter.GetBytes(0x101u));
        file.Write(BitConverter.GetBytes(keyTable));
        file.Write(BitConverter.GetBytes(dataTable));
        file.Write(BitConverter.GetBytes((uint)strings.Length));
        file.Write(entries.ToArray());
        file.Write(keys.ToArray());
        file.Write(data.ToArray());
        return file.ToArray();
    }
}

public class SfoTests
{
    [Fact]
    public void TheTitleAndSerial_AreRead()
    {
        var file = SfoBuilder.Build(("TITLE", "Sonic Unleashed"), ("TITLE_ID", "BLUS30244"), ("CATEGORY", "DG"));

        var values = Sfo.Parse(file);

        Assert.Equal("Sonic Unleashed", values["TITLE"]);
        Assert.Equal("BLUS30244", values["TITLE_ID"]);
    }

    [Fact]
    public void NonAscii_TitlesSurvive()
    {
        var values = Sfo.Parse(SfoBuilder.Build(("TITLE", "ブリーチ Édition ★")));

        Assert.Equal("ブリーチ Édition ★", values["TITLE"]);
    }

    [Fact]
    public void Junk_GivesAnEmptyResult_NotAnError()
    {
        Assert.Empty(Sfo.Parse([]));
        Assert.Empty(Sfo.Parse(new byte[100]));
        Assert.Empty(Sfo.Parse(Encoding.ASCII.GetBytes("this is not an sfo file at all, really")));
    }

    [Fact]
    public void ATruncatedFile_GivesWhatItCan_AndNeverThrows()
    {
        var file = SfoBuilder.Build(("TITLE", "Sonic Unleashed"), ("TITLE_ID", "BLUS30244"));

        for (var length = 0; length < file.Length; length++)
        {
            _ = Sfo.Parse(file.AsSpan(0, length));
        }
    }

    [Fact]
    public void AHugeEntryCount_IsRefused()
    {
        var file = SfoBuilder.Build(("TITLE", "x"));
        BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(file, 16);

        Assert.Empty(Sfo.Parse(file));
    }
}

public class Rpcs3GamesFileTests
{
    [Fact]
    public void EachLine_GivesASerialAndAFolder()
    {
        var text = "BLUS30244: C:/rpcs3/games//Sonic Unleashed (USA)/\r\nBLUS30769: C:/rpcs3/games//Bleach/\r\n";

        var games = Rpcs3GamesFile.Parse(text);

        Assert.Equal([("BLUS30244", "C:/rpcs3/games//Sonic Unleashed (USA)/"), ("BLUS30769", "C:/rpcs3/games//Bleach/")], games);
    }

    [Fact]
    public void QuotesComments_AndBlankLines_AreHandled()
    {
        var games = Rpcs3GamesFile.Parse("# comment\n\n'BLES1': \"D:/a b/\"\nBLES2: $(EmulatorDir)dev_hdd0/game/BLES2/\n");

        Assert.Equal([("BLES1", "D:/a b/"), ("BLES2", "$(EmulatorDir)dev_hdd0/game/BLES2/")], games);
    }

    [Theory]
    [InlineData("no colon here")]
    [InlineData(": missing serial")]
    [InlineData("BAD SERIAL: x")]
    [InlineData("SERIAL: ")]
    public void BadLines_AreSkipped(string line)
    {
        Assert.Empty(Rpcs3GamesFile.Parse(line));
    }

    [Fact]
    public void NullText_IsEmpty()
    {
        Assert.Empty(Rpcs3GamesFile.Parse(null!));
    }
}

public class GamesServiceTests
{
    private readonly FakeGameLibrary _library = new();
    private readonly MemoryGamesStore _store = new();
    private readonly FakeTimeProvider _time = new();

    private GamesService Create() => new(_library, _store, _time, TimeSpan.Zero);

    [Fact]
    public void TheList_IsAlphabetical_AccentsFolded_WithIdsOnly()
    {
        var list = Create().GetList();

        Assert.Equal(["Bleach", "Édition spéciale", "Sonic Unleashed"], list.Games.Select(g => g.Title));
        Assert.All(list.Games, g => Assert.True(AppId.LooksValid(g.Id)));
    }

    [Fact]
    public void Launching_StartsTheGame_AndRemembersIt_NewestFirst()
    {
        var service = Create();
        var list = service.GetList().Games;
        var bleach = list.Single(g => g.Title == "Bleach");
        var sonic = list.Single(g => g.Title == "Sonic Unleashed");

        Assert.Equal(LaunchOutcome.Ok, service.Launch(bleach.Id).Outcome);
        Assert.Equal(LaunchOutcome.Ok, service.Launch(sonic.Id).Outcome);

        Assert.Equal([sonic.Id, bleach.Id], service.GetList().Recent);
        Assert.Equal([@"C:\games\Bleach", @"C:\games\Sonic"], _library.Launched);
        Assert.Equal([@"C:\games\Sonic", @"C:\games\Bleach"], _store.Recent);
    }

    [Fact]
    public void ReplayingAGame_MovesItToTheFront_WithoutDuplicates()
    {
        var service = Create();
        var list = service.GetList().Games;
        service.Launch(list[0].Id);
        service.Launch(list[1].Id);
        service.Launch(list[0].Id);

        Assert.Equal([list[0].Id, list[1].Id], service.GetList().Recent);
    }

    [Fact]
    public void AGameThatFailsToStart_IsNotRemembered()
    {
        _library.LaunchSucceeds = false;
        var service = Create();
        var id = service.GetList().Games[0].Id;

        Assert.Equal(LaunchOutcome.Failed, service.Launch(id).Outcome);
        Assert.Empty(service.GetList().Recent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0000000000000000")]
    [InlineData(@"C:\games\Sonic")]
    [InlineData("../../windows")]
    public void OnlyListedIds_CanBeLaunched(string? id)
    {
        var service = Create();
        service.GetList();

        Assert.Equal(LaunchOutcome.UnknownApp, service.Launch(id).Outcome);
        Assert.Empty(_library.Launched);
    }

    [Fact]
    public void RecentGamesThatDisappeared_AreLeftOut()
    {
        _store.Recent = [@"C:\games\Gone", @"C:\games\Bleach"];
        var service = Create();

        var recent = service.GetList().Recent;

        Assert.Single(recent);
    }

    [Fact]
    public void TheRecentList_IsLimited()
    {
        _library.Games = [.. Enumerable.Range(0, 30).Select(i => GameEntry.Create($"Game {i:00}", $"S{i}", $@"C:\g\{i}"))];
        var service = Create();

        foreach (var game in service.GetList().Games)
        {
            service.Launch(game.Id);
        }

        Assert.Equal(GamesService.MaxRecent, service.GetList().Recent.Count);
    }

    [Fact]
    public void Images_AreOnlyGivenForListedGames()
    {
        var service = Create();
        var id = service.GetList().Games[0].Id;

        Assert.NotNull(service.GetImage(id, GameImage.Cover));
        Assert.Null(service.GetImage("0000000000000000", GameImage.Cover));
    }
}

public sealed class Rpcs3LibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pc-control-rpcs3-" + Guid.NewGuid().ToString("N"));

    public Rpcs3LibraryTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "config"));
        File.WriteAllBytes(Path.Combine(_root, "rpcs3.exe"), []);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string AddDisc(string folder, string serial, string? title, bool withBackground = false)
    {
        var game = Path.Combine(_root, "games", folder);
        Directory.CreateDirectory(Path.Combine(game, "PS3_GAME"));
        if (title is not null)
        {
            File.WriteAllBytes(Path.Combine(game, "PS3_GAME", "PARAM.SFO"), SfoBuilder.Build(("TITLE", title), ("TITLE_ID", serial)));
        }

        File.WriteAllBytes(Path.Combine(game, "PS3_GAME", "ICON0.PNG"), [1, 2, 3]);
        if (withBackground)
        {
            File.WriteAllBytes(Path.Combine(game, "PS3_GAME", "PIC1.PNG"), [4, 5, 6]);
        }

        var yml = Path.Combine(_root, "config", "games.yml");
        File.AppendAllText(yml, $"{serial}: {game.Replace('\\', '/')}/\n");
        return game;
    }

    private Rpcs3Library Create(string setting = "auto", Func<ProcessStartInfo, bool>? start = null) =>
        new(setting == "auto" ? _root : setting, fullscreen: true, start, () => []);

    [Fact]
    public void TheGames_ComeFromGamesYml_WithTheirSfoTitles()
    {
        AddDisc("a-folder", "BLUS30244", "Sonic Unleashed");
        AddDisc("Bleach", "BLUS30769", null);

        var games = Create().GetGames();

        Assert.Equal(["Sonic Unleashed", "Bleach"], games.Select(g => g.Title)); // no SFO: the folder name
        Assert.Equal(["BLUS30244", "BLUS30769"], games.Select(g => g.Serial));
    }

    [Fact]
    public void AGameWhoseFolderIsGone_IsSkipped()
    {
        AddDisc("kept", "BLUS1", "Kept");
        File.AppendAllText(Path.Combine(_root, "config", "games.yml"), "BLUS2: " + Path.Combine(_root, "games", "missing").Replace('\\', '/') + "/\n");

        Assert.Equal(["Kept"], Create().GetGames().Select(g => g.Title));
    }

    [Fact]
    public void Images_AreTheCoverAndTheBackground()
    {
        AddDisc("g", "BLUS1", "G", withBackground: true);
        var library = Create();
        var game = library.GetGames().Single();

        Assert.Equal([1, 2, 3], library.GetImage(game, GameImage.Cover));
        Assert.Equal([4, 5, 6], library.GetImage(game, GameImage.Background));
    }

    [Fact]
    public void AMissingBackground_IsNull()
    {
        AddDisc("g", "BLUS1", "G");
        var library = Create();

        Assert.Null(library.GetImage(library.GetGames().Single(), GameImage.Background));
    }

    [Fact]
    public void Launching_RunsRpcs3WithoutItsWindow_FullscreenOnTheGameFolder()
    {
        var folder = AddDisc("g", "BLUS1", "G");
        ProcessStartInfo? seen = null;
        var library = Create(start: info => { seen = info; return true; });

        var ok = library.Launch(library.GetGames().Single());

        Assert.True(ok);
        Assert.Equal(Path.Combine(_root, "rpcs3.exe"), seen!.FileName);
        Assert.Equal(["--no-gui", "--fullscreen", folder], seen.ArgumentList);
        Assert.Equal(_root, seen.WorkingDirectory);
        Assert.False(seen.UseShellExecute);
    }

    [Fact]
    public void ARefusedStart_IsReported()
    {
        AddDisc("g", "BLUS1", "G");
        var library = Create(start: _ => false);

        Assert.False(library.Launch(library.GetGames().Single()));
    }

    [Fact]
    public void ThePathSetting_CanNameTheFolderOrTheExe()
    {
        AddDisc("g", "BLUS1", "G");

        Assert.True(Create(_root).Available);
        Assert.True(Create(Path.Combine(_root, "rpcs3.exe")).Available);
    }

    [Fact]
    public void WithoutRpcs3_NothingIsAvailable_AndTheReasonSaysWhere()
    {
        var library = new Rpcs3Library(Path.Combine(_root, "nowhere"), fullscreen: true, null, () => []);

        Assert.False(library.Available);
        Assert.Empty(library.GetGames());
        Assert.Contains("nowhere", library.UnavailableReason, StringComparison.Ordinal);
    }
}

/// <summary>Reads the real RPCS3 on this PC. Nothing is started. Off by default: <c>$env:PCCONTROL_REAL_INPUT = "1"</c>.</summary>
public class RealRpcs3Tests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void TheRealGameList_HasTitlesAndCovers()
    {
        if (Environment.GetEnvironmentVariable("PCCONTROL_REAL_INPUT") != "1")
        {
            return;
        }

        var library = new Rpcs3Library("auto", fullscreen: true);
        if (!library.Available)
        {
            output.WriteLine("RPCS3 not found: " + library.UnavailableReason);
            return;
        }

        output.WriteLine("rpcs3: " + library.FindExe());
        foreach (var game in library.GetGames())
        {
            output.WriteLine($"{game.Serial}  {game.Title}  cover {library.GetImage(game, GameImage.Cover)?.Length ?? 0} B, background {library.GetImage(game, GameImage.Background)?.Length ?? 0} B");
        }

        Assert.NotEmpty(library.GetGames());
    }
}
