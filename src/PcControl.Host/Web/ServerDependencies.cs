using PcControl.Core.Actions;
using PcControl.Core.Apps;
using PcControl.Core.Capture;
using PcControl.Core.Config;
using PcControl.Core.Display;
using PcControl.Core.Games;
using PcControl.Core.Input;
using PcControl.Core.Pairing;
using PcControl.Core.Performance;
using PcControl.Core.Theme;

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
    PowerToysShortcutResolver PowerToys,
    IUrlOpener UrlOpener,
    PowerService Power,
    ProcessService Processes,
    IThemeProvider Theme,
    GamesService Games,
    LocalPairing Local,
    CouchBackdrop Backdrop,
    PerformanceService Performance,
    ControllerStatus Controllers);
