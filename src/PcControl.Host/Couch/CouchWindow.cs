using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Serilog;

namespace PcControl.Host.Couch;

/// <summary>
/// The fullscreen window that shows the controller UI: a web page from this app's own server, in
/// WebView2. It has no frame, covers the screen, and tells its owner when the page asks to be hidden.
/// </summary>
internal sealed class CouchWindow : Form
{
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(14, 17, 22) };
    private readonly string _address;
    private readonly string _profileFolder;

    public event EventHandler? HideRequested;

    public CouchWindow(string address, string profileFolder)
    {
        _address = address;
        _profileFolder = profileFolder;

        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(14, 17, 22);
        Text = "PC Control";
        Controls.Add(_web);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            // A controller button is not a click, so without this the browser would keep the page silent.
            var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required" };
            var environment = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: _profileFolder, options: options);
            await _web.EnsureCoreWebView2Async(environment);

            var settings = _web.CoreWebView2.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;

            _web.CoreWebView2.WebMessageReceived += OnMessage;
            _web.CoreWebView2.NavigationCompleted += (_, _) => _web.Focus();
            _web.CoreWebView2.Navigate(_address);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Log.Error(ex, "The controller UI could not start its web view");
            HideRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            // The page posts a JSON string, which arrives as a string; accept an object as well.
            var text = e.TryGetWebMessageAsString();
            using var message = JsonDocument.Parse(text);
            if (!message.RootElement.TryGetProperty("type", out var type))
            {
                return;
            }

            switch (type.GetString())
            {
                case "hide":
                    HideRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            // Not a message we know; ignore it.
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _web.Dispose();
        }

        base.Dispose(disposing);
    }
}
