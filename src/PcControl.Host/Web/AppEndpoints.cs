using PcControl.Core.Apps;
using PcControl.Core.Input;

namespace PcControl.Host.Web;

internal sealed record FavoriteRequest(bool? Favorite);

/// <summary>The app launcher: the Start menu, favorites, recent apps, and the PowerToys Run button.</summary>
internal static class AppEndpoints
{
    /// <summary>Icons are requested at this size; the phone scales them as needed.</summary>
    private const int IconSize = 64;

    public static void Map(RouteGroupBuilder secured)
    {
        secured.MapGet("/apps", GetApps);
        secured.MapGet("/apps/{id}/icon", GetIcon);
        secured.MapPost("/apps/{id}/launch", Launch);
        secured.MapPut("/apps/{id}/favorite", SetFavorite);
        secured.MapPost("/powertoys-run", PressPowerToysRun);
    }

    private static IResult GetApps(AppsService apps, PowerToysShortcutResolver powerToys)
    {
        var list = apps.GetList();
        var shortcut = powerToys.Resolve();

        return Results.Json(new
        {
            apps = list.Apps,
            recent = list.Recent,
            launcher = new
            {
                powerToysRunShortcut = shortcut.Shortcut.ToString(),
                powerToysDetected = shortcut.FromPowerToys,
            },
        });
    }

    private static IResult GetIcon(string id, AppsService apps, HttpContext context)
    {
        var png = apps.GetIcon(id, IconSize);
        if (png is null)
        {
            return Results.Json(new { error = "no_icon" }, statusCode: StatusCodes.Status404NotFound);
        }

        // Icons rarely change; letting the phone keep them makes the list open faster next time.
        context.Response.Headers.CacheControl = "private, max-age=3600";
        return Results.File(png, "image/png");
    }

    private static IResult Launch(string id, AppsService apps, RemoteControlState state, HttpContext context, ILogger<AppsService> logger)
    {
        if (ApiEndpoints.RefusedWhilePaused(state) is { } paused)
        {
            return paused;
        }

        var result = apps.Launch(id);
        switch (result.Outcome)
        {
            case LaunchOutcome.Ok:
                logger.LogInformation("{DeviceName} launched {App}", context.User.Identity!.Name, result.App!.Name);
                return Results.Json(new { launched = true, name = result.App.Name });

            case LaunchOutcome.UnknownApp:
                return Results.Json(new { error = "unknown_app" }, statusCode: StatusCodes.Status404NotFound);

            default:
                logger.LogWarning("{DeviceName} tried to launch {App} but Windows could not start it", context.User.Identity!.Name, result.App!.Name);
                return Results.Json(
                    new { error = "launch_failed", message = $"Windows could not start {result.App.Name}." },
                    statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static IResult SetFavorite(string id, FavoriteRequest request, AppsService apps)
    {
        if (request.Favorite is not { } favorite)
        {
            return Results.Json(new { error = "invalid_message" }, statusCode: StatusCodes.Status400BadRequest);
        }

        return apps.SetFavorite(id, favorite)
            ? Results.NoContent()
            : Results.Json(new { error = "unknown_app" }, statusCode: StatusCodes.Status404NotFound);
    }

    private static IResult PressPowerToysRun(
        PowerToysShortcutResolver powerToys,
        IInputInjector injector,
        RemoteControlState state,
        HttpContext context,
        ILogger<AppsService> logger)
    {
        if (ApiEndpoints.RefusedWhilePaused(state) is { } paused)
        {
            return paused;
        }

        var shortcut = powerToys.Resolve();
        shortcut.Shortcut.Send(injector);
        logger.LogInformation("{DeviceName} pressed {Shortcut} to open PowerToys Run", context.User.Identity!.Name, shortcut.Shortcut);
        return Results.NoContent();
    }
}
