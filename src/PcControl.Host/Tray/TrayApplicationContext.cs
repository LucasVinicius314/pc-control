using System.Diagnostics;
using PcControl.Core.Config;
using PcControl.Core.Pairing;
using PcControl.Host.Properties;
using PcControl.Host.Web;
using Serilog;

namespace PcControl.Host.Tray;

/// <summary>The tray icon and its menu. Owns the lifetime of the WinForms message loop.</summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AppConfig _config;
    private readonly PinService _pins;
    private readonly DeviceRegistry _registry;
    private readonly RemoteControlState _state;
    private readonly DeviceConnections _connections;
    private readonly Control _ui = new();
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _pauseItem = new("Pause remote control") { CheckOnClick = true };
    private readonly ToolStripMenuItem _startupItem = new("Start with Windows");
    private readonly PcControl.Windows.StartupService _startup = new();
    private MainForm? _window;

    public TrayApplicationContext(
        AppConfig config,
        PinService pins,
        DeviceRegistry registry,
        RemoteControlState state,
        DeviceConnections connections,
        Couch.CouchController? couch = null)
    {
        _config = config;
        _pins = pins;
        _registry = registry;
        _state = state;
        _connections = connections;
        // Forces the handle to be created now, on the UI thread, so RunOnUi can marshal back to it.
        _ = _ui.Handle;

        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Open PC Control", null, (_, _) => ShowWindow(showPairing: false));
        var controllerUi = new ToolStripMenuItem("Open controller UI", null, (_, _) => couch?.Toggle()) { Visible = couch is not null };
        var pair = new ToolStripMenuItem("Pair a phone…", null, (_, _) => ShowWindow(showPairing: true));
        var logs = new ToolStripMenuItem("Open logs folder", null, (_, _) => OpenFolder(AppPaths.LogsDirectory));
        var quit = new ToolStripMenuItem("Quit", null, (_, _) => ExitThread());

        _pauseItem.CheckedChanged += (_, _) => _state.Paused = _pauseItem.Checked;

        open.Font = new Font(open.Font, FontStyle.Bold);
        // Windows' own Startup settings can change this, so read it fresh each time the menu opens.
        menu.Opening += (_, _) => _startupItem.Checked = _startup.IsEnabled;
        _startupItem.Click += (_, _) => ToggleStartup();
        menu.Items.AddRange([open, controllerUi, pair, new ToolStripSeparator(), _pauseItem, _startupItem, new ToolStripSeparator(), logs, new ToolStripSeparator(), quit]);

        _icon = new NotifyIcon
        {
            Icon = AppIcon.Value,
            Text = "PC Control",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => ShowWindow(showPairing: false);

        _registry.DevicePaired += OnDevicePaired;
        _state.Changed += OnStateChanged;
        _connections.Changed += OnConnectionsChanged;

        UpdateTooltip();

        if (_registry.List().Count == 0)
        {
            // First run: nothing is paired yet, so go straight to the PIN.
            RunOnUi(() => ShowWindow(showPairing: true));
        }
    }

    private void ShowWindow(bool showPairing)
    {
        if (_window is not { IsDisposed: false })
        {
            _window = new MainForm(_pins, _registry, _connections, LanAddresses.Get(), _config.Port);
            _window.FormClosed += (_, _) => _window = null;
            _window.Show();
        }

        if (_window.WindowState == FormWindowState.Minimized)
        {
            _window.WindowState = FormWindowState.Normal;
        }

        if (showPairing)
        {
            _window.ShowPairing(issueNew: true);
        }

        _window.Activate();
    }

    private void OnDevicePaired(object? sender, PairedDevice device) =>
        RunOnUi(() =>
        {
            _icon.ShowBalloonTip(4000, "PC Control", $"{device.Name} is now paired.", ToolTipIcon.Info);
            _window?.HidePairing();
        });

    private void OnStateChanged(object? sender, EventArgs e) =>
        RunOnUi(() =>
        {
            Log.Information("Remote control {State} from the tray", _state.Paused ? "paused" : "resumed");
            if (_pauseItem.Checked != _state.Paused)
            {
                _pauseItem.Checked = _state.Paused;
            }

            UpdateTooltip();
        });

    private void OnConnectionsChanged(object? sender, EventArgs e) => RunOnUi(UpdateTooltip);

    /// <summary>Runs <paramref name="action"/> on the UI thread; safe to call from any thread.</summary>
    private void RunOnUi(Action action)
    {
        try
        {
            if (!_ui.IsDisposed)
            {
                _ui.BeginInvoke(action);
            }
        }
        catch (InvalidOperationException)
        {
            // The tray is shutting down.
        }
    }

    private void UpdateTooltip()
    {
        var phones = _connections.DeviceCount;
        var text = phones switch
        {
            0 => "PC Control",
            1 => "PC Control - 1 phone connected",
            _ => $"PC Control - {phones} phones connected",
        };

        // The tray tooltip is limited to 63 characters.
        _icon.Text = _state.Paused ? text + " (paused)" : text;
    }

    private void ToggleStartup()
    {
        var enable = !_startup.IsEnabled;
        try
        {
            _startup.SetEnabled(enable);
            Log.Information("Start with Windows {State}", enable ? "turned on" : "turned off");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Warning(ex, "Could not change Start with Windows");
            MessageBox.Show($"Windows would not let PC Control change its startup setting:{Environment.NewLine}{ex.Message}", "PC Control", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _registry.DevicePaired -= OnDevicePaired;
            _state.Changed -= OnStateChanged;
            _connections.Changed -= OnConnectionsChanged;
            _window?.Dispose();
            _icon.Visible = false;
            _icon.Dispose();
            _ui.Dispose();
        }

        base.Dispose(disposing);
    }
}
