using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using PcControl.Core.Apps;
using PcControl.Windows;
using Xunit.Abstractions;

namespace PcControl.Tests.Windows;

/// <summary>
/// Reads the real Start menu. It only reads (nothing is launched), but it needs your Windows session,
/// so it is off by default: <c>$env:PCCONTROL_REAL_INPUT = "1"; dotnet test --filter ShellAppCatalogTests</c>.
/// </summary>
public class ShellAppCatalogTests(ITestOutputHelper output)
{
    private static bool Enabled => Environment.GetEnvironmentVariable("PCCONTROL_REAL_INPUT") == "1";

    private static ShellAppCatalog Catalog(string cache) => new(cache, NullLogger<ShellAppCatalog>.Instance);

    private static string TempCache() => Path.Combine(Path.GetTempPath(), "pccontrol-icon-test-" + Guid.NewGuid().ToString("N"));

    private static List<(string Name, string Id)> GetStartApps()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-StartApps | ForEach-Object { $_.Name + [char]9 + $_.AppID }\"",
            RedirectStandardOutput = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        var text = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\t', 2))
            .Where(parts => parts.Length == 2)
            .Select(parts => (parts[0], parts[1]))];
    }

    [Fact]
    public void OurStartMenu_MatchesWhatPowerShellsGetStartAppsReports()
    {
        if (!Enabled)
        {
            return;
        }

        var ours = Catalog(TempCache()).GetApps();
        var theirs = GetStartApps();
        output.WriteLine($"ours: {ours.Count} entries, Get-StartApps: {theirs.Count} entries");

        Assert.NotEmpty(ours);
        Assert.NotEmpty(theirs);

        var ourPairs = ours.Select(a => (a.Name, a.LaunchId)).ToHashSet();
        var missing = theirs.Where(t => !ourPairs.Contains(t)).ToList();
        var extra = ours.Where(a => !theirs.Contains((a.Name, a.LaunchId))).ToList();
        foreach (var m in missing.Take(10))
        {
            output.WriteLine($"in Get-StartApps but not ours: {m.Name} | {m.Id}");
        }

        foreach (var e in extra.Take(10))
        {
            output.WriteLine($"ours but not in Get-StartApps: {e.Name} | {e.LaunchId}");
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryRealApp_GetsAUniqueId()
    {
        if (!Enabled)
        {
            return;
        }

        var apps = Catalog(TempCache()).GetApps();

        Assert.Equal(apps.Select(a => a.LaunchId).Distinct().Count(), apps.Select(a => a.Id).Distinct().Count());
        Assert.All(apps, a => Assert.True(AppId.LooksValid(a.Id)));
    }

    [Fact]
    public void RealIcons_ArePngs_WithTransparency_AndAreCachedOnDisk()
    {
        if (!Enabled)
        {
            return;
        }

        var cache = TempCache();
        try
        {
            var catalog = Catalog(cache);
            var apps = catalog.GetApps().Take(40).ToList();
            var withIcon = 0;
            var transparent = 0;

            foreach (var app in apps)
            {
                var png = catalog.GetIconPng(app, 64);
                if (png is null)
                {
                    continue;
                }

                withIcon++;
                Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);

                using var image = new System.Drawing.Bitmap(new MemoryStream(png));
                Assert.InRange(image.Width, 16, 256);
                if (HasTransparentPixel(image))
                {
                    transparent++;
                }
            }

            output.WriteLine($"{withIcon} of {apps.Count} sampled apps have an icon; {transparent} of those have transparency");
            Assert.True(withIcon >= apps.Count * 0.8, $"only {withIcon} of {apps.Count} apps produced an icon");
            Assert.True(transparent > 0, "no icon kept its transparency");
            Assert.True(Directory.GetFiles(cache, "*.png").Length >= withIcon);

            // A second request is served from the cache and is identical.
            var again = catalog.GetIconPng(apps[0], 64);
            Assert.Equal(catalog.GetIconPng(apps[0], 64), again);
        }
        finally
        {
            if (Directory.Exists(cache))
            {
                Directory.Delete(cache, recursive: true);
            }
        }
    }

    [Fact]
    public void AskingForTheIconOfEveryRealApp_NeverThrows_AndMostHaveOne()
    {
        if (!Enabled)
        {
            return;
        }

        var cache = TempCache();
        try
        {
            var catalog = Catalog(cache);
            var apps = catalog.GetApps();
            var withoutIcon = new List<string>();

            // Broken shortcuts make the shell fail in odd ways (one real case came back as an
            // out-of-memory error). None of that may escape: such an app just has no icon.
            foreach (var app in apps)
            {
                if (catalog.GetIconPng(app, 64) is null)
                {
                    withoutIcon.Add(app.Name);
                }
            }

            output.WriteLine($"{apps.Count - withoutIcon.Count} of {apps.Count} apps have an icon; without: {string.Join(", ", withoutIcon.Take(10))}");
            Assert.True(withoutIcon.Count < apps.Count / 5, $"{withoutIcon.Count} of {apps.Count} apps had no icon");
        }
        finally
        {
            if (Directory.Exists(cache))
            {
                Directory.Delete(cache, recursive: true);
            }
        }
    }

    [Fact]
    public void LaunchIdsOfEveryKind_ResolveInTheShell_WithoutLaunchingAnything()
    {
        if (!Enabled)
        {
            return;
        }

        var apps = Catalog(TempCache()).GetApps();
        var kinds = new Dictionary<string, AppEntry?>
        {
            ["packaged app"] = apps.FirstOrDefault(a => a.LaunchId.Contains('!', StringComparison.Ordinal)),
            ["desktop shortcut or exe"] = apps.FirstOrDefault(a => a.LaunchId.Contains(":\\", StringComparison.Ordinal)),
            ["link"] = apps.FirstOrDefault(a => a.LaunchId.Contains("://", StringComparison.Ordinal)),
            ["generated"] = apps.FirstOrDefault(a => a.LaunchId.StartsWith("Microsoft.AutoGenerated", StringComparison.Ordinal)),
        };

        foreach (var (kind, app) in kinds)
        {
            output.WriteLine($"{kind}: {app?.Name ?? "(none on this PC)"} => {app?.LaunchId}");
        }

        Assert.NotNull(kinds["packaged app"]);
        Assert.NotNull(kinds["desktop shortcut or exe"]);
    }

    private static bool HasTransparentPixel(System.Drawing.Bitmap image)
    {
        for (var y = 0; y < image.Height; y += 2)
        {
            for (var x = 0; x < image.Width; x += 2)
            {
                if (image.GetPixel(x, y).A < 255)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
