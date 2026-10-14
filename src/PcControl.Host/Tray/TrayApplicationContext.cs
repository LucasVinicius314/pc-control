using System.Diagnostics;
using PcControl.Core.Config;
using PcControl.Core.Pairing;
using PcControl.Host.Properties;
using Serilog;

namespace PcControl.Host.Tray;

/// <summary>The tray icon and its menu. Owns the lifetime of the WinForms message loop.</summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AppConfig _config;
    private readonly PinService _pins;
    private readonly DeviceRegistry _registry;
    private readonly RemoteControlState _state;
    private readonly Control _ui = new();
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _devicesMenu = new("Paired devices");
    private readonly ToolStripMenuItem _pauseItem = new("Pause remote control") { CheckOnClick = true };
    private PairingForm? _pairingForm;

    public TrayApplicationContext(
        AppConfig config,
        PinService pins,
        DeviceRegistry registry,
        RemoteControlState state)
    {
        _config = config;
        _pins = pins;
        _registry = registry;
        _state = state;
        // Forces the handle to be created now, on the UI thread, so RunOnUi can marshal back to it.
        _ = _ui.Handle;

        var menu = new ContextMenuStrip();
        var showPin = new ToolStripMenuItem("Show pairing PIN…", null, (_, _) => ShowPairing());
        var logs = new ToolStripMenuItem("Open logs folder", null, (_, _) => OpenFolder(AppPaths.LogsDirectory));
        var quit = new ToolStripMenuItem("Quit", null, (_, _) => ExitThread());

        _devicesMenu.DropDownOpening += (_, _) => RebuildDevicesMenu();
        _devicesMenu.DropDownItems.Add(new ToolStripMenuItem("(loading)") { Enabled = false });
        _pauseItem.CheckedChanged += (_, _) => _state.Paused = _pauseItem.Checked;

        menu.Items.AddRange([showPin, _devicesMenu, _pauseItem, new ToolStripSeparator(), logs, new ToolStripSeparator(), quit]);

        _icon = new NotifyIcon
        {
            Icon = AppIcon.Value,
            Text = "PC Control",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => ShowPairing();

        _registry.DevicePaired += OnDevicePaired;
        _state.Changed += OnStateChanged;

        UpdateTooltip();

        if (_registry.List().Count == 0)
        {
            // First run: nothing is paired yet, so go straight to the PIN.
            RunOnUi(ShowPairing);
        }
    }

    private void ShowPairing()
    {
        if (_pairingForm is { IsDisposed: false })
        {
            _pairingForm.Activate();
            return;
        }

        _pairingForm = new PairingForm(_pins, LanAddresses.Get(), _config.Port);
        _pairingForm.FormClosed += (_, _) => _pairingForm = null;
        _pairingForm.Show();
        _pairingForm.Activate();
    }

    private void RebuildDevicesMenu()
    {
        _devicesMenu.DropDownItems.Clear();

        var devices = _registry.List();
        if (devices.Count == 0)
        {
            _devicesMenu.DropDownItems.Add(new ToolStripMenuItem("(none paired)") { Enabled = false });
            return;
        }

        foreach (var device in devices)
        {
            var item = new ToolStripMenuItem($"Remove “{device.Name}” (paired {device.PairedAt.LocalDateTime:g})");
            item.Click += (_, _) => ConfirmRevoke(device);
            _devicesMenu.DropDownItems.Add(item);
        }
    }

    private void ConfirmRevoke(PairedDevice device)
    {
        var answer = MessageBox.Show(
            $"Remove “{device.Name}”? It will stop working immediately and must pair again with a new PIN.",
            "PC Control",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (answer == DialogResult.Yes && _registry.Revoke(device.Id))
        {
            Log.Information("Device {DeviceName} ({DeviceId}) removed from the tray", device.Name, device.Id);
        }
    }

    private void OnDevicePaired(object? sender, PairedDevice device) =>
        RunOnUi(() =>
        {
            _icon.ShowBalloonTip(4000, "PC Control", $"{device.Name} is now paired.", ToolTipIcon.Info);
            _pairingForm?.Close();
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

    private void UpdateTooltip() =>
        _icon.Text = _state.Paused ? "PC Control (paused)" : "PC Control";

    private static void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _registry.DevicePaired -= OnDevicePaired;
            _state.Changed -= OnStateChanged;
            _pairingForm?.Dispose();
            _icon.Visible = false;
            _icon.Dispose();
            _ui.Dispose();
        }

        base.Dispose(disposing);
    }
}
