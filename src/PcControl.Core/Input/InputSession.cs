namespace PcControl.Core.Input;

public enum InputResult
{
    Ok,
    UnknownKey,
    InvalidValue,
    TooManyHeldKeys,
}

/// <summary>
/// The input state of one connected phone. It validates every request, remembers what is held down
/// and can release all of it, so a dropped connection never leaves a key or button stuck.
/// </summary>
public sealed class InputSession(IInputInjector injector, PointerCurve curve)
{
    public const int MaxDelta = 2000;
    public const int MaxWheel = 1200;
    public const int MaxZoomSteps = 10;
    public const int MaxTextLength = 256;
    public const int MaxHeldKeys = 16;
    public const double MinSensitivity = 0.25;
    public const double MaxSensitivity = 4;
    public const int WheelNotch = 120;

    private const string ControlLeft = "ControlLeft";

    private readonly object _gate = new();
    private readonly Dictionary<string, KeyInfo> _heldKeys = new(StringComparer.Ordinal);
    private readonly HashSet<MouseButton> _heldButtons = [];
    private double _sensitivity = 1;
    private double _remainderX;
    private double _remainderY;

    public double Sensitivity
    {
        get
        {
            lock (_gate)
            {
                return _sensitivity;
            }
        }
    }

    public int HeldCount
    {
        get
        {
            lock (_gate)
            {
                return _heldKeys.Count + _heldButtons.Count;
            }
        }
    }

    public InputResult SetSensitivity(double value)
    {
        if (double.IsNaN(value) || value < MinSensitivity || value > MaxSensitivity)
        {
            return InputResult.InvalidValue;
        }

        lock (_gate)
        {
            _sensitivity = value;
        }

        return InputResult.Ok;
    }

    /// <summary>Moves the cursor by a finger movement in phone pixels, after the curve and sensitivity.</summary>
    public InputResult Move(int dx, int dy)
    {
        if (Math.Abs(dx) > MaxDelta || Math.Abs(dy) > MaxDelta)
        {
            return InputResult.InvalidValue;
        }

        lock (_gate)
        {
            var multiplier = curve.Multiplier(Math.Sqrt((double)dx * dx + (double)dy * dy)) * _sensitivity;

            // Keep the fractional part so slow movement is not lost to rounding.
            var x = (dx * multiplier) + _remainderX;
            var y = (dy * multiplier) + _remainderY;
            var wholeX = (int)Math.Truncate(x);
            var wholeY = (int)Math.Truncate(y);
            _remainderX = x - wholeX;
            _remainderY = y - wholeY;

            if (wholeX != 0 || wholeY != 0)
            {
                injector.MoveRelative(wholeX, wholeY);
            }
        }

        return InputResult.Ok;
    }

    public InputResult Scroll(int vertical, int horizontal)
    {
        if (Math.Abs(vertical) > MaxWheel || Math.Abs(horizontal) > MaxWheel)
        {
            return InputResult.InvalidValue;
        }

        if (vertical != 0 || horizontal != 0)
        {
            lock (_gate)
            {
                injector.Wheel(vertical, horizontal);
            }
        }

        return InputResult.Ok;
    }

    public void ButtonDown(MouseButton button)
    {
        lock (_gate)
        {
            if (_heldButtons.Add(button))
            {
                injector.MouseButton(button, down: true);
            }
        }
    }

    public void ButtonUp(MouseButton button)
    {
        lock (_gate)
        {
            if (_heldButtons.Remove(button))
            {
                injector.MouseButton(button, down: false);
            }
        }
    }

    public void ButtonClick(MouseButton button)
    {
        lock (_gate)
        {
            if (_heldButtons.Contains(button))
            {
                return;
            }

            injector.MouseButton(button, down: true);
            injector.MouseButton(button, down: false);
        }
    }

    public InputResult KeyDown(string? code)
    {
        if (!KeyCatalog.TryGet(code, out var key))
        {
            return InputResult.UnknownKey;
        }

        lock (_gate)
        {
            if (_heldKeys.ContainsKey(code!))
            {
                return InputResult.Ok;
            }

            if (_heldKeys.Count >= MaxHeldKeys)
            {
                return InputResult.TooManyHeldKeys;
            }

            _heldKeys[code!] = key;
            injector.Key(key, down: true);
        }

        return InputResult.Ok;
    }

    public InputResult KeyUp(string? code)
    {
        if (!KeyCatalog.TryGet(code, out var key))
        {
            return InputResult.UnknownKey;
        }

        lock (_gate)
        {
            if (_heldKeys.Remove(code!))
            {
                injector.Key(key, down: false);
            }
        }

        return InputResult.Ok;
    }

    /// <summary>Presses and releases a key. Modifiers already held stay held.</summary>
    public InputResult KeyPress(string? code)
    {
        if (!KeyCatalog.TryGet(code, out var key))
        {
            return InputResult.UnknownKey;
        }

        lock (_gate)
        {
            // A held key pressed again is a repeat: release it so the press is clean.
            if (_heldKeys.Remove(code!))
            {
                injector.Key(key, down: false);
            }

            injector.Key(key, down: true);
            injector.Key(key, down: false);
        }

        return InputResult.Ok;
    }

    /// <summary>
    /// Types text. New lines become Enter and tabs become Tab; other control characters are dropped.
    /// </summary>
    public InputResult TypeText(string? text)
    {
        if (text is null || text.Length > MaxTextLength)
        {
            return InputResult.InvalidValue;
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        lock (_gate)
        {
            var run = new System.Text.StringBuilder();
            foreach (var c in normalized)
            {
                if (c is '\n' or '\t')
                {
                    FlushRun(run);
                    KeyPressLocked(c == '\n' ? "Enter" : "Tab");
                }
                else if (!char.IsControl(c))
                {
                    run.Append(c);
                }
            }

            FlushRun(run);
        }

        return InputResult.Ok;
    }

    /// <summary>Zooms with Ctrl + mouse wheel. Positive steps zoom in.</summary>
    public InputResult Zoom(int steps)
    {
        if (steps == 0 || Math.Abs(steps) > MaxZoomSteps)
        {
            return InputResult.InvalidValue;
        }

        lock (_gate)
        {
            var ctrlAlreadyHeld = _heldKeys.Keys.Any(k => k.StartsWith("Control", StringComparison.Ordinal));
            KeyCatalog.TryGet(ControlLeft, out var ctrl);

            if (!ctrlAlreadyHeld)
            {
                injector.Key(ctrl, down: true);
            }

            injector.Wheel(steps * WheelNotch, 0);

            if (!ctrlAlreadyHeld)
            {
                injector.Key(ctrl, down: false);
            }
        }

        return InputResult.Ok;
    }

    /// <summary>Releases every held key and mouse button. Safe to call repeatedly and from any thread.</summary>
    public void ReleaseAll()
    {
        lock (_gate)
        {
            foreach (var key in _heldKeys.Values.Reverse())
            {
                injector.Key(key, down: false);
            }

            foreach (var button in _heldButtons)
            {
                injector.MouseButton(button, down: false);
            }

            _heldKeys.Clear();
            _heldButtons.Clear();
            _remainderX = 0;
            _remainderY = 0;
        }
    }

    private void FlushRun(System.Text.StringBuilder run)
    {
        if (run.Length == 0)
        {
            return;
        }

        injector.TypeText(run.ToString());
        run.Clear();
    }

    private void KeyPressLocked(string code)
    {
        KeyCatalog.TryGet(code, out var key);
        injector.Key(key, down: true);
        injector.Key(key, down: false);
    }
}
