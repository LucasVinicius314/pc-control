namespace PcControl.Core.Input;

public enum MouseButton
{
    Left,
    Right,
    Middle,
}

/// <summary>A key as Windows wants it: a virtual-key code plus the "extended key" flag.</summary>
public readonly record struct KeyInfo(ushort VirtualKey, bool Extended);

/// <summary>
/// Puts input on the PC. The Windows implementation uses SendInput; tests use a fake. Callers
/// (<see cref="InputSession"/>) are responsible for validating values and tracking held state.
/// </summary>
public interface IInputInjector
{
    /// <summary>Moves the cursor by whole pixels relative to where it is.</summary>
    void MoveRelative(int dx, int dy);

    void MouseButton(MouseButton button, bool down);

    /// <summary>Wheel movement in wheel units (120 is one notch). Positive is up / right.</summary>
    void Wheel(int vertical, int horizontal);

    void Key(KeyInfo key, bool down);

    /// <summary>Types text as Unicode characters, independent of the PC's keyboard layout.</summary>
    void TypeText(string text);
}
