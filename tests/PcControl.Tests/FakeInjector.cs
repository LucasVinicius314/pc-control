using PcControl.Core.Input;

namespace PcControl.Tests;

/// <summary>Records what would have been sent to Windows, as readable strings.</summary>
public sealed class FakeInjector : IInputInjector
{
    private readonly List<string> _events = [];
    private readonly object _gate = new();

    public IReadOnlyList<string> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    public void MoveRelative(int dx, int dy) => Add($"move {dx},{dy}");

    public void MouseButton(MouseButton button, bool down) => Add($"{button.ToString().ToLowerInvariant()} {(down ? "down" : "up")}");

    public void Wheel(int vertical, int horizontal) => Add($"wheel {vertical},{horizontal}");

    public void Key(KeyInfo key, bool down) => Add($"key {key.VirtualKey:X2} {(down ? "down" : "up")}");

    public void TypeText(string text) => Add($"text {text}");

    public void Clear()
    {
        lock (_gate)
        {
            _events.Clear();
        }
    }

    /// <summary>Waits until an event matching <paramref name="expected"/> arrives (input is applied on the server thread).</summary>
    public async Task WaitForAsync(string expected, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (Events.Contains(expected))
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"Timed out waiting for '{expected}'. Got: [{string.Join(" | ", Events)}]");
    }

    private void Add(string value)
    {
        lock (_gate)
        {
            _events.Add(value);
        }
    }
}
