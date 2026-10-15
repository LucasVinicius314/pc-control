using PcControl.Core.Apps;
using PcControl.Core.Capture;
using PcControl.Core.Config;
using PcControl.Core.Display;
using PcControl.Core.Input;
using PcControl.Core.Pairing;

namespace PcControl.Host.Web;

/// <summary>Everything the web server needs from the rest of the app, in one place.</summary>
internal sealed record ServerDependencies(
    AppConfig Config,
    PinService Pins,
    PairingRateLimiter Limiter,
    DeviceRegistry Registry,
    RemoteControlState State,
    DeviceConnections Connections,
    IInputInjector Injector,
    IScreenCapturer Screen,
    IJpegEncoder Encoder,
    DpiToggleService Dpi,
    AppsService Apps,
    PowerToysShortcutResolver PowerToys);
