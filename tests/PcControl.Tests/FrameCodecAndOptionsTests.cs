using System.Text;
using PcControl.Core.Capture;
using PcControl.Core.Protocol;

namespace PcControl.Tests;

public class FrameCodecTests
{
    [Fact]
    public void Encode_PutsTheHeaderBeforeTheJpeg()
    {
        var frame = FrameCodec.Encode(ViewChannel.Display, 2, 300, -1, [0xFF, 0xD8, 0xFF]);

        Assert.Equal(FrameCodec.DisplayKind, frame[0]);
        Assert.Equal([0xFF, 0xD8, 0xFF], frame[FrameCodec.HeaderLength..]);
        Assert.True(FrameCodec.TryDecodeHeader(frame, out var channel, out var monitor, out var x, out var y));
        Assert.Equal(ViewChannel.Display, channel);
        Assert.Equal(2, monitor);
        Assert.Equal((300, -1), (x, y));
    }

    [Fact]
    public void Encode_MagnifierUsesItsOwnKind()
    {
        var frame = FrameCodec.Encode(ViewChannel.Magnifier, 1, 1, 2, [1]);

        Assert.Equal(FrameCodec.MagnifierKind, frame[0]);
        Assert.True(FrameCodec.TryDecodeHeader(frame, out var channel, out _, out _, out _));
        Assert.Equal(ViewChannel.Magnifier, channel);
    }

    [Fact]
    public void Encode_ClampsCursorValuesIntoInt16()
    {
        var frame = FrameCodec.Encode(ViewChannel.Display, 0, 100_000, -100_000, [1]);

        Assert.True(FrameCodec.TryDecodeHeader(frame, out _, out _, out var x, out var y));
        Assert.Equal((short.MaxValue, short.MinValue), (x, y));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x10, 0, 0, 0, 0 })] // header only, no picture
    [InlineData(new byte[] { 0x42, 0, 0, 0, 0, 1 })] // unknown kind
    public void TryDecodeHeader_RejectsBadFrames(byte[] frame)
    {
        Assert.False(FrameCodec.TryDecodeHeader(frame, out _, out _, out _, out _));
    }
}

public class ViewRequestParserTests
{
    private static ViewOptions? Parse(string json)
    {
        Assert.True(WsMessageParser.TryParse(Encoding.UTF8.GetBytes(json), out var message, out _));
        return ViewRequestParser.TryParse(message!, out var options) ? options : null;
    }

    [Fact]
    public void Magnifier_UsesTheRequestedValues()
    {
        var o = Parse("""{"type":"view","stream":"magnifier","enabled":true,"width":300,"height":150,"fps":12,"monitor":1}""");

        Assert.Equal(new ViewOptions(ViewChannel.Magnifier, true, 1, 300, 150, 12), o);
    }

    [Fact]
    public void Magnifier_FillsInDefaults()
    {
        var o = Parse("""{"type":"view","stream":"magnifier","enabled":true}""");

        Assert.Equal(new ViewOptions(ViewChannel.Magnifier, true, ViewOptions.FollowCursor, 200, 120, ViewOptions.DefaultMagnifierFps), o);
    }

    [Fact]
    public void Display_UsesWidthAndFps_AndIgnoresHeight()
    {
        var o = Parse("""{"type":"view","stream":"display","enabled":true,"width":1000,"height":9,"fps":4}""");

        Assert.Equal(new ViewOptions(ViewChannel.Display, true, ViewOptions.FollowCursor, 1000, 0, 4), o);
    }

    [Fact]
    public void Display_FillsInDefaults()
    {
        var o = Parse("""{"type":"view","stream":"display","enabled":false}""");

        Assert.Equal(new ViewOptions(ViewChannel.Display, false, ViewOptions.FollowCursor, ViewOptions.DefaultDisplayWidth, 0, ViewOptions.DefaultDisplayFps), o);
    }

    [Fact]
    public void OutOfRangeValues_AreClamped_NotRefused()
    {
        var magnifier = Parse("""{"type":"view","stream":"magnifier","enabled":true,"width":99999,"height":1,"fps":500}""")!;
        var display = Parse("""{"type":"view","stream":"display","enabled":true,"width":99999,"fps":0}""")!;

        Assert.Equal((ViewOptions.MaxRegionWidth, 16, ViewOptions.MaxMagnifierFps), (magnifier.Width, magnifier.Height, magnifier.Fps));
        Assert.Equal((ViewOptions.MaxDisplayWidth, 1), (display.Width, display.Fps));
    }

    [Fact]
    public void NegativeOrHugeMonitor_IsNormalised()
    {
        Assert.Equal(ViewOptions.FollowCursor, Parse("""{"type":"view","stream":"display","enabled":true,"monitor":-5}""")!.Monitor);
        Assert.Equal(15, Parse("""{"type":"view","stream":"display","enabled":true,"monitor":999}""")!.Monitor);
    }

    [Theory]
    [InlineData("""{"type":"view","enabled":true}""")]
    [InlineData("""{"type":"view","stream":"audio","enabled":true}""")]
    [InlineData("""{"type":"view","stream":"display"}""")]
    [InlineData("""{"type":"view","stream":"display","enabled":"yes"}""")]
    [InlineData("""{"type":"view","stream":5,"enabled":true}""")]
    public void MalformedRequests_AreRefused(string json)
    {
        Assert.Null(Parse(json));
    }
}
