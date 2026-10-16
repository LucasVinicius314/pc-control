using PcControl.Core.Capture;
using PcControl.Core.Config;
using PcControl.Host.Web;
using PcControl.Windows;
using Serilog;

namespace PcControl.Host.Couch;

/// <summary>
/// Opens and closes the controller UI. Safe to call from any thread: the work is moved to the UI
/// thread. The window exists only while it is open, so it uses no memory the rest of the time.
/// </summary>
internal sealed class CouchController : IDisposable
{
    private readonly AppConfig _config;
    private readonly LocalPairing _local;
    private readonly IScreenCapturer _screen;
    private readonly IJpegEncoder _encoder;
    private readonly CouchBackdrop _backdrop;
    private readonly Control _ui = new();
    private CouchWindow? _window;

    public CouchController(AppConfig config, LocalPairing local, IScreenCapturer screen, IJpegEncoder encoder, CouchBackdrop backdrop)
    {
        _config = config;
        _local = local;
        _screen = screen;
        _encoder = encoder;
        _backdrop = backdrop;
        _ = _ui.Handle; // created now, on the UI thread, so later calls can be marshalled to it
    }

    public bool IsOpen => _window is not null;

    public void Toggle() => RunOnUi(() =>
    {
        if (_window is null)
        {
            ShowNow();
        }
        else
        {
            HideNow();
        }
    });

    public void Show() => RunOnUi(() =>
    {
        if (_window is null)
        {
            ShowNow();
        }
    });

    public void Hide() => RunOnUi(HideNow);

    private void RunOnUi(Action action)
    {
        if (_ui.IsDisposed)
        {
            return;
        }

        if (_ui.InvokeRequired)
        {
            _ui.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    private void ShowNow()
    {
        TakeBackdrop();

        // How much of the picture shows through travels in the address; only this window gets it.
        var seeThrough = _config.Couch.SeeThrough.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        var address = $"http://127.0.0.1:{_config.Port}/couch.html#local={_local.Key}&see={seeThrough}&sound={(_config.Couch.Sounds ? 1 : 0)}";
        var window = new CouchWindow(address, Path.Combine(AppPaths.Root, "webview2"));
        window.HideRequested += (_, _) => RunOnUi(HideNow);
        _window = window;
        window.Show();
        // A game in front would otherwise keep the focus; this is what lets the UI take it.
        ForegroundWindow.BringToFront(window.Handle);
        Log.Information("Controller UI opened");
    }

    /// <summary>A small picture of the main screen as it is now (a game, say), for the UI to show behind itself.</summary>
    private void TakeBackdrop()
    {
        _backdrop.Jpeg = null;
        if (_config.Couch.SeeThrough <= 0)
        {
            return;
        }

        try
        {
            var monitor = _screen.GetMonitors().FirstOrDefault(m => m.IsPrimary);
            if (monitor is null || !_screen.IsDesktopAvailable())
            {
                Log.Warning("No picture of the screen for the controller UI backdrop: the screen cannot be read right now");
                return;
            }

            var width = Math.Min(1280, monitor.Bounds.Width);
            var height = Math.Max(1, (int)Math.Round((double)monitor.Bounds.Height * width / monitor.Bounds.Width));
            var frame = _screen.Capture(monitor.Bounds, width, height);
            if (frame is null)
            {
                Log.Warning("No picture of the screen for the controller UI backdrop: the capture gave nothing");
                return;
            }

            _backdrop.Jpeg = _encoder.Encode(frame, 70);
            Log.Information("Controller UI backdrop picture taken ({Kilobytes} KB)", (_backdrop.Jpeg?.Length ?? 0) / 1024);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            Log.Warning(ex, "Could not take a picture of the screen for the controller UI backdrop");
        }
    }

    private void HideNow()
    {
        var window = _window;
        if (window is null)
        {
            return;
        }

        _window = null;
        _backdrop.Jpeg = null;
        window.Close();
        window.Dispose();
        Log.Information("Controller UI closed");
    }

    public void Dispose()
    {
        HideNow();
        _ui.Dispose();
    }
}
