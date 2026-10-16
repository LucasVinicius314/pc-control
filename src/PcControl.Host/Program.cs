using PcControl.Core.Actions;
using PcControl.Core.Apps;
using PcControl.Core.Capture;
using PcControl.Core.Config;
using PcControl.Core.Display;
using PcControl.Core.Input;
using PcControl.Windows;
using Microsoft.Extensions.Logging;
using PcControl.Core.Pairing;
using PcControl.Host.Tray;
using PcControl.Host.Web;
using Serilog;

namespace PcControl.Host;

internal static class Program
{
    private const string AppTitle = "PC Control";

    [STAThread]
    private static int Main()
    {
        // One instance per signed-in user; "Local\" scopes the name to this session.
        // The name includes the data folder, so a second copy on the same settings is refused while a
        // copy pointed at a different folder (PCCONTROL_DATA_DIR, e.g. for testing) can run beside it.
        var folderId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(AppPaths.Root.ToUpperInvariant())))[..12];
        using var mutex = new Mutex(initiallyOwned: true, $@"Local\PcControl.SingleInstance.{folderId}", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("PC Control is already running. Look for its icon in the system tray.", AppTitle);
            return 0;
        }

        ApplicationConfiguration.Initialize();

        AppConfig config;
        try
        {
            AppPaths.EnsureCreated();
            config = ConfigLoader.LoadOrCreate(AppPaths.ConfigFile);
        }
        catch (ConfigException ex)
        {
            MessageBox.Show($"{ex.Message}{Environment.NewLine}{Environment.NewLine}Fix {AppPaths.ConfigFile} and start again.", AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .WriteTo.File(
                Path.Combine(AppPaths.LogsDirectory, "pc-control-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            return Run(config);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "PC Control stopped unexpectedly");
            MessageBox.Show($"PC Control could not start:{Environment.NewLine}{ex.Message}", AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
            mutex.ReleaseMutex();
        }
    }

    private static int Run(AppConfig config)
    {
        Log.Information("PC Control starting on port {Port}", config.Port);

        var time = TimeProvider.System;
        var pairing = config.Pairing;
        var pins = new PinService(time, TimeSpan.FromMinutes(pairing.PinLifetimeMinutes), pairing.MaxFailuresPerPin);
        var limiter = new PairingRateLimiter(
            time,
            pairing.MaxFailedAttempts,
            TimeSpan.FromMinutes(pairing.AttemptWindowMinutes),
            TimeSpan.FromMinutes(pairing.LockoutMinutes));
        var registry = new DeviceRegistry(new JsonDeviceStore(AppPaths.DevicesFile), time);
        var state = new RemoteControlState();
        var connections = new DeviceConnections();

        var injector = new SendInputInjector(new Serilog.Extensions.Logging.SerilogLoggerFactory(Log.Logger).CreateLogger<SendInputInjector>());
        Log.Information("Per-monitor DPI awareness (v2): {Aware}", DpiAwareness.IsPerMonitorV2());

        // Desktop Duplication reads the screen without disturbing the mouse cursor and does no work
        // while nothing changes; the older GDI capture is only the fallback.
        var gdiScreen = new GdiScreenCapturer();
        using var dxgiScreen = new DxgiScreenCapturer(gdiScreen, new Serilog.Extensions.Logging.SerilogLoggerFactory(Log.Logger).CreateLogger<DxgiScreenCapturer>());
        IScreenCapturer screen = string.Equals(config.Capture.Method.Trim(), "gdi", StringComparison.OrdinalIgnoreCase) ? gdiScreen : dxgiScreen;
        Log.Information("Screen capture method: {Method}", screen is DxgiScreenCapturer ? "Desktop Duplication (GDI as fallback)" : "GDI (chosen in config.json)");
        var loggers = new Serilog.Extensions.Logging.SerilogLoggerFactory(Log.Logger);

        // Try the ways of reading and changing the display scale in order. The first two use Windows'
        // own calls; the script is only there if the user set one up because those stopped working.
        var scaleBackends = new List<IDisplayScaleBackend>
        {
            new DisplayConfigScaleBackend(loggers.CreateLogger<DisplayConfigScaleBackend>()),
            new EffectiveDpiScaleBackend(),
        };
        if (!string.IsNullOrWhiteSpace(config.Dpi.FallbackScriptPath))
        {
            scaleBackends.Add(new ScriptScaleBackend(config.Dpi.FallbackScriptPath, loggers.CreateLogger<ScriptScaleBackend>()));
        }

        using var dpi = new DpiToggleService(screen, scaleBackends, config.Dpi);
        var dpiStatus = dpi.Check();
        if (dpiStatus.Available)
        {
            Log.Information("Display scale toggle ready: {Monitor} is at {Percent}% (via {Backend})", dpiStatus.Monitor?.Name, dpiStatus.Percent, dpiStatus.Backend);
        }
        else
        {
            Log.Warning("Display scale toggle switched off: {Reason}", dpiStatus.Reason);
        }

        var apps = new AppsService(
            new ShellAppCatalog(AppPaths.IconsDirectory, loggers.CreateLogger<ShellAppCatalog>()),
            new JsonAppsStateStore(AppPaths.AppsFile),
            time,
            config.Launcher.RecentCount);
        var powerToys = new PowerToysShortcutResolver(
            config.Launcher,
            () => File.Exists(AppPaths.PowerToysRunSettingsFile) ? File.ReadAllText(AppPaths.PowerToysRunSettingsFile) : null);
        var shortcut = powerToys.Resolve();
        Log.Information(
            "PowerToys Run button will press {Shortcut} ({Source})",
            shortcut.Shortcut,
            shortcut.FromPowerToys ? "read from PowerToys' settings" : "from config or the default");

        var app = ServerFactory.Build(new ServerDependencies(
            config, pins, limiter, registry, state, connections, injector, screen, new GdiPlusJpegEncoder(), dpi, apps, powerToys,
            new ShellUrlOpener(),
            new PowerService(new WindowsPowerBackend(), time),
            new ProcessService(new WindowsProcessCatalog(), Environment.ProcessId)));

        try
        {
            app.StartAsync().GetAwaiter().GetResult();
        }
        catch (IOException ex)
        {
            Log.Error(ex, "Could not listen on port {Port}", config.Port);
            MessageBox.Show(
                $"Port {config.Port} is already in use. Close the other program, or change \"port\" in {AppPaths.ConfigFile}.",
                AppTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }

        Log.Information("Listening on port {Port}", config.Port);

        using (var tray = new TrayApplicationContext(config, pins, registry, state, connections))
        {
            Application.Run(tray);
        }

        Log.Information("PC Control stopping");
        app.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        return 0;
    }
}
