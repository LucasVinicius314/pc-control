using System.Text.Json;
using PcControl.Core.Protocol;

namespace PcControl.Core.Capture;

/// <summary>What the phone asked a stream to show. Values are always inside the safe limits.</summary>
public sealed record ViewOptions(ViewChannel Stream, bool Enabled, int Monitor, int Width, int Height, int Fps)
{
    public const int MaxMagnifierFps = 20;
    public const int MaxDisplayFps = 10;
    public const int MaxRegionWidth = 800;
    public const int MaxRegionHeight = 600;
    public const int MaxDisplayWidth = 1920;

    /// <summary>The display preview is never taller than this, whatever the monitor's shape.</summary>
    public const int MaxDisplayHeight = 900;
    public const int DefaultMagnifierFps = 15;
    public const int DefaultDisplayFps = 8;
    public const int DefaultDisplayWidth = 720;

    /// <summary>Monitor value meaning "whichever one the cursor is on".</summary>
    public const int FollowCursor = -1;
}

public static class ViewRequestParser
{
    /// <summary>
    /// Reads a <c>view</c> message. Out-of-range numbers are clamped rather than refused, because a
    /// phone that asks for too much should still get a working stream.
    /// </summary>
    public static bool TryParse(WsMessage message, out ViewOptions? options)
    {
        ArgumentNullException.ThrowIfNull(message);
        options = null;
        var root = message.Root;

        ViewChannel stream;
        switch (GetString(root, "stream"))
        {
            case "magnifier":
                stream = ViewChannel.Magnifier;
                break;
            case "display":
                stream = ViewChannel.Display;
                break;
            default:
                return false;
        }

        if (!root.TryGetProperty("enabled", out var enabledElement)
            || enabledElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        var monitor = GetInt(root, "monitor") ?? ViewOptions.FollowCursor;
        monitor = monitor < 0 ? ViewOptions.FollowCursor : Math.Min(monitor, 15);

        if (stream == ViewChannel.Magnifier)
        {
            options = new ViewOptions(
                stream,
                enabledElement.GetBoolean(),
                monitor,
                Math.Clamp(GetInt(root, "width") ?? 200, 16, ViewOptions.MaxRegionWidth),
                Math.Clamp(GetInt(root, "height") ?? 120, 16, ViewOptions.MaxRegionHeight),
                Math.Clamp(GetInt(root, "fps") ?? ViewOptions.DefaultMagnifierFps, 1, ViewOptions.MaxMagnifierFps));
        }
        else
        {
            options = new ViewOptions(
                stream,
                enabledElement.GetBoolean(),
                monitor,
                Math.Clamp(GetInt(root, "width") ?? ViewOptions.DefaultDisplayWidth, 160, ViewOptions.MaxDisplayWidth),
                0,
                Math.Clamp(GetInt(root, "fps") ?? ViewOptions.DefaultDisplayFps, 1, ViewOptions.MaxDisplayFps));
        }

        return true;
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? GetInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;
}
