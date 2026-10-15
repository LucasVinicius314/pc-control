using PcControl.Core.Apps;
using PcControl.Core.Config;
using PcControl.Core.Input;
using PcControl.Host;

namespace PcControl.Tests;

public sealed class JsonAppsStateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pccontrol-apps-" + Guid.NewGuid().ToString("N"));

    public JsonAppsStateStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string FilePath => Path.Combine(_dir, "apps.json");

    [Fact]
    public void AMissingFile_MeansNothingYet()
    {
        Assert.Equal(AppsState.Empty, new JsonAppsStateStore(FilePath).Load());
    }

    [Fact]
    public void WhatIsSaved_IsLoadedBack()
    {
        var store = new JsonAppsStateStore(FilePath);

        store.Save(new AppsState(["a", "b"], ["c", "a"]));
        var loaded = new JsonAppsStateStore(FilePath).Load();

        Assert.Equal(["a", "b"], loaded.Favorites);
        Assert.Equal(["c", "a"], loaded.Recent);
    }

    [Fact]
    public void Saving_LeavesNoTemporaryFileBehind()
    {
        new JsonAppsStateStore(FilePath).Save(new AppsState(["a"], []));

        Assert.Equal([FilePath], Directory.GetFiles(_dir));
    }

    [Fact]
    public void AnUnreadableFile_StartsEmpty_AndIsKeptForInspection()
    {
        File.WriteAllText(FilePath, "{ this is not json");

        var loaded = new JsonAppsStateStore(FilePath).Load();

        Assert.Equal(AppsState.Empty, loaded);
        Assert.True(File.Exists(FilePath + ".corrupt"));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void BlankAndDuplicateEntries_AreCleanedOnLoad()
    {
        File.WriteAllText(FilePath, """{"favorites":["a","a"," ",""],"recent":["b","b"]}""");

        var loaded = new JsonAppsStateStore(FilePath).Load();

        Assert.Equal(["a"], loaded.Favorites);
        Assert.Equal(["b"], loaded.Recent);
    }

    [Fact]
    public void AFileWithMissingParts_LoadsWhatItHas()
    {
        File.WriteAllText(FilePath, """{"favorites":["a"]}""");

        var loaded = new JsonAppsStateStore(FilePath).Load();

        Assert.Equal(["a"], loaded.Favorites);
        Assert.Empty(loaded.Recent);
    }

    [Fact]
    public void ItWorksEndToEnd_WithTheService()
    {
        var catalog = new FakeCatalog { Apps = [AppEntry.Create("Notepad", "notepad")] };
        var first = new AppsService(catalog, new JsonAppsStateStore(FilePath), TimeProvider.System);
        first.SetFavorite(AppId.From("notepad"), true);
        first.Launch(AppId.From("notepad"));

        var second = new AppsService(catalog, new JsonAppsStateStore(FilePath), TimeProvider.System);
        var list = second.GetList();

        Assert.True(list.Apps.Single().Favorite);
        Assert.Equal([AppId.From("notepad")], list.Recent);
    }
}

public class PowerToysShortcutResolverTests
{
    private const string WinShiftSpace = """{"properties":{"open_powerlauncher":{"win":true,"ctrl":false,"alt":false,"shift":true,"code":32}}}""";

    private static PowerToysShortcutResolver Resolver(string shortcut, string? settings) =>
        new(new LauncherConfig { PowerToysRunShortcut = shortcut }, () => settings);

    [Fact]
    public void Auto_FollowsPowerToys()
    {
        var resolved = Resolver("auto", WinShiftSpace).Resolve();

        Assert.Equal("Shift+Win+Space", resolved.Shortcut.ToString());
        Assert.True(resolved.FromPowerToys);
    }

    [Fact]
    public void Auto_FallsBackToAltSpace_WhenPowerToysIsNotInstalled()
    {
        var resolved = Resolver("auto", null).Resolve();

        Assert.Equal("Alt+Space", resolved.Shortcut.ToString());
        Assert.False(resolved.FromPowerToys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("{}")]
    public void Auto_FallsBackToAltSpace_WhenTheSettingsCannotBeUnderstood(string settings)
    {
        Assert.Equal("Alt+Space", Resolver("auto", settings).Resolve().Shortcut.ToString());
    }

    [Fact]
    public void AnExplicitShortcut_WinsOverPowerToys()
    {
        var resolved = Resolver("Ctrl+Alt+K", WinShiftSpace).Resolve();

        Assert.Equal("Ctrl+Alt+K", resolved.Shortcut.ToString());
        Assert.False(resolved.FromPowerToys);
    }

    [Fact]
    public void Auto_IsNotCaseSensitive()
    {
        Assert.True(Resolver("AUTO", WinShiftSpace).Resolve().FromPowerToys);
    }

    [Fact]
    public void ASettingsFileThatCannotBeRead_IsTreatedAsMissing()
    {
        var resolver = new PowerToysShortcutResolver(new LauncherConfig(), () => throw new IOException("locked"));

        Assert.Equal("Alt+Space", resolver.Resolve().Shortcut.ToString());
    }

    [Fact]
    public void EachCall_ReadsTheSettingsAgain()
    {
        var current = WinShiftSpace;
        var resolver = new PowerToysShortcutResolver(new LauncherConfig(), () => current);

        Assert.Equal("Shift+Win+Space", resolver.Resolve().Shortcut.ToString());
        current = """{"properties":{"open_powerlauncher":{"win":false,"ctrl":false,"alt":true,"shift":false,"code":32}}}""";

        Assert.Equal("Alt+Space", resolver.Resolve().Shortcut.ToString());
    }

    [Fact]
    public void TheFallback_IsReallyTheSameAsTheDocumentedDefault()
    {
        Assert.True(Shortcut.TryParse(PowerToysSettings.DefaultShortcut, out var parsed));
        Assert.Equal(parsed!.ToString(), Resolver("auto", null).Resolve().Shortcut.ToString());
    }
}
