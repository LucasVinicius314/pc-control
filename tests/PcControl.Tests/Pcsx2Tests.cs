using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PcControl.Core.Games;
using PcControl.Host;
using PcControl.Host.Web;
using PcControl.Windows;

namespace PcControl.Tests;

/// <summary>Builds a tiny ISO 9660 disc image the way a PS2 disc is laid out, with a SYSTEM.CNF in the root.</summary>
internal static class IsoBuilder
{
    public static byte[] Build(string? systemCnf, bool withBootFile = true)
    {
        const int sector = 2048;
        var image = new byte[24 * sector];

        // Primary volume descriptor at sector 16.
        var volume = 16 * sector;
        image[volume] = 1;
        Encoding.ASCII.GetBytes("CD001").CopyTo(image, volume + 1);
        var root = volume + 156;
        image[root] = 34;
        BitConverter.GetBytes(20u).CopyTo(image, root + 2); // the root directory is at sector 20
        BitConverter.GetBytes((uint)sector).CopyTo(image, root + 10);
        image[root + 25] = 2; // a directory

        // The root directory: ".", "..", then SYSTEM.CNF (sector 21).
        var directory = 20 * sector;
        var position = directory;
        position = WriteRecord(image, position, [0], 20, sector, isDirectory: true);
        position = WriteRecord(image, position, [1], 20, sector, isDirectory: true);
        if (systemCnf is not null && withBootFile)
        {
            var bytes = Encoding.ASCII.GetBytes(systemCnf);
            WriteRecord(image, position, Encoding.ASCII.GetBytes("SYSTEM.CNF;1"), 21, bytes.Length, isDirectory: false);
            bytes.CopyTo(image, 21 * sector);
        }

        return image;
    }

    private static int WriteRecord(byte[] image, int at, byte[] name, uint extent, int length, bool isDirectory)
    {
        var size = 33 + name.Length;
        if (size % 2 == 1)
        {
            size++; // records are padded to an even length
        }

        image[at] = (byte)size;
        BitConverter.GetBytes(extent).CopyTo(image, at + 2);
        BitConverter.GetBytes((uint)length).CopyTo(image, at + 10);
        image[at + 25] = (byte)(isDirectory ? 2 : 0);
        image[at + 32] = (byte)name.Length;
        name.CopyTo(image, at + 33);
        return at + size;
    }
}

