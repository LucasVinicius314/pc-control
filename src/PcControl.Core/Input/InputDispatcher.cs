using System.Buffers.Binary;
using System.Text.Json;
using PcControl.Core.Protocol;

namespace PcControl.Core.Input;

/// <summary>What happened to an input message: not input at all, handled, or refused with a code.</summary>
public readonly record struct DispatchResult(bool IsInput, string? ErrorCode)
{
    public static DispatchResult NotInput => new(false, null);

    public static DispatchResult Handled => new(true, null);

    public static DispatchResult Error(string code) => new(true, code);
}

/// <summary>Maps wire messages (see docs/protocol/websocket.md) onto an <see cref="InputSession"/>.</summary>
public static class InputDispatcher
{
    public const byte MoveFrame = 0x01;
    public const byte ScrollFrame = 0x02;

    /// <summary>Binary input frames are a kind byte plus two little-endian int16 values.</summary>
    public const int BinaryFrameLength = 5;

    private static readonly HashSet<string> InputTypes =
        new(StringComparer.Ordinal) { "button", "key", "text", "zoom", "settings", "releaseAll" };

    /// <summary>True for message types that put input on the PC (and so stop while the kill switch is on).</summary>
    public static bool IsInputMessage(string type) => InputTypes.Contains(type);

    public static DispatchResult HandleBinary(ReadOnlySpan<byte> frame, InputSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (frame.Length != BinaryFrameLength)
        {
            return DispatchResult.Error("invalid_message");
        }

        var a = BinaryPrimitives.ReadInt16LittleEndian(frame[1..]);
        var b = BinaryPrimitives.ReadInt16LittleEndian(frame[3..]);

        return frame[0] switch
        {
            MoveFrame => FromResult(session.Move(a, b)),
            ScrollFrame => FromResult(session.Scroll(a, b)),
            _ => DispatchResult.Error("invalid_message"),
        };
    }

    public static DispatchResult HandleJson(WsMessage message, InputSession session)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(session);

        var root = message.Root;

        switch (message.Type)
        {
            case "button":
                return HandleButton(root, session);

            case "key":
                return HandleKey(root, session);

            case "text":
                return GetString(root, "text") is { } text
                    ? FromResult(session.TypeText(text))
                    : DispatchResult.Error("invalid_message");

            case "zoom":
                return GetInt(root, "steps") is { } steps
                    ? FromResult(session.Zoom(steps))
                    : DispatchResult.Error("invalid_message");

            case "settings":
                return GetDouble(root, "sensitivity") is { } sensitivity
                    ? FromResult(session.SetSensitivity(sensitivity))
                    : DispatchResult.Error("invalid_message");

            case "releaseAll":
                session.ReleaseAll();
                return DispatchResult.Handled;

            default:
                return DispatchResult.NotInput;
        }
    }

    private static DispatchResult HandleButton(JsonElement root, InputSession session)
    {
        MouseButton? button = GetString(root, "button") switch
        {
            "left" => MouseButton.Left,
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            _ => null,
        };

        if (button is null)
        {
            return DispatchResult.Error("unknown_button");
        }

        switch (GetString(root, "action"))
        {
            case "down":
                session.ButtonDown(button.Value);
                return DispatchResult.Handled;
            case "up":
                session.ButtonUp(button.Value);
                return DispatchResult.Handled;
            case "click":
                session.ButtonClick(button.Value);
                return DispatchResult.Handled;
            default:
                return DispatchResult.Error("invalid_message");
        }
    }

    private static DispatchResult HandleKey(JsonElement root, InputSession session)
    {
        var code = GetString(root, "code");
        return GetString(root, "action") switch
        {
            "down" => FromResult(session.KeyDown(code)),
            "up" => FromResult(session.KeyUp(code)),
            "press" => FromResult(session.KeyPress(code)),
            _ => DispatchResult.Error("invalid_message"),
        };
    }

    private static DispatchResult FromResult(InputResult result) => result switch
    {
        InputResult.Ok => DispatchResult.Handled,
        InputResult.UnknownKey => DispatchResult.Error("unknown_key"),
        InputResult.TooManyHeldKeys => DispatchResult.Error("too_many_held_keys"),
        _ => DispatchResult.Error("invalid_value"),
    };

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? GetInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static double? GetDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number
            : null;
}
