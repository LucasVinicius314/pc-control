using PcControl.Core.Input;

namespace PcControl.Tests;

public class KeyCatalogTests
{
    [Theory]
    [InlineData("KeyA", 0x41, false)]
    [InlineData("KeyZ", 0x5A, false)]
    [InlineData("Digit0", 0x30, false)]
    [InlineData("Digit9", 0x39, false)]
    [InlineData("F1", 0x70, false)]
    [InlineData("F12", 0x7B, false)]
    [InlineData("Enter", 0x0D, false)]
    [InlineData("Escape", 0x1B, false)]
    [InlineData("Backspace", 0x08, false)]
    [InlineData("Space", 0x20, false)]
    [InlineData("ArrowLeft", 0x25, true)]
    [InlineData("Delete", 0x2E, true)]
    [InlineData("MetaLeft", 0x5B, true)]
    [InlineData("ControlLeft", 0xA2, false)]
    [InlineData("AltLeft", 0xA4, false)]
    [InlineData("ShiftLeft", 0xA0, false)]
    public void KnownKeys_MapToVirtualKeys(string code, int vk, bool extended)
    {
        Assert.True(KeyCatalog.TryGet(code, out var key));
        Assert.Equal((ushort)vk, key.VirtualKey);
        Assert.Equal(extended, key.Extended);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("keya")]
    [InlineData("F13")]
    [InlineData("0x41")]
    [InlineData("NumpadEnter")]
    [InlineData("Power")]
    public void UnknownKeys_AreRefused(string? code)
    {
        Assert.False(KeyCatalog.TryGet(code, out _));
    }

    [Fact]
    public void EveryLetterDigitAndFunctionKey_IsPresent()
    {
        for (var c = 'A'; c <= 'Z'; c++)
        {
            Assert.True(KeyCatalog.TryGet("Key" + c, out _));
        }

        for (var d = 0; d <= 9; d++)
        {
            Assert.True(KeyCatalog.TryGet("Digit" + d, out _));
        }

        for (var f = 1; f <= 12; f++)
        {
            Assert.True(KeyCatalog.TryGet("F" + f, out _));
        }
    }

    [Theory]
    [InlineData("ControlLeft", true)]
    [InlineData("ShiftRight", true)]
    [InlineData("AltLeft", true)]
    [InlineData("MetaRight", true)]
    [InlineData("KeyA", false)]
    [InlineData("Enter", false)]
    public void IsModifier_RecognisesModifiers(string code, bool expected)
    {
        Assert.Equal(expected, KeyCatalog.IsModifier(code));
    }

    [Theory]
    [InlineData("MediaPlayPause", 0xB3)]
    [InlineData("MediaTrackNext", 0xB0)]
    [InlineData("MediaTrackPrevious", 0xB1)]
    [InlineData("MediaStop", 0xB2)]
    [InlineData("AudioVolumeMute", 0xAD)]
    [InlineData("AudioVolumeDown", 0xAE)]
    [InlineData("AudioVolumeUp", 0xAF)]
    public void MediaKeys_AreInTheCatalog_AsExtendedKeys(string code, int virtualKey)
    {
        Assert.True(KeyCatalog.TryGet(code, out var key));
        Assert.Equal((ushort)virtualKey, key.VirtualKey);
        Assert.True(key.Extended);
        Assert.False(KeyCatalog.IsModifier(code));
    }
}
