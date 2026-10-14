using System.Text;
using PcControl.Core.Protocol;

namespace PcControl.Tests;

public class WsMessageParserTests
{
    private static bool Parse(string json, out WsMessage? message, out string? error) =>
        WsMessageParser.TryParse(Encoding.UTF8.GetBytes(json), out message, out error);

    [Fact]
    public void ValidMessage_IsParsed()
    {
        Assert.True(Parse("""{"type":"ping","t":5}""", out var message, out var error));

        Assert.Null(error);
        Assert.Equal("ping", message!.Type);
        Assert.Equal(5, message.Root.GetProperty("t").GetInt32());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("""{"type":5}""")]
    [InlineData("""{"type":""}""")]
    [InlineData("""{"type":null}""")]
    public void InvalidMessage_IsRejected(string json)
    {
        Assert.False(Parse(json, out var message, out var error));

        Assert.Null(message);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void OverlongType_IsRejected()
    {
        var json = $$"""{"type":"{{new string('a', WsMessageParser.MaxTypeLength + 1)}}"}""";

        Assert.False(Parse(json, out _, out _));
    }

    [Fact]
    public void OversizedMessage_IsRejected()
    {
        var json = $$"""{"type":"ping","pad":"{{new string('a', WsMessageParser.MaxMessageBytes)}}"}""";

        Assert.False(Parse(json, out _, out var error));
        Assert.Equal("message size out of range", error);
    }
}
