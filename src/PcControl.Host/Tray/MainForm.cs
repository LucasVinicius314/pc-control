using System.Net;
using PcControl.Core.Pairing;
using PcControl.Host.Properties;
using PcControl.Host.Web;
using QRCoder;

namespace PcControl.Host.Tray;

/// <summary>
/// The desktop window: which phones are paired and connected, and a PIN with a QR code for pairing
/// a new one. Closing it leaves the app running in the tray.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly PinService _pins;
    private readonly DeviceRegistry _registry;
    private readonly DeviceConnections _connections;
    private readonly IReadOnlyList<IPAddress> _addresses;
    private readonly int _port;

    private readonly TableLayoutPanel _root = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12) };
    private readonly FlowLayoutPanel _pairingPanel = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Visible = false };
    private readonly PictureBox _qr = new() { SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(220, 220) };
    private readonly Label _pin = new() { AutoSize = true, Font = new Font("Consolas", 32, FontStyle.Bold) };
    private readonly Label _expires = new() { AutoSize = true };
    private readonly Label _address = new() { AutoSize = true };
    private readonly ComboBox _addressPicker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly Label _summary = new() { AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
    private readonly ListView _phones = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        GridLines = false,
    };

    private readonly Button _pairButton = new() { Text = "Pair a new phone", AutoSize = true };
    private readonly Button _removeButton = new() { Text = "Remove selected", AutoSize = true, Enabled = false };
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };

    public MainForm(
        PinService pins,
        DeviceRegistry registry,
        DeviceConnections connections,
        IReadOnlyList<IPAddress> addresses,
        int port)
    {
        _pins = pins;
        _registry = registry;
        _connections = connections;
        _addresses = addresses;
        _port = port;

        Text = "PC Control";
        Icon = AppIcon.Value;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(420, 640);

        BuildPairingPanel();
        BuildPhonesPanel();

        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(_root);

        _pairButton.Click += (_, _) => ShowPairing(issueNew: true);
        _removeButton.Click += (_, _) => RemoveSelected();
        _phones.SelectedIndexChanged += (_, _) => _removeButton.Enabled = _phones.SelectedItems.Count > 0;
        _tick.Tick += (_, _) => OnTick();

        Load += (_, _) =>
        {
            _registry.DevicePaired += OnDevicesChanged;
            _registry.DeviceRevoked += OnDevicesChanged;
            _connections.Changed += OnConnectionsChanged;
            _tick.Start();
            RefreshPhones();
        };
        FormClosed += (_, _) =>
        {
            _registry.DevicePaired -= OnDevicesChanged;
            _registry.DeviceRevoked -= OnDevicesChanged;
            _connections.Changed -= OnConnectionsChanged;
            _tick.Stop();
        };
    }

    /// <summary>Shows the PIN and QR code. When <paramref name="issueNew"/> is false the current PIN is kept.</summary>
    public void ShowPairing(bool issueNew)
    {
        _pairingPanel.Visible = true;
        RefreshPairing(issueNew);
    }

    /// <summary>Hides the PIN and QR code, for example once a phone has paired with it.</summary>
    public void HidePairing() => _pairingPanel.Visible = false;

    private string? SelectedAddress =>
        _addresses.Count == 0 ? null : _addresses[Math.Max(0, _addressPicker.SelectedIndex)].ToString();

    private void BuildPairingPanel()
    {
        var hint = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(380, 0),
            Text = "Scan the code with the phone's camera, or open the address in the phone's browser and type the PIN.",
        };
        var newPin = new Button { Text = "New PIN", AutoSize = true };
        newPin.Click += (_, _) => RefreshPairing(issueNew: true);
        var hide = new Button { Text = "Done", AutoSize = true };
        hide.Click += (_, _) => _pairingPanel.Visible = false;
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.AddRange([newPin, hide]);

        _pairingPanel.Controls.AddRange([hint, _qr, _pin, _expires, _address]);
        if (_addresses.Count > 1)
        {
            foreach (var address in _addresses)
            {
                _addressPicker.Items.Add(address.ToString());
            }

            _addressPicker.SelectedIndex = 0;
            _addressPicker.SelectedIndexChanged += (_, _) => RefreshPairing(issueNew: false);
            _pairingPanel.Controls.Add(_addressPicker);
        }

        _pairingPanel.Controls.Add(buttons);
        _root.Controls.Add(_pairingPanel, 0, 0);
    }

    private void BuildPhonesPanel()
    {
        _phones.Columns.Add("Phone", 170);
        _phones.Columns.Add("Status", 90);
        _phones.Columns.Add("Connected since", 110);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.AddRange([_pairButton, _removeButton]);

        var panel = new Panel { Dock = DockStyle.Fill };
        _summary.Dock = DockStyle.Top;
        _summary.Padding = new Padding(0, 4, 0, 8);
        panel.Controls.Add(_phones);
        panel.Controls.Add(buttons);
        panel.Controls.Add(_summary);
        _root.Controls.Add(panel, 0, 1);
    }

    // Registry and connection events arrive on request threads; the list belongs to the UI thread.
    private void OnDevicesChanged(object? sender, PairedDevice device) => RunOnUi(RefreshPhones);

    private void OnConnectionsChanged(object? sender, EventArgs e) => RunOnUi(RefreshPhones);

    private void RunOnUi(Action action)
    {
        try
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(action);
            }
        }
        catch (InvalidOperationException)
        {
            // The window is closing.
        }
    }

    private void RefreshPhones()
    {
        var devices = _registry.List();
        var live = _connections.Snapshot();
        var selected = _phones.SelectedItems.Count > 0 ? (Guid?)_phones.SelectedItems[0].Tag : null;

        _phones.BeginUpdate();
        _phones.Items.Clear();
        foreach (var device in devices.OrderByDescending(d => live.ContainsKey(d.Id)).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var isLive = live.TryGetValue(device.Id, out var info);
            var status = isLive ? (info!.Count > 1 ? $"Connected ({info.Count})" : "Connected") : "Offline";
            var since = isLive ? info!.Since.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.CurrentCulture) : "—";

            var item = new ListViewItem([device.Name, status, since]) { Tag = device.Id };
            if (!isLive)
            {
                item.ForeColor = SystemColors.GrayText;
            }

            item.Selected = device.Id == selected;
            _phones.Items.Add(item);
        }

        _phones.EndUpdate();

        var connected = live.Count;
        _summary.Text = devices.Count == 0
            ? "No phones paired yet"
            : $"{connected} of {devices.Count} phone{(devices.Count == 1 ? string.Empty : "s")} connected";
        _removeButton.Enabled = _phones.SelectedItems.Count > 0;
    }

    private void RemoveSelected()
    {
        if (_phones.SelectedItems.Count == 0)
        {
            return;
        }

        var id = (Guid)_phones.SelectedItems[0].Tag!;
        var name = _phones.SelectedItems[0].Text;
        var answer = MessageBox.Show(
            this,
            $"Remove “{name}”? It will stop working immediately and must pair again with a new PIN.",
            "PC Control",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (answer == DialogResult.Yes)
        {
            _registry.Revoke(id);
        }
    }

    private void OnTick()
    {
        if (_pairingPanel.Visible)
        {
            UpdateCountdown();
        }
    }

    private void RefreshPairing(bool issueNew)
    {
        var ticket = issueNew ? _pins.Issue() : _pins.Current;
        var address = SelectedAddress;

        _qr.Image?.Dispose();
        _qr.Image = null;

        if (address is null)
        {
            _pin.Text = ticket is null ? "------" : $"{ticket.Pin[..3]} {ticket.Pin[3..]}";
            _address.Text = "No home-network address found. Connect the PC to Wi-Fi or Ethernet.";
        }
        else
        {
            var url = $"http://{address}:{_port}/";
            _address.Text = url;
            _pin.Text = ticket is null ? "------" : $"{ticket.Pin[..3]} {ticket.Pin[3..]}";
            if (ticket is not null)
            {
                _qr.Image = RenderQr(url + "#pin=" + ticket.Pin);
            }
        }

        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        var ticket = _pins.Current;
        if (ticket is null)
        {
            _expires.Text = "PIN expired or used. Press “New PIN”.";
            _pin.Text = "------";
            _qr.Image?.Dispose();
            _qr.Image = null;
            return;
        }

        var left = ticket.ExpiresAt - DateTimeOffset.UtcNow;
        _expires.Text = $"Expires in {Math.Max(0, (int)left.TotalMinutes)}:{Math.Max(0, left.Seconds):D2}";
    }

    private static Bitmap RenderQr(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8);
        using var stream = new MemoryStream(png);
        using var image = new Bitmap(stream);
        return new Bitmap(image);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _qr.Image?.Dispose();
            _tick.Dispose();
        }

        base.Dispose(disposing);
    }
}
