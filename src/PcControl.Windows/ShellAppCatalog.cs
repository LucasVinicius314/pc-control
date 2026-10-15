using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PcControl.Core.Apps;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;

namespace PcControl.Windows;

/// <summary>
/// Reads the Start menu from the shell's "Apps" folder (shell:AppsFolder), the same list PowerShell's
/// Get-StartApps shows, and starts apps the way Explorer does. It covers packaged (Store) apps and
/// classic desktop shortcuts alike.
/// </summary>
public sealed class ShellAppCatalog(string iconCacheDirectory, ILogger<ShellAppCatalog> logger) : IAppCatalog
{
    private const string AppsFolder = "shell:AppsFolder";
    private static readonly TimeSpan IconCacheLifetime = TimeSpan.FromHours(12);

    // The phone asks for a screenful of icons at once. The shell can fail ("out of memory") when a
    // dozen extractions run at the same time, so they take turns; each one takes only a moment.
    private static readonly SemaphoreSlim IconGate = new(1, 1);

    public IReadOnlyList<AppEntry> GetApps() => OnStaThread(ReadApps);

    public byte[]? GetIconPng(AppEntry app, int size)
    {
        ArgumentNullException.ThrowIfNull(app);

        var file = Path.Combine(iconCacheDirectory, $"{app.Id}-{size}.png");
        try
        {
            if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < IconCacheLifetime)
            {
                return File.ReadAllBytes(file);
            }
        }
        catch (IOException)
        {
            // A cache file we cannot read is just a cache miss.
        }

        byte[]? png;
        IconGate.Wait();
        try
        {
            png = OnStaThread(() => RenderIcon(app, size));
        }
        catch (InvalidOperationException ex)
        {
            // The shell call itself failed (see RenderIcon); an app without an icon is not an error.
            logger.LogDebug(ex, "No icon for {Name}", app.Name);
            return null;
        }
        finally
        {
            IconGate.Release();
        }

        if (png is not null)
        {
            try
            {
                Directory.CreateDirectory(iconCacheDirectory);
                File.WriteAllBytes(file, png);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Could not cache the icon for {Name}", app.Name);
            }
        }

        return png;
    }

    public bool Launch(AppEntry app)
    {
        ArgumentNullException.ThrowIfNull(app);

        try
        {
            // The launch id comes from the Start menu itself, never from the phone.
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $@"{AppsFolder}\{app.LaunchId}",
                UseShellExecute = true,
            });

            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "Windows could not start {Name}", app.Name);
            return false;
        }
    }

    private static unsafe List<AppEntry> ReadApps()
    {
        var apps = new List<AppEntry>();

        PInvoke.SHCreateItemFromParsingName(AppsFolder, null, out IShellItem folder);
        var bhid = PInvoke.BHID_EnumItems;
        var iid = typeof(IEnumShellItems).GUID;
        folder.BindToHandler(null, &bhid, &iid, out var raw);

        var enumerator = (IEnumShellItems)Marshal.GetObjectForIUnknown((nint)raw);
        try
        {
            var batch = new IShellItem[1];
            while (true)
            {
                uint fetched;
                enumerator.Next(1, batch, &fetched);
                if (fetched == 0 || batch[0] is not { } item)
                {
                    break;
                }

                try
                {
                    var name = DisplayName(item, SIGDN.SIGDN_NORMALDISPLAY);
                    var launchId = DisplayName(item, SIGDN.SIGDN_PARENTRELATIVEPARSING);
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(launchId))
                    {
                        apps.Add(AppEntry.Create(name, launchId));
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
            Marshal.ReleaseComObject(folder);
        }

        return apps;
    }

    private static unsafe string DisplayName(IShellItem item, SIGDN kind)
    {
        item.GetDisplayName(kind, out var text);
        try
        {
            return new string((char*)text.Value);
        }
        finally
        {
            Marshal.FreeCoTaskMem((nint)text.Value);
        }
    }

    private byte[]? RenderIcon(AppEntry app, int size)
    {
        try
        {
            PInvoke.SHCreateItemFromParsingName($@"{AppsFolder}\{app.LaunchId}", null, out IShellItemImageFactory factory);
            try
            {
                factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF.SIIGBF_ICONONLY | SIIGBF.SIIGBF_BIGGERSIZEOK, out var bitmap);
                using (bitmap)
                {
                    return BitmapToPng(new HBITMAP(bitmap.DangerousGetHandle()));
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch (Exception ex)
        {
            // The shell reports a broken shortcut (a missing target, an uninstalled app) in many
            // ways, including HRESULTs that .NET turns into OutOfMemoryException. Whatever went
            // wrong, the app simply has no icon.
            logger.LogDebug(ex, "No icon for {Name}", app.Name);
            return null;
        }
    }

    /// <summary>
    /// Converts the shell's bitmap to a PNG keeping its transparency. Going through
    /// <c>Bitmap.FromHbitmap</c> would lose the alpha channel and give icons a black background.
    /// </summary>
    private static unsafe byte[]? BitmapToPng(HBITMAP bitmap)
    {
        BITMAP info = default;
        if (PInvoke.GetObject(bitmap, sizeof(BITMAP), &info) == 0 || info.bmWidth <= 0 || info.bmHeight <= 0)
        {
            return null;
        }

        var width = info.bmWidth;
        var height = info.bmHeight;
        var pixels = new byte[width * height * 4];

        var header = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
            },
        };

        var screen = PInvoke.GetDC(HWND.Null);
        try
        {
            fixed (byte* p = pixels)
            {
                if (PInvoke.GetDIBits(screen, bitmap, 0, (uint)height, p, &header, DIB_USAGE.DIB_RGB_COLORS) != height)
                {
                    return null;
                }
            }
        }
        finally
        {
            _ = PInvoke.ReleaseDC(HWND.Null, screen);
        }

        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            // The shell hands back pre-multiplied alpha.
            using var image = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, handle.AddrOfPinnedObject());
            using var stream = new MemoryStream();
            image.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>Shell objects want a single-threaded apartment; the web server's threads are not one.</summary>
    private static T OnStaThread<T>(Func<T> work)
    {
        T? result = default;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
            Name = "Shell (STA)",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return failure is null ? result! : throw new InvalidOperationException("The Windows shell call failed.", failure);
    }
}
