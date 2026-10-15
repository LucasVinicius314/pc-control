using PcControl.Core.Config;
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
        using var mutex = new Mutex(initiallyOwned: true, @"Local\PcControl.SingleInstance", out var isFirst);
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

        var app = ServerFactory.Build(config, pins, limiter, registry, state, connections, injector, new GdiScreenCapturer(), new GdiPlusJpegEncoder());

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
