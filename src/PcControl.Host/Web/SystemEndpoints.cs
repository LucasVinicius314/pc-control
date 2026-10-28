using PcControl.Core.Actions;
using PcControl.Core.Capture;
using PcControl.Core.Config;
using PcControl.Core.Performance;

namespace PcControl.Host.Web;

internal sealed record UrlRequest(string? Url);

internal sealed record PowerRequest(string? Action, bool? Confirm);

internal sealed record CloseRequest(bool? Force);

/// <summary>Open a link on the PC, power actions, and closing programs.</summary>
internal static class SystemEndpoints
{
    public static void Map(RouteGroupBuilder secured)
    {
        secured.MapPost("/url", OpenUrl);

        secured.MapGet("/power", GetPower);
        secured.MapPost("/power", RunPower);
        secured.MapDelete("/power/pending", CancelPower);

        secured.MapGet("/performance", GetPerformance);
        secured.MapGet("/processes", GetProcesses);
        secured.MapGet("/processes/{id}/preview", GetProcessPreview);
        secured.MapPost("/processes/{id}/close", CloseProcess);
        secured.MapPost("/processes/{id}/focus", FocusProcess);
    }

    private static IResult Disabled() =>
        Results.Json(new { error = "feature_disabled" }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult OpenUrl(UrlRequest request, AppConfig config, IUrlOpener opener, RemoteControlState state, HttpContext context, ILogger<UrlRequest> logger)
    {
        if (!config.Features.UrlOpen)
        {
            return Disabled();
        }

        if (ApiEndpoints.RefusedWhilePaused(state) is { } paused)
        {
            return paused;
        }

        if (!UrlRules.TryNormalize(request.Url, out var url))
        {
            return Results.Json(
                new { error = "invalid_url", message = "Only web addresses (http or https) can be opened." },
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!opener.Open(url))
        {
            return Results.Json(
                new { error = "open_failed", message = "Windows could not open that address." },
                statusCode: StatusCodes.Status502BadGateway);
        }

        // Only the site is logged: the rest of an address can carry private tokens.
        logger.LogInformation("{DeviceName} opened a link to {Host}", context.User.Identity!.Name, url.Host);
        return Results.Json(new { opened = true, url = url.AbsoluteUri });
    }

    private static IResult GetPower(AppConfig config, PowerService power)
    {
        if (!config.Features.Power)
        {
            return Disabled();
        }

        var pending = power.Pending;
        return Results.Json(new
        {
            pending = pending is null ? null : new { action = Name(pending.Action), secondsLeft = pending.SecondsLeft },
        });
    }

    private static IResult RunPower(PowerRequest request, AppConfig config, PowerService power, RemoteControlState state, HttpContext context, ILogger<PowerService> logger)
    {
        if (!config.Features.Power)
        {
            return Disabled();
        }

        if (ApiEndpoints.RefusedWhilePaused(state) is { } paused)
        {
            return paused;
        }

        if (!PowerService.TryParseAction(request.Action, out var action))
        {
            return Results.Json(new { error = "unknown_action" }, statusCode: StatusCodes.Status400BadRequest);
        }

        var result = power.Request(action, request.Confirm == true);
        switch (result.Outcome)
        {
            case PowerOutcome.ConfirmationRequired:
                return Results.Json(
                    new { error = "confirmation_required", message = "This needs an explicit confirmation." },
                    statusCode: StatusCodes.Status400BadRequest);

            case PowerOutcome.Ok:
                logger.LogWarning("{DeviceName} asked the PC to {Action}", context.User.Identity!.Name, Name(action));
                return Results.Json(new { action = Name(action), delaySeconds = result.DelaySeconds });

            default:
                logger.LogWarning("{DeviceName} asked the PC to {Action} but Windows refused", context.User.Identity!.Name, Name(action));
                return Results.Json(
                    new { error = "power_failed", message = "Windows did not carry that out." },
                    statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static IResult CancelPower(AppConfig config, PowerService power, HttpContext context, ILogger<PowerService> logger)
    {
        if (!config.Features.Power)
        {
            return Disabled();
        }

        // Cancelling stays possible while paused: it only ever makes the PC safer.
        if (!power.Cancel())
        {
            return Results.Json(new { error = "nothing_pending" }, statusCode: StatusCodes.Status404NotFound);
        }

        logger.LogWarning("{DeviceName} cancelled the pending restart or shutdown", context.User.Identity!.Name);
        return Results.NoContent();
    }

    /// <summary>Task-manager numbers: the whole PC, and the busiest programs by the chosen column.</summary>
    private static IResult GetPerformance(PerformanceService performance, string? sort, int? count)
    {
        if (!PerformanceService.TryParseSort(sort, out var order))
        {
            return Results.Json(new { error = "invalid_sort" }, statusCode: StatusCodes.Status400BadRequest);
        }

        var snapshot = performance.Get(order, count ?? 25);
        return Results.Json(new
        {
            cpu = snapshot.CpuPercent,
            cores = snapshot.Cores,
            memory = new { used = snapshot.MemoryUsedBytes, total = snapshot.MemoryTotalBytes },
            gpu = snapshot.GpuPercent,
            disk = new { percent = snapshot.DiskPercent, readPerSecond = snapshot.DiskReadBytesPerSecond, writePerSecond = snapshot.DiskWriteBytesPerSecond },
            network = new { downPerSecond = snapshot.NetworkDownBytesPerSecond, upPerSecond = snapshot.NetworkUpBytesPerSecond },
            processes = snapshot.Processes.Select(p => new
            {
                name = p.Name,
                count = p.Count,
                cpu = p.CpuPercent,
                memory = p.MemoryBytes,
                gpu = p.GpuPercent,
                io = p.IoBytesPerSecond,
            }),
        });
    }

    private static IResult GetProcesses(AppConfig config, ProcessService processes)
    {
        if (!config.Features.ProcessKiller)
        {
            return Disabled();
        }

        return Results.Json(processes.GetList().Select(p => new { id = p.Id, name = p.Name, title = p.Title, minimized = p.Minimized }));
    }

    private static IResult GetProcessPreview(string id, int? width, AppConfig config, ProcessService processes, IJpegEncoder encoder, RemoteControlState state, HttpContext context)
    {
        if (!config.Features.ProcessKiller)
        {
            return Disabled();
        }

        // A window's picture shows what is on the PC, so it follows the same kill switch as the rest.
        if (ApiEndpoints.RefusedWhilePaused(state) is { } paused)
        {
            return paused;
        }

        var (outcome, frame) = processes.Preview(id, Math.Clamp(width ?? 480, 160, 960));
        if (outcome == ProcessOutcome.Unknown)
        {
            return Results.Json(new { error = "unknown_process" }, statusCode: StatusCodes.Status404NotFound);
        }

        if (frame is null)
        {
            return Results.Json(new { error = "no_image" }, statusCode: StatusCodes.Status404NotFound);
        }

        context.Response.Headers.CacheControl = "no-store";
        return Results.File(encoder.Encode(frame, 70), "image/jpeg");
    }

    private static IResult CloseProcess(string id, CloseRequest? request, AppConfig config, ProcessService processes, RemoteControlState state, HttpContext context, ILogger<ProcessService> logger)
    {
        if (!config.Features.ProcessKiller)
        {
            return Disabled();
        }

        if (ApiEndpoints.RefusedWhilePaused(state) is { } paused)
        {
            return paused;
        }

        var force = request?.Force == true;
        var (outcome, title) = processes.Close(id, force);
        switch (outcome)
        {
            case ProcessOutcome.Ok:
                logger.LogWarning("{DeviceName} {How} {Program}", context.User.Identity!.Name, force ? "force-closed" : "asked to close", title);
                return Results.Json(new { closed = true, title });

            case ProcessOutcome.Unknown:
                return Results.Json(new { error = "unknown_process" }, statusCode: StatusCodes.Status404NotFound);

            case ProcessOutcome.Gone:
                return Results.Json(new { error = "process_gone" }, statusCode: StatusCodes.Status404NotFound);

            default:
                return Results.Json(
                    new { error = "close_failed", message = $"{title} did not close." },
                    statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static IResult FocusProcess(string id, AppConfig config, ProcessService processes, RemoteControlState state, HttpContext context, ILogger<ProcessService> logger)
    {
        if (!config.Features.ProcessKiller)
        {
            return Disabled();
        }

        if (ApiEndpoints.RefusedWhilePaused(state) is { } paused)
        {
            return paused;
        }

        var (outcome, title) = processes.Switch(id);
        switch (outcome)
        {
            case ProcessOutcome.Ok:
                logger.LogInformation("{DeviceName} switched to {Program}", context.User.Identity!.Name, title);
                return Results.Json(new { focused = true, title });

            case ProcessOutcome.Unknown:
                return Results.Json(new { error = "unknown_process" }, statusCode: StatusCodes.Status404NotFound);

            case ProcessOutcome.Gone:
                return Results.Json(new { error = "process_gone" }, statusCode: StatusCodes.Status404NotFound);

            default:
                return Results.Json(
                    new { error = "focus_failed", message = $"Windows would not bring {title} to the front." },
                    statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static string Name(PowerAction action) => action switch
    {
        PowerAction.SignOut => "signOut",
        _ => action.ToString().ToLowerInvariant(),
    };
}
