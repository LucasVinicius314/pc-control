namespace PcControl.Core.Input;

/// <summary>
/// Turns the controller's button state, read many times a second, into "the Guide (Xbox) button was
/// just pressed", once per press. Holding the button does not repeat, and a quick tremble of the
/// reading (released and pressed again within the debounce time) counts as one press.
/// </summary>
public sealed class GuideButtonDetector(TimeSpan? debounce = null)
{
    public const ushort GuideMask = 0x0400;

    private readonly TimeSpan _debounce = debounce ?? TimeSpan.FromMilliseconds(250);
    private bool _down;
    private DateTimeOffset _lastPress = DateTimeOffset.MinValue;

    /// <summary>Feed the latest reading; true exactly when a new press starts.</summary>
    public bool Update(ushort buttons, DateTimeOffset now)
    {
        var down = (buttons & GuideMask) != 0;
        var pressed = down && !_down && now - _lastPress >= _debounce;
        if (pressed)
        {
            _lastPress = now;
        }

        _down = down;
        return pressed;
    }

    /// <summary>Forget the held state, for example when the controller is unplugged.</summary>
    public void Reset() => _down = false;
}
