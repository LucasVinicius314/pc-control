using System.Text.Json;

namespace PcControl.Core.Protocol;

public static class WsMessageTypes
{
    public const string Hello = "hello";
    public const string Ping = "ping";
    public const string Pong = "pong";
    public const string Paused = "paused";
    public const string Error = "error";
}

/// <summary>A parsed client text message: <c>{"type": "...", ...}</c>.</summary>
public sealed record WsMessage(string Type, JsonElement Root);

public static class WsMessageParser
{
    public const int MaxMessageBytes = 4096;
    public const int MaxTypeLength = 32;

    /// <summary>Parses a client text frame. On failure <paramref name="error"/> says why.</summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out WsMessage? message, out string? error)
    {
        message = null;

        if (utf8.Length == 0 || utf8.Length > MaxMessageBytes)
        {
            error = "message size out of range";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(utf8.ToArray());
            var root = doc.RootElement.Clone();

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String)
            {
                error = "message needs a string \"type\"";
                return false;
            }

            var type = typeElement.GetString();
            if (string.IsNullOrEmpty(type) || type.Length > MaxTypeLength)
            {
                error = "invalid message type";
                return false;
            }

            message = new WsMessage(type, root);
            error = null;
            return true;
        }
        catch (JsonException)
        {
            error = "malformed JSON";
            return false;
        }
    }
}
