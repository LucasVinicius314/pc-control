using System.Net;
using PcControl.Core.Pairing;
using QRCoder;

namespace PcControl.Host.Tray;

/// <summary>Shows the pairing PIN and a QR code that carries the PC's address and the PIN.</summary>
internal sealed class PairingForm : Form
{
    private readonly PinService _pins;
    private readonly IReadOnlyList<IPAddress> _addresses;
    private readonly int _port;

    private readonly PictureBox _qr = new() { SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(240, 240) };
    private readonly Label _pin = new() { AutoSize = true, Font = new Font("Consolas", 36, FontStyle.Bold) };
    private readonly Label _expires = new() { AutoSize = true };
    private readonly Label _address = new() { AutoSize = true };
    private readonly ComboBox _addressPicker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };

    public PairingForm(PinService pins, IReadOnlyList<IPAddress> addresses, int port)
    {
        _pins = pins;
        _addresses = addresses;
        _port = port;

        Text = "PC Control — pair a phone";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ShowInTaskbar = true;
        Icon = Properties.AppIcon.Value;

        var newPin = new Button { Text = "New PIN", AutoSize = true };
        newPin.Click += (_, _) => Refresh_(issueNew: true);
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
        AcceptButton = close;

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.AddRange([newPin, close]);

        var hint = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(300, 0),
            Text = "Scan the code with your phone's camera, or open the address in the phone's browser and type the PIN.",
        };

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(16),
            WrapContents = false,
        };
        layout.Controls.AddRange([hint, _qr, _pin, _expires, _address]);
        if (addresses.Count > 1)
        {
            foreach (var a in addresses)
            {
                _addressPicker.Items.Add(a.ToString());
            }

            _addressPicker.SelectedIndex = 0;
            _addressPicker.SelectedIndexChanged += (_, _) => Refresh_(issueNew: false);
            layout.Controls.Add(_addressPicker);
        }

        layout.Controls.Add(buttons);
        Controls.Add(layout);
        ClientSize = new Size(340, addresses.Count > 1 ? 590 : 560);

        _tick.Tick += (_, _) => UpdateCountdown();
        Shown += (_, _) => _tick.Start();
        FormClosed += (_, _) => _tick.Dispose();

        Refresh_(issueNew: true);
    }

    private string? SelectedAddress =>
        _addresses.Count == 0 ? null : _addresses[Math.Max(0, _addressPicker.SelectedIndex)].ToString();

    private void Refresh_(bool issueNew)
    {
        var ticket = issueNew ? _pins.Issue() : _pins.Current;
        var address = SelectedAddress;

        _pin.Text = ticket is null ? "------" : $"{ticket.Pin[..3]} {ticket.Pin[3..]}";

        if (address is null)
        {
            _address.Text = "No home-network address found. Connect the PC to Wi-Fi or Ethernet.";
            _qr.Image?.Dispose();
            _qr.Image = null;
        }
        else
        {
            var url = $"http://{address}:{_port}/";
            _address.Text = url;
            _qr.Image?.Dispose();
            _qr.Image = ticket is null ? null : RenderQr(url + "#pin=" + ticket.Pin);
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
