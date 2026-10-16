using PcControl.Core.Apps;
using PcControl.Core.Games;

namespace PcControl.Host.Web;

/// <summary>The games shelf: the game list, their pictures, and starting one.</summary>
internal static class GameEndpoints
{
    public static void Map(RouteGroupBuilder secured)
    {
        secured.MapGet("/games", GetGames);
        secured.MapGet("/games/{id}/cover", (string id, GamesService games, HttpContext context) => Image(id, GameImage.Cover, games, context));
        secured.MapGet("/games/{id}/background", (string id, GamesService games, HttpContext context) => Image(id, GameImage.Background, games, context));
        secured.MapPost("/games/{id}/launch", Launch);
    }

    private static IResult GetGames(GamesService games)
    {
        if (!games.Available)
        {
            return Results.Json(new { available = false, reason = games.UnavailableReason, games = Array.Empty<object>(), recent = Array.Empty<string>() });
        }

        var list = games.GetList();
        return Results.Json(new
        {
            available = true,
            reason = (string?)null,
            games = list.Games.Select(g => new { id = g.Id, title = g.Title, serial = g.Serial, source = "rpcs3" }),
            recent = list.Recent,
        });
    }

    private static IResult Image(string id, GameImage image, GamesService games, HttpContext context)
    {
        var png = games.GetImage(id, image);
        if (png is null)
        {
            return Results.Json(new { error = "no_image" }, statusCode: StatusCodes.Status404NotFound);
        }

        context.Response.Headers.CacheControl = "private, max-age=3600";
        return Results.File(png, "image/png");
    }

    private static IResult Launch(string id, GamesService games, RemoteControlState state, HttpContext context, ILogger<GamesService> logger)
    {
        if (ApiEndpoints.RefusedWhilePaused(state) is { } paused)
        {
            return paused;
        }

        var result = games.Launch(id);
        switch (result.Outcome)
        {
            case LaunchOutcome.Ok:
                logger.LogInformation("{DeviceName} started the game {Game}", context.User.Identity!.Name, result.App!.Name);
                return Results.Json(new { launched = true, title = result.App.Name });

            case LaunchOutcome.UnknownApp:
                return Results.Json(new { error = "unknown_game" }, statusCode: StatusCodes.Status404NotFound);

            default:
                logger.LogWarning("{DeviceName} tried to start {Game} but it did not start", context.User.Identity!.Name, result.App!.Name);
                return Results.Json(
                    new { error = "launch_failed", message = $"{result.App.Name} did not start." },
                    statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