public class Ps2Tests
{
    [Theory]
    [InlineData("BOOT2 = cdrom0:\\SLUS_213.00;1\r\nVER = 1.00\r\n", "SLUS-21300")]
    [InlineData("BOOT2=cdrom0:\\SCUS_973.28;1", "SCUS-97328")]
    [InlineData("boot2 = cdrom0:\\SLPM_650.12;1", "SLPM-65012")]
    [InlineData("BOOT = cdrom0:\\SLUS_006.31;1\nVMODE = NTSC", "SLUS-00631")]
    [InlineData("VER = 1.00\nBOOT2 = cdrom0:\\SLES_523.62;1", "SLES-52362")]
    public void TheSerial_IsReadFromSystemCnf(string text, string expected)
    {
        Assert.Equal(expected, Ps2.SerialFromSystemCnf(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("VER = 1.00")]
    [InlineData("BOOT2 = cdrom0:\\NOTASERIAL;1")]
    [InlineData("BOOT2 = ")]
    public void WithoutAUsableBootLine_ThereIsNoSerial(string? text)
    {
        Assert.Null(Ps2.SerialFromSystemCnf(text));
    }

    [Fact]
    public void SystemCnf_IsReadFromADiscImage()
    {
        var image = IsoBuilder.Build("BOOT2 = cdrom0:\\SLUS_213.00;1\r\n");

        using var stream = new MemoryStream(image);
        var text = Ps2.ReadRootFile(stream, "SYSTEM.CNF");

        Assert.Equal("SLUS-21300", Ps2.SerialFromSystemCnf(text));
    }

    [Fact]
    public void ADiscWithoutThatFile_GivesNothing()
    {
        using var stream = new MemoryStream(IsoBuilder.Build("BOOT2 = cdrom0:\\SLUS_213.00;1", withBootFile: false));

        Assert.Null(Ps2.ReadRootFile(stream, "SYSTEM.CNF"));
    }

    [Fact]
    public void SomethingThatIsNotADiscImage_GivesNothing_AndNeverThrows()
    {
        Assert.Null(Ps2.ReadRootFile(new MemoryStream([]), "SYSTEM.CNF"));
        Assert.Null(Ps2.ReadRootFile(new MemoryStream(new byte[100_000]), "SYSTEM.CNF"));
        Assert.Null(Ps2.ReadRootFile(new MemoryStream(Encoding.ASCII.GetBytes(new string('x', 100_000))), "SYSTEM.CNF"));
    }

    [Fact]
    public void ADamagedImage_NeverThrows()
    {
        var good = IsoBuilder.Build("BOOT2 = cdrom0:\\SLUS_213.00;1\r\n");

        for (var cut = 0; cut < good.Length; cut += 997)
        {
            _ = Ps2.ReadRootFile(new MemoryStream(good, 0, cut), "SYSTEM.CNF");
        }

        // A directory that claims to be enormous, and one whose records have no length.
        var huge = (byte[])good.Clone();
        BitConverter.GetBytes(uint.MaxValue).CopyTo(huge, (16 * 2048) + 156 + 10);
        Assert.Null(Ps2.ReadRootFile(new MemoryStream(huge), "SYSTEM.CNF"));
    }

    [Fact]
    public void TheGameIndex_GivesNamesBySerial()
    {
        const string yaml = """
            # PCSX2 game database
            SLUS-21168:
              name: "Castlevania - Curse of Darkness"
              region: "NTSC-U"
              compat: 5
            SLUS-21300:
              name: "DreamWorks Over the Hedge"
              name-en: "Over the Hedge"
              region: "NTSC-U"
            SCUS-97328:
              name: Gran Turismo 4
            notaserial:
              name: "Ignored"
            """;

        var names = Ps2.ParseGameIndex(new StringReader(yaml));

        Assert.Equal("Castlevania - Curse of Darkness", names["SLUS-21168"]);
        Assert.Equal("DreamWorks Over the Hedge", names["SLUS-21300"]);
        Assert.Equal("Gran Turismo 4", names["SCUS-97328"]);
        Assert.Equal("Gran Turismo 4", names["scus-97328"]); // serials match in any case
        Assert.False(names.ContainsKey("notaserial"));
        Assert.Equal(3, names.Count);
    }

    [Fact]
    public void TheGameFolders_AreReadFromTheIni()
    {
        const string ini = "[UI]\r\nRecursivePaths = ignored\r\n[GameList]\r\nRecursivePaths = C:\\Games\\PS2\r\nRecursivePaths = D:\\More\r\nPaths = E:\\Flat\r\n[Other]\r\nPaths = ignored too\r\n";

        var folders = Ps2.ParseGameFolders(ini);

        Assert.Equal([(@"C:\Games\PS2", true), (@"D:\More", true), (@"E:\Flat", false)], folders);
    }

    [Fact]
    public void WithoutGameFolders_TheListIsEmpty()
    {
        Assert.Empty(Ps2.ParseGameFolders("[UI]\nx = 1"));
        Assert.Empty(Ps2.ParseGameFolders(null));
    }

    [Theory]
    [InlineData("Game.iso", true)]
    [InlineData("game.CHD", true)]
    [InlineData("game.bin", true)]
    [InlineData("notes.txt", false)]
    [InlineData("save.ps2", false)]
    [InlineData("noextension", false)]
    public void OnlyDiscImages_AreGames(string name, bool expected)
    {
        Assert.Equal(expected, Ps2.IsDiscImage(name));
    }
}

public class CompositeGameLibraryTests
{
    private static FakeGameLibrary Library(string source, params string[] titles) => new()
    {
        Source = source,
        Games = [.. titles.Select(t => GameEntry.Create(t, "S", $@"C:\{source}\{t}", source))],
    };

    [Fact]
    public void TheGames_OfEveryLibrary_AreListed()
    {
        var all = new CompositeGameLibrary(Library("rpcs3", "A", "B"), Library("pcsx2", "C"));

        Assert.Equal(["A", "B", "C"], all.GetGames().Select(g => g.Title));
    }

    [Fact]
    public void EachGame_GoesBackToItsOwnLibrary_ToStart()
    {
        var ps3 = Library("rpcs3", "A");
        var ps2 = Library("pcsx2", "C");
        var all = new CompositeGameLibrary(ps3, ps2);

        all.Launch(all.GetGames().Single(g => g.Source == "pcsx2"));

        Assert.Equal([@"C:\pcsx2\C"], ps2.Launched);
        Assert.Empty(ps3.Launched);
    }

    [Fact]
    public void Pictures_ComeFromTheOwningLibrary()
    {
        var ps3 = Library("rpcs3", "A");
        ps3.Image = [1];
        var ps2 = Library("pcsx2", "C");
        ps2.Image = null;
        var all = new CompositeGameLibrary(ps3, ps2);

        Assert.Equal([1], all.GetImage(all.GetGames().Single(g => g.Source == "rpcs3"), GameImage.Cover));
        Assert.Null(all.GetImage(all.GetGames().Single(g => g.Source == "pcsx2"), GameImage.Cover));
    }

    [Fact]
    public void ALibraryThatIsNotThere_AddsNoGames_ButTheOtherStillWorks()
    {
        var missing = Library("pcsx2", "X");
        missing.Available = false;
        missing.UnavailableReason = "PCSX2 was not found.";
        var all = new CompositeGameLibrary(Library("rpcs3", "A"), missing);

        Assert.True(all.Available);
        Assert.Null(all.UnavailableReason);
        Assert.Equal(["A"], all.GetGames().Select(g => g.Title));
    }

    [Fact]
    public void WhenNoneIsThere_TheReasonsAreGivenTogether()
    {
        var one = Library("rpcs3");
        one.Available = false;
        one.UnavailableReason = "RPCS3 was not found.";
        var two = Library("pcsx2");
        two.Available = false;
        two.UnavailableReason = "PCSX2 was not found.";
        var all = new CompositeGameLibrary(one, two);

        Assert.False(all.Available);
        Assert.Equal("RPCS3 was not found. PCSX2 was not found.", all.UnavailableReason);
    }

    [Fact]
    public void ASameNamedGame_InTwoEmulators_GetsTwoDifferentIds()
    {
        var a = GameEntry.Create("Same", "S", @"C:\x\same", "rpcs3");
        var b = GameEntry.Create("Same", "S", @"C:\x\same", "pcsx2");

        Assert.NotEqual(a.Id, b.Id);
    }
}

public sealed class Pcsx2LibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pc-control-pcsx2-" + Guid.NewGuid().ToString("N"));
    private readonly string _emu;
    private readonly string _data;
    private readonly string _games;

    public Pcsx2LibraryTests()
    {
        _emu = Path.Combine(_root, "emu");
        _data = Path.Combine(_root, "data");
        _games = Path.Combine(_root, "games");
        Directory.CreateDirectory(Path.Combine(_emu, "resources"));
        Directory.CreateDirectory(Path.Combine(_data, "inis"));
        Directory.CreateDirectory(_games);
        File.WriteAllBytes(Path.Combine(_emu, "pcsx2-qt.exe"), []);
        File.WriteAllText(Path.Combine(_emu, "resources", "GameIndex.yaml"), "SLUS-21300:\n  name: \"DreamWorks Over the Hedge\"\nSLUS-21168:\n  name: \"Castlevania - Curse of Darkness\"\n");
        File.WriteAllText(Path.Combine(_data, "inis", "PCSX2.ini"), $"[GameList]\r\nRecursivePaths = {_games}\r\n");

        // A BIOS in PCSX2's default place, so games can start.
        Directory.CreateDirectory(Path.Combine(_data, "bios"));
        File.WriteAllBytes(Path.Combine(_data, "bios", "scph.bin"), new byte[600 * 1024]);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string AddIso(string folder, string file, string? bootLine)
    {
        var dir = Path.Combine(_games, folder);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, file);
        File.WriteAllBytes(path, IsoBuilder.Build(bootLine));
        return path;
    }

    private Pcsx2Library Create(Func<ProcessStartInfo, bool>? start = null, bool fullscreen = true, Func<string, byte[]?>? fetchCover = null) =>
        new(_emu, fullscreen, start, () => [], _data, fetchCover);

    [Fact]
    public void TheTitle_ComesFromTheDiscsSerial_AndPcsx2sOwnList()
    {
        AddIso("a", "weird file name.iso", "BOOT2 = cdrom0:\\SLUS_213.00;1");

        var game = Create().GetGames().Single();

        Assert.Equal("DreamWorks Over the Hedge", game.Title);
        Assert.Equal("SLUS-21300", game.Serial);
        Assert.Equal("pcsx2", game.Source);
    }

    [Fact]
    public void WithoutASerialInTheList_TheFileNameIsTheTitle()
    {
        AddIso("b", "Some Game (USA).iso", "BOOT2 = cdrom0:\\SLUS_999.99;1");
        AddIso("c", "No Boot File.iso", null);
        File.WriteAllBytes(Path.Combine(_games, "Compressed Game.chd"), [1, 2, 3]);

        var titles = Create().GetGames().Select(g => g.Title).Order().ToList();

        Assert.Equal(["Compressed Game", "No Boot File", "Some Game (USA)"], titles);
    }

    [Fact]
    public void OnlyDiscImages_InTheListedFolders_AreGames()
    {
        AddIso("a", "Game.iso", "BOOT2 = cdrom0:\\SLUS_213.00;1");
        File.WriteAllText(Path.Combine(_games, "a", "readme.txt"), "hi");
        File.WriteAllBytes(Path.Combine(_root, "Elsewhere.iso"), [0]);

        Assert.Single(Create().GetGames());
    }

    [Fact]
    public void AFlatPath_DoesNotLookInsideSubfolders()
    {
        AddIso("deep", "Deep.iso", null);
        File.WriteAllBytes(Path.Combine(_games, "Top.iso"), [0]);
        File.WriteAllText(Path.Combine(_data, "inis", "PCSX2.ini"), $"[GameList]\r\nPaths = {_games}\r\n");

        Assert.Equal(["Top"], Create().GetGames().Select(g => g.Title));
    }

    [Fact]
    public void AFolderThatIsNotThere_IsSkipped_AndNothingIsListedTwice()
    {
        AddIso("a", "Game.iso", null);
        File.WriteAllText(Path.Combine(_data, "inis", "PCSX2.ini"), $"[GameList]\r\nRecursivePaths = {Path.Combine(_root, "gone")}\r\nRecursivePaths = {_games}\r\nRecursivePaths = {_games}\r\n");

        Assert.Single(Create().GetGames());
    }

    [Fact]
    public void WithoutAnIni_ThereAreNoGames()
    {
        File.Delete(Path.Combine(_data, "inis", "PCSX2.ini"));

        Assert.Empty(Create().GetGames());
    }

    [Fact]
    public void ACover_IsFoundByTheSerial_TheTitle_OrNextToTheGame()
    {
        var iso = AddIso("a", "Game.iso", "BOOT2 = cdrom0:\\SLUS_213.00;1");
        var library = Create();
        var game = library.GetGames().Single();
        Assert.Null(library.GetImage(game, GameImage.Cover));

        Directory.CreateDirectory(Path.Combine(_data, "covers"));
        File.WriteAllBytes(Path.Combine(_data, "covers", "SLUS-21300.jpg"), [0xFF, 0xD8, 0xFF, 1]);
        Assert.Equal([0xFF, 0xD8, 0xFF, 1], library.GetImage(game, GameImage.Cover));

        File.Delete(Path.Combine(_data, "covers", "SLUS-21300.jpg"));
        File.WriteAllBytes(Path.Combine(_data, "covers", "DreamWorks Over the Hedge.png"), [2]);
        Assert.Equal([2], library.GetImage(game, GameImage.Cover));

        File.Delete(Path.Combine(_data, "covers", "DreamWorks Over the Hedge.png"));
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(iso)!, "cover.png"), [3]);
        Assert.Equal([3], library.GetImage(game, GameImage.Cover));
    }

    [Fact]
    public void TheBoxArt_AlsoServesAsTheBackground()
    {
        AddIso("a", "Game.iso", "BOOT2 = cdrom0:\\SLUS_213.00;1");
        Directory.CreateDirectory(Path.Combine(_data, "covers"));
        File.WriteAllBytes(Path.Combine(_data, "covers", "SLUS-21300.jpg"), [0xFF, 0xD8, 0xFF, 1]);
        var library = Create();
        var game = library.GetGames().Single();

        Assert.Equal(library.GetImage(game, GameImage.Cover), library.GetImage(game, GameImage.Background));
    }

    [Fact]
    public void AMissingCover_IsDownloadedOnce_AndKeptInPcsx2sCoversFolder()
    {
        AddIso("a", "Game.iso", "BOOT2 = cdrom0:\\SLUS_213.00;1");
        var asked = new List<string>();
        var library = Create(fetchCover: serial => { asked.Add(serial); return [0xFF, 0xD8, 0xFF, 9, 9, 9]; });
        var game = library.GetGames().Single();

        Assert.Equal([0xFF, 0xD8, 0xFF, 9, 9, 9], library.GetImage(game, GameImage.Cover));
        Assert.Equal([0xFF, 0xD8, 0xFF, 9, 9, 9], library.GetImage(game, GameImage.Cover));

        Assert.Equal(["SLUS-21300"], asked);
        Assert.True(File.Exists(Path.Combine(_data, "covers", "SLUS-21300.jpg")));
    }

    [Fact]
    public void ACoverThatCannotBeGot_IsNotAskedForAgain_AndNothingIsSaved()
    {
        AddIso("a", "Game.iso", "BOOT2 = cdrom0:\\SLUS_213.00;1");
        var asked = 0;
        var library = Create(fetchCover: _ => { asked++; return null; });
        var game = library.GetGames().Single();

        Assert.Null(library.GetImage(game, GameImage.Cover));
        Assert.Null(library.GetImage(game, GameImage.Cover));

        Assert.Equal(1, asked);
        Assert.False(Directory.Exists(Path.Combine(_data, "covers")));
    }

    [Fact]
    public void ADownloadThatIsNotAPicture_IsIgnored_AndAGameWithoutASerialIsNotLookedUp()
    {
        AddIso("a", "Game.iso", "BOOT2 = cdrom0:\\SLUS_213.00;1");
        AddIso("b", "Other.iso", null);
        var asked = new List<string>();
        var library = Create(fetchCover: serial => { asked.Add(serial); return "<html>no</html>"u8.ToArray(); });

        foreach (var game in library.GetGames())
        {
            Assert.Null(library.GetImage(game, GameImage.Cover));
        }

        Assert.Equal(["SLUS-21300"], asked);
    }

    [Fact]
    public void AnOfflineMachine_JustMeansNoCover()
    {
        AddIso("a", "Game.iso", "BOOT2 = cdrom0:\\SLUS_213.00;1");
        var library = Create(fetchCover: _ => throw new HttpRequestException("offline"));

        Assert.Null(library.GetImage(library.GetGames().Single(), GameImage.Cover));
    }

    [Fact]
    public void Launching_RunsPcsx2WithoutItsWindow_FullscreenOnTheDiscImage()
    {
        var iso = AddIso("a", "Game.iso", null);
        ProcessStartInfo? seen = null;
        var library = Create(info => { seen = info; return true; });

        var ok = library.Launch(library.GetGames().Single());

        Assert.True(ok);
        Assert.Equal(Path.Combine(_emu, "pcsx2-qt.exe"), seen!.FileName);
        Assert.Equal(["-nogui", "-fullscreen", "--", iso], seen.ArgumentList);
        Assert.False(seen.UseShellExecute);
    }

    [Fact]
    public void FullscreenCanBeTurnedOff()
    {
        AddIso("a", "Game.iso", null);
        ProcessStartInfo? seen = null;
        var library = Create(info => { seen = info; return true; }, fullscreen: false);

        library.Launch(library.GetGames().Single());

        Assert.DoesNotContain("-fullscreen", seen!.ArgumentList);
    }

    [Fact]
    public void AGameWhoseFileIsGone_DoesNotLaunch()
    {
        var iso = AddIso("a", "Game.iso", null);
        var library = Create(_ => true);
        var game = library.GetGames().Single();
        File.Delete(iso);

        Assert.False(library.Launch(game));
    }

    [Fact]
    public void ThePathSetting_CanNameTheFolderOrTheProgram()
    {
        Assert.True(Create().IsLocation(_emu));
        Assert.True(Create().IsLocation(Path.Combine(_emu, "pcsx2-qt.exe")));
        Assert.False(Create().IsLocation(_games));
        Assert.False(Create().IsLocation(Path.Combine(_root, "nowhere")));
    }

    [Fact]
    public void WithoutPcsx2_NothingIsAvailable_AndTheReasonSaysWhere()
    {
        var library = new Pcsx2Library(Path.Combine(_root, "nowhere"), true, null, () => [], _data);

        Assert.False(library.Available);
        Assert.Empty(library.GetGames());
        Assert.Contains("nowhere", library.UnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePortableData_IsNextToTheProgram_WhenPortableIniIsThere()
    {
        File.WriteAllText(Path.Combine(_emu, "portable.ini"), string.Empty);
        var library = new Pcsx2Library(_emu, true, null, () => []); // no data folder override

        Assert.Equal(_emu, library.DataRoot());
    }

    [Fact]
    public void TheListIsNotReReadFromTheDiscs_WhenNothingChanged()
    {
        var iso = AddIso("a", "Game.iso", "BOOT2 = cdrom0:\\SLUS_213.00;1");
        var library = Create();
        Assert.Equal("DreamWorks Over the Hedge", library.GetGames().Single().Title);

        // Locking the file exclusively would make a re-read fail; the remembered title still comes back.
        using var locked = new FileStream(iso, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Equal("DreamWorks Over the Hedge", library.GetGames().Single().Title);
    }
}

public sealed class Pcsx2BiosTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pc-control-bios-" + Guid.NewGuid().ToString("N"));
    private readonly string _data;

    public Pcsx2BiosTests()
    {
        _data = Path.Combine(_root, "Documents", "PCSX2");
        Directory.CreateDirectory(Path.Combine(_root, "emu"));
        Directory.CreateDirectory(Path.Combine(_data, "inis"));
        File.WriteAllBytes(Path.Combine(_root, "emu", "pcsx2-qt.exe"), []);
        var games = Path.Combine(_root, "games");
        Directory.CreateDirectory(games);
        File.WriteAllBytes(Path.Combine(games, "Game.iso"), IsoBuilder.Build(null));
        Ini(string.Empty);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Ini(string extra) =>
        File.WriteAllText(Path.Combine(_data, "inis", "PCSX2.ini"), extra + $"[GameList]\r\nRecursivePaths = {Path.Combine(_root, "games")}\r\n");

    private Pcsx2Library Create(Func<ProcessStartInfo, bool>? start = null) =>
        new(Path.Combine(_root, "emu"), true, start, () => [], _data);

    [Fact]
    public void WithoutAnyBios_TheProblemSaysWhereItLooked()
    {
        Assert.Contains(Path.Combine(_data, "bios"), Create().Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void ABiosFolderThatIsSetButWrong_IsReportedAsMissing_WithTheRealFolder()
    {
        // The same mistake as a path that climbs out one level too far.
        Ini("[Folders]\r\nBios = ..\\..\\..\\Nowhere\\bios\r\n");

        var warning = Create().Warning;

        Assert.Contains("does not exist", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("..", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenABiosIsNextToThePcsx2Program_TheMessagePointsToIt()
    {
        Ini("[Folders]\r\nBios = ..\\..\\..\\Nowhere\\bios\r\n");
        Directory.CreateDirectory(Path.Combine(_root, "emu", "bios"));
        File.WriteAllBytes(Path.Combine(_root, "emu", "bios", "scph.bin"), new byte[1024 * 1024]);

        var warning = Create().Warning;

        Assert.Contains(Path.Combine(_root, "emu", "bios"), warning, StringComparison.Ordinal);
        Assert.Contains("choose that folder", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void ARelativeBiosFolder_IsReadFromThePcsx2DataFolder()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Documents", "elsewhere"));
        File.WriteAllBytes(Path.Combine(_root, "Documents", "elsewhere", "rom.bin"), new byte[1024 * 1024]);
        Ini("[Folders]\r\nBios = ..\\elsewhere\r\n");

        Assert.Null(Create().Warning);
    }

    [Fact]
    public void AnyBiosImageInTheFolder_Counts_LikePcsx2sOwnSearch()
    {
        Directory.CreateDirectory(Path.Combine(_data, "bios"));
        File.WriteAllBytes(Path.Combine(_data, "bios", "some-other.rom0"), new byte[4 * 1024 * 1024]);
        Ini("[Filenames]\r\nBIOS = missing-file.bin\r\n");

        Assert.Null(Create().Warning);
    }

    [Fact]
    public void TinyFiles_AreNotABios()
    {
        Directory.CreateDirectory(Path.Combine(_data, "bios"));
        File.WriteAllText(Path.Combine(_data, "bios", "readme.txt"), "not a bios");

        Assert.Contains("no PS2 BIOS", Create().Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void TheConfiguredFile_BeingThere_IsEnough()
    {
        Directory.CreateDirectory(Path.Combine(_data, "bios"));
        File.WriteAllBytes(Path.Combine(_data, "bios", "scph.bin"), [1]);
        Ini("[Filenames]\r\nBIOS = scph.bin\r\n");

        Assert.Null(Create().Warning);
    }

    [Fact]
    public void WithoutABios_NoGameIsStarted_AndTheLibrarySaysWhy()
    {
        var started = false;
        var library = Create(_ => { started = true; return true; });
        var game = library.GetGames().Single();

        Assert.False(library.Launch(game));
        Assert.False(started);
        Assert.NotNull(library.LaunchProblem(game));
    }

    [Fact]
    public void WithABios_ThereIsNoProblem_AndTheGameStarts()
    {
        Directory.CreateDirectory(Path.Combine(_data, "bios"));
        File.WriteAllBytes(Path.Combine(_data, "bios", "scph.bin"), new byte[600 * 1024]);
        var library = Create(_ => true);
        var game = library.GetGames().Single();

        Assert.Null(library.LaunchProblem(game));
        Assert.True(library.Launch(game));
    }

    [Fact]
    public void NoIniAtAll_IsNotWarnedAbout_Pcsx2sOwnWizardHandlesFirstRun()
    {
        File.Delete(Path.Combine(_data, "inis", "PCSX2.ini"));

        Assert.Null(Create().Warning);
    }

    [Fact]
    public void TheService_RefusesToStart_AndGivesTheReason()
    {
        var library = Create(_ => true);
        var service = new GamesService(library, new MemoryGamesStore(), new FakeTimeProvider(), TimeSpan.FromHours(1));
        var id = service.GetList().Games.Single().Id;

        var result = service.Launch(id);

        Assert.Equal(PcControl.Core.Apps.LaunchOutcome.Failed, result.Outcome);
        Assert.Contains("BIOS", service.ProblemFor(id), StringComparison.Ordinal);
        Assert.Empty(service.GetList().Recent); // a game that did not start is not "recently played"
    }

    [Theory]
    [InlineData("[Folders]\nBios = a\\b\n[Other]\nBios = no", "Folders", "Bios", "a\\b")]
    [InlineData("[Folders]\r\nbios=  spaced  \r\n", "Folders", "Bios", "spaced")]
    [InlineData("[Folders]\nBios =\n", "Folders", "Bios", null)]
    [InlineData("[Other]\nBios = x", "Folders", "Bios", null)]
    [InlineData(null, "Folders", "Bios", null)]
    public void AnIniValue_IsReadFromItsSection(string? ini, string section, string key, string? expected)
    {
        Assert.Equal(expected, Ps2.IniValue(ini, section, key));
    }
}

public sealed class Pcsx2SettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pc-control-pcsx2set-" + Guid.NewGuid().ToString("N"));

    public Pcsx2SettingsTests() => Directory.CreateDirectory(Path.Combine(_root, "emu"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Choosing_Pcsx2_SavesUnderItsOwnKey_AndLeavesRpcs3Alone()
    {
        File.WriteAllBytes(Path.Combine(_root, "emu", "pcsx2-qt.exe"), []);
        var config = Path.Combine(_root, "config.json");
        File.WriteAllText(config, "{ \"games\": { \"rpcs3Path\": \"D:\\\\RPCS3\" } }");
        var library = new Pcsx2Library("auto", true, null, () => [], _root);
        var games = new GamesService(library, new MemoryGamesStore(), new FakeTimeProvider(), TimeSpan.FromHours(1));
        var settings = new EmulatorSettings(library, games, config, "pcsx2Path");

        var problem = settings.Apply(Path.Combine(_root, "emu"));

        Assert.Null(problem);
        using var saved = JsonDocument.Parse(File.ReadAllText(config));
        var section = saved.RootElement.GetProperty("games");
        Assert.Equal(Path.Combine(_root, "emu"), section.GetProperty("pcsx2Path").GetString());
        Assert.Equal(@"D:\RPCS3", section.GetProperty("rpcs3Path").GetString());
    }

    [Fact]
    public void APathWithoutPcsx2_IsRefused_WithItsOwnProgramName()
    {
        var library = new Pcsx2Library("auto", true, null, () => [], _root);
        var games = new GamesService(library, new MemoryGamesStore(), new FakeTimeProvider(), TimeSpan.FromHours(1));
        var settings = new EmulatorSettings(library, games, Path.Combine(_root, "config.json"), "pcsx2Path");

        var problem = settings.Apply(Path.Combine(_root, "nowhere"));

        Assert.Contains("pcsx2-qt.exe", problem, StringComparison.Ordinal);
        Assert.Equal("auto", library.Setting);
    }

    [Fact]
    public void TheGameCount_IsOnlyThisEmulatorsGames()
    {
        File.WriteAllBytes(Path.Combine(_root, "emu", "pcsx2-qt.exe"), []);
        var ps2 = new FakeGameLibrary { Source = "pcsx2", Games = [GameEntry.Create("Two", "S", @"C:\p\two", "pcsx2")] };
        var ps3 = new FakeGameLibrary { Source = "rpcs3", Games = [GameEntry.Create("One", "S", @"C:\r\one"), GameEntry.Create("Uno", "S", @"C:\r\uno")] };
        var library = new Pcsx2Library(Path.Combine(_root, "emu"), true, null, () => [], _root);
        var games = new GamesService(new CompositeGameLibrary(ps3, ps2), new MemoryGamesStore(), new FakeTimeProvider(), TimeSpan.FromHours(1));
        var settings = new EmulatorSettings(library, games, Path.Combine(_root, "config.json"), "pcsx2Path");

        Assert.Equal(1, settings.GetStatus().GameCount);
    }
}

public class ImageTypesTests
{
    [Fact]
    public void ThePictureType_IsToldFromItsFirstBytes()
    {
        Assert.Equal("image/png", ImageTypes.Sniff([0x89, 0x50, 0x4E, 0x47, 0, 0]));
        Assert.Equal("image/jpeg", ImageTypes.Sniff([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Equal("image/webp", ImageTypes.Sniff(Encoding.ASCII.GetBytes("RIFF....WEBPVP8 ")));
        Assert.Equal("image/gif", ImageTypes.Sniff(Encoding.ASCII.GetBytes("GIF89a")));
        Assert.Equal("image/png", ImageTypes.Sniff([])); // unknown: the old default
    }
}

/// <summary>Reads the real PCSX2 on this PC. Nothing is started. Off by default: <c>$env:PCCONTROL_REAL_INPUT = "1"</c>.</summary>
public class RealPcsx2Tests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void TheRealGameList_HasTitlesAndSerials()
    {
        if (Environment.GetEnvironmentVariable("PCCONTROL_REAL_INPUT") != "1")
        {
            return;
        }

        var library = new Pcsx2Library("auto", fullscreen: true);
        if (!library.Available)
        {
            output.WriteLine("PCSX2 not found: " + library.UnavailableReason);
            return;
        }

        output.WriteLine("pcsx2: " + library.FindExe() + "   data: " + library.DataRoot());
        output.WriteLine("warning: " + (library.Warning ?? "none"));
        var timer = Stopwatch.StartNew();
        var games = library.GetGames();
        output.WriteLine($"{games.Count} games in {timer.ElapsedMilliseconds} ms");
        foreach (var game in games)
        {
            output.WriteLine($"{game.Serial,-11} {game.Title}  (cover: {library.GetImage(game, GameImage.Cover)?.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"})");
        }

        timer.Restart();
        _ = library.GetGames();
        output.WriteLine($"second read: {timer.ElapsedMilliseconds} ms");
        Assert.NotEmpty(games);
    }
}
