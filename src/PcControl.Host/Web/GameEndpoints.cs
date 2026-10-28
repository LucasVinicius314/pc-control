using PcControl.Core.Apps;
using PcControl.Core.Games;

namespace PcControl.Host.Web;

/// <summary>Tells what kind of picture a file is from its first bytes (covers can be png, jpeg or webp).</summary>
internal static class ImageTypes
{
    public static string Sniff(byte[] data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (data.Length >= 12 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' && data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P')
        {
            return "image/webp";
        }

        if (data.Length >= 4 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8')
        {
            return "image/gif";
        }

        return "image/png";
    }
}

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
            games = list.Games.Select(g => new { id = g.Id, title = g.Title, serial = g.Serial, source = g.Source }),
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
        return Results.File(png, ImageTypes.Sniff(png));
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
                var why = games.ProblemFor(id);
                logger.LogWarning("{DeviceName} tried to start {Game} but it did not start{Why}", context.User.Identity!.Name, result.App!.Name, why is null ? string.Empty : ": " + why);
                return Results.Json(
                    new { error = "launch_failed", message = why ?? $"{result.App.Name} did not start." },
                    statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
