using System.Text;
using PcControl.Core.Input;
using PcControl.Core.Protocol;

namespace PcControl.Tests;

public class InputDispatcherTests
{
    private readonly FakeInjector _injector = new();

    private InputSession Session() => new(_injector, new PointerCurve(1));

    private static WsMessage Json(string json)
    {
        Assert.True(WsMessageParser.TryParse(Encoding.UTF8.GetBytes(json), out var message, out _));
        return message!;
    }

    private static byte[] Frame(byte kind, short a, short b)
    {
        var frame = new byte[5];
        frame[0] = kind;
        BitConverter.TryWriteBytes(frame.AsSpan(1), a);
        BitConverter.TryWriteBytes(frame.AsSpan(3), b);
        return frame;
    }

    [Fact]
    public void BinaryMove_IsDecodedLittleEndian_IncludingNegatives()
    {
        var result = InputDispatcher.HandleBinary(Frame(InputDispatcher.MoveFrame, 3, -2), Session());

        Assert.True(result.IsInput);
        Assert.Null(result.ErrorCode);
        // Gain 1, distance sqrt(13) = 3.6, multiplier 1.36 -> (4.08, -2.72) -> (4, -2).
        Assert.Equal(["move 4,-2"], _injector.Events);
    }

    [Fact]
    public void BinaryScroll_IsPassedToTheWheel()
    {
        InputDispatcher.HandleBinary(Frame(InputDispatcher.ScrollFrame, -240, 120), Session());

        Assert.Equal(["wheel -240,120"], _injector.Events);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(6)]
    public void BinaryFrame_OfTheWrongLength_IsRefused(int length)
    {
        var result = InputDispatcher.HandleBinary(new byte[length], Session());

        Assert.Equal("invalid_message", result.ErrorCode);
        Assert.Empty(_injector.Events);
    }

    [Fact]
    public void BinaryFrame_WithAnUnknownKind_IsRefused()
    {
        Assert.Equal("invalid_message", InputDispatcher.HandleBinary(Frame(0x7F, 1, 1), Session()).ErrorCode);
    }

    [Fact]
    public void BinaryMove_BeyondTheLimit_IsRefused()
    {
        var result = InputDispatcher.HandleBinary(Frame(InputDispatcher.MoveFrame, short.MaxValue, 0), Session());

        Assert.Equal("invalid_value", result.ErrorCode);
    }

    [Theory]
    [InlineData("""{"type":"button","button":"left","action":"click"}""", "left down|left up")]
    [InlineData("""{"type":"button","button":"right","action":"click"}""", "right down|right up")]
    [InlineData("""{"type":"button","button":"middle","action":"down"}""", "middle down")]
    [InlineData("""{"type":"key","code":"Enter","action":"press"}""", "key 0D down|key 0D up")]
    [InlineData("""{"type":"key","code":"ShiftLeft","action":"down"}""", "key A0 down")]
    [InlineData("""{"type":"text","text":"abc"}""", "text abc")]
    [InlineData("""{"type":"zoom","steps":1}""", "key A2 down|wheel 120,0|key A2 up")]
    public void JsonInput_IsAppliedToTheInjector(string json, string expected)
    {
        var result = InputDispatcher.HandleJson(Json(json), Session());

        Assert.True(result.IsInput);
        Assert.Null(result.ErrorCode);
        Assert.Equal(expected.Split('|'), _injector.Events);
    }

    [Fact]
    public void Settings_ChangeTheSensitivity()
    {
        var session = Session();

        var result = InputDispatcher.HandleJson(Json("""{"type":"settings","sensitivity":2.5}"""), session);

        Assert.Null(result.ErrorCode);
        Assert.Equal(2.5, session.Sensitivity);
    }

    [Fact]
    public void ReleaseAll_ReleasesHeldInput()
    {
        var session = Session();
        session.KeyDown("ControlLeft");
        _injector.Clear();

        InputDispatcher.HandleJson(Json("""{"type":"releaseAll"}"""), session);

        Assert.Equal(["key A2 up"], _injector.Events);
    }

    [Theory]
    [InlineData("""{"type":"button","button":"back","action":"click"}""", "unknown_button")]
    [InlineData("""{"type":"button","action":"click"}""", "unknown_button")]
    [InlineData("""{"type":"button","button":"left","action":"wiggle"}""", "invalid_message")]
    [InlineData("""{"type":"key","code":"Bogus","action":"press"}""", "unknown_key")]
    [InlineData("""{"type":"key","code":"Enter","action":"hold"}""", "invalid_message")]
    [InlineData("""{"type":"key","action":"press"}""", "unknown_key")]
    [InlineData("""{"type":"text"}""", "invalid_message")]
    [InlineData("""{"type":"text","text":5}""", "invalid_message")]
    [InlineData("""{"type":"zoom","steps":"two"}""", "invalid_message")]
    [InlineData("""{"type":"zoom","steps":99}""", "invalid_value")]
    [InlineData("""{"type":"zoom","steps":1.5}""", "invalid_message")]
    [InlineData("""{"type":"settings","sensitivity":99}""", "invalid_value")]
    [InlineData("""{"type":"settings"}""", "invalid_message")]
    public void BadInput_IsRefusedWithACode_AndNothingIsInjected(string json, string code)
    {
        var result = InputDispatcher.HandleJson(Json(json), Session());

        Assert.True(result.IsInput);
        Assert.Equal(code, result.ErrorCode);
        Assert.Empty(_injector.Events);
    }

    [Theory]
    [InlineData("ping")]
    [InlineData("mystery")]
    public void NonInputTypes_AreLeftAlone(string type)
    {
        var result = InputDispatcher.HandleJson(Json($$"""{"type":"{{type}}"}"""), Session());

        Assert.False(result.IsInput);
    }

    [Theory]
    [InlineData("button", true)]
    [InlineData("key", true)]
    [InlineData("text", true)]
    [InlineData("zoom", true)]
    [InlineData("settings", true)]
    [InlineData("releaseAll", true)]
    [InlineData("ping", false)]
    [InlineData("hello", false)]
    public void IsInputMessage_ListsTheInputTypes(string type, bool expected)
    {
        Assert.Equal(expected, InputDispatcher.IsInputMessage(type));
    }
}
