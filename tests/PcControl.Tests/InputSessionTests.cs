using System.Globalization;
using PcControl.Core.Input;

namespace PcControl.Tests;

public class InputSessionTests
{
    private readonly FakeInjector _injector = new();

    private InputSession Create(double gain = 1) => new(_injector, new PointerCurve(gain));

    [Fact]
    public void Move_AppliesCurveAndSensitivity()
    {
        var session = Create(gain: 2);
        session.SetSensitivity(1.5);

        session.Move(1, 0);

        // Multiplier at distance 1 is 2 * (1 + 1/10) = 2.2, times 1.5 = 3.3, so 3 whole pixels.
        Assert.Equal(["move 3,0"], _injector.Events);
    }

    [Fact]
    public void Move_KeepsTheFractionalRemainder()
    {
        var session = Create(gain: 1);
        session.SetSensitivity(0.25);

        for (var i = 0; i < 40; i++)
        {
            session.Move(1, 0);
        }

        var total = _injector.Events.Sum(e => int.Parse(e.Split(' ')[1].Split(',')[0], CultureInfo.InvariantCulture));

        // 40 steps of 0.275 px = 11 px. Without the remainder every step would round to zero.
        Assert.InRange(total, 10, 11);
    }

    [Fact]
    public void Move_ZeroDelta_SendsNothing()
    {
        Create().Move(0, 0);

        Assert.Empty(_injector.Events);
    }

    [Fact]
    public void Move_OverTheLimit_IsRejected()
    {
        var result = Create().Move(InputSession.MaxDelta + 1, 0);

        Assert.Equal(InputResult.InvalidValue, result);
        Assert.Empty(_injector.Events);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(5)]
    [InlineData(double.NaN)]
    public void Sensitivity_OutOfRange_IsRejected(double value)
    {
        var session = Create();

        Assert.Equal(InputResult.InvalidValue, session.SetSensitivity(value));
        Assert.Equal(1, session.Sensitivity);
    }

    [Fact]
    public void Scroll_PassesWheelUnitsThrough_AndRejectsHugeValues()
    {
        var session = Create();

        Assert.Equal(InputResult.Ok, session.Scroll(240, -120));
        Assert.Equal(InputResult.InvalidValue, session.Scroll(InputSession.MaxWheel + 1, 0));
        Assert.Equal(["wheel 240,-120"], _injector.Events);
    }

    [Fact]
    public void Buttons_TrackHeldState()
    {
        var session = Create();

        session.ButtonDown(MouseButton.Left);
        session.ButtonDown(MouseButton.Left);
        Assert.Equal(1, session.HeldCount);
        session.ButtonUp(MouseButton.Left);
        session.ButtonUp(MouseButton.Left);

        Assert.Equal(["left down", "left up"], _injector.Events);
        Assert.Equal(0, session.HeldCount);
    }

    [Fact]
    public void ButtonClick_IsDownThenUp_AndSkippedWhileHeld()
    {
        var session = Create();

        session.ButtonClick(MouseButton.Right);
        session.ButtonDown(MouseButton.Left);
        session.ButtonClick(MouseButton.Left);

        Assert.Equal(["right down", "right up", "left down"], _injector.Events);
    }

    [Fact]
    public void KeyPress_IsDownThenUp()
    {
        var session = Create();

        Assert.Equal(InputResult.Ok, session.KeyPress("Enter"));

        Assert.Equal(["key 0D down", "key 0D up"], _injector.Events);
        Assert.Equal(0, session.HeldCount);
    }

    [Fact]
    public void KeyDownAndUp_TrackHeldState_AndIgnoreDuplicates()
    {
        var session = Create();

        session.KeyDown("ControlLeft");
        session.KeyDown("ControlLeft");
        session.KeyUp("ControlLeft");
        session.KeyUp("ControlLeft");

        Assert.Equal(["key A2 down", "key A2 up"], _injector.Events);
    }

    [Fact]
    public void HeldModifier_WrapsALaterKeyPress()
    {
        var session = Create();

        session.KeyDown("ControlLeft");
        session.KeyPress("KeyC");
        session.KeyUp("ControlLeft");

        Assert.Equal(["key A2 down", "key 43 down", "key 43 up", "key A2 up"], _injector.Events);
    }

    [Fact]
    public void UnknownKey_IsRefused()
    {
        var session = Create();

        Assert.Equal(InputResult.UnknownKey, session.KeyDown("Bogus"));
        Assert.Equal(InputResult.UnknownKey, session.KeyUp(null));
        Assert.Equal(InputResult.UnknownKey, session.KeyPress("F99"));
        Assert.Empty(_injector.Events);
    }

    [Fact]
    public void TooManyHeldKeys_AreRefused()
    {
        var session = Create();
        for (var i = 0; i < InputSession.MaxHeldKeys; i++)
        {
            Assert.Equal(InputResult.Ok, session.KeyDown("Key" + (char)('A' + i)));
        }

        Assert.Equal(InputResult.TooManyHeldKeys, session.KeyDown("Digit1"));
    }

    [Fact]
    public void TypeText_SendsUnicodeRuns_AndTurnsNewlinesAndTabsIntoKeys()
    {
        var session = Create();

        session.TypeText("héllo\nwörld\tok");

        Assert.Equal(
            ["text héllo", "key 0D down", "key 0D up", "text wörld", "key 09 down", "key 09 up", "text ok"],
            _injector.Events);
    }

    [Fact]
    public void TypeText_NormalisesCarriageReturns_AndDropsOtherControlCharacters()
    {
        var session = Create();

        session.TypeText("a\r\nb\rc\u0007d");

        Assert.Equal(
            ["text a", "key 0D down", "key 0D up", "text b", "key 0D down", "key 0D up", "text cd"],
            _injector.Events);
    }

    [Fact]
    public void TypeText_KeepsEmojiAsOneRun()
    {
        Create().TypeText("hi 😀");

        Assert.Equal(["text hi 😀"], _injector.Events);
    }

    [Fact]
    public void TypeText_TooLongOrNull_IsRejected()
    {
        var session = Create();

        Assert.Equal(InputResult.InvalidValue, session.TypeText(new string('a', InputSession.MaxTextLength + 1)));
        Assert.Equal(InputResult.InvalidValue, session.TypeText(null));
        Assert.Empty(_injector.Events);
    }

    [Fact]
    public void Zoom_HoldsCtrlAroundTheWheel()
    {
        Create().Zoom(2);

        Assert.Equal(["key A2 down", "wheel 240,0", "key A2 up"], _injector.Events);
    }

    [Fact]
    public void Zoom_OutIsNegative_AndDoesNotReleaseACtrlThatIsAlreadyHeld()
    {
        var session = Create();
        session.KeyDown("ControlRight");
        _injector.Clear();

        session.Zoom(-1);

        Assert.Equal(["wheel -120,0"], _injector.Events);
        Assert.Equal(1, session.HeldCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(-11)]
    public void Zoom_InvalidSteps_AreRejected(int steps)
    {
        Assert.Equal(InputResult.InvalidValue, Create().Zoom(steps));
    }

    [Fact]
    public void ReleaseAll_ReleasesEveryKeyAndButton_AndIsRepeatable()
    {
        var session = Create();
        session.KeyDown("ControlLeft");
        session.KeyDown("ShiftLeft");
        session.ButtonDown(MouseButton.Left);
        _injector.Clear();

        session.ReleaseAll();
        session.ReleaseAll();

        Assert.Equal(["key A0 up", "key A2 up", "left up"], _injector.Events);
        Assert.Equal(0, session.HeldCount);
    }

    [Fact]
    public void ReleaseAll_WithNothingHeld_DoesNothing()
    {
        Create().ReleaseAll();

        Assert.Empty(_injector.Events);
    }

    [Fact]
    public async Task ConcurrentUse_IsSafe()
    {
        var session = Create();

        var tasks = Enumerable.Range(0, 8).Select(i => Task.Run(() =>
        {
            for (var n = 0; n < 200; n++)
            {
                session.Move(1, 1);
                session.KeyDown("Key" + (char)('A' + i));
                session.KeyUp("Key" + (char)('A' + i));
                session.ButtonClick(MouseButton.Left);
            }
        }));
        await Task.WhenAll(tasks);
        session.ReleaseAll();

        Assert.Equal(0, session.HeldCount);
    }
}
