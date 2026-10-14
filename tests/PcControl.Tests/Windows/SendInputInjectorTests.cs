using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using PcControl.Core.Input;
using PcControl.Windows;

namespace PcControl.Tests.Windows;

/// <summary>
/// Talks to the real Windows input system, so it moves the actual mouse. It is off by default.
/// Run it with: <c>$env:PCCONTROL_REAL_INPUT = "1"; dotnet test --filter SendInputInjectorTests</c>
/// </summary>
public static partial class SendInputInjectorTests
{
    private const string EnableVariable = "PCCONTROL_REAL_INPUT";

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    private static bool Enabled => Environment.GetEnvironmentVariable(EnableVariable) == "1";

    [Fact]
    public static async Task RealMouseMove_MovesTheCursor_ThenRestoresIt()
    {
        if (!Enabled)
        {
            return;
        }

        var injector = new SendInputInjector(NullLogger<SendInputInjector>.Instance);
        Assert.True(GetCursorPos(out var before));
        try
        {
            injector.MoveRelative(80, 0);
            await Task.Delay(100);
            Assert.True(GetCursorPos(out var after));

            // Windows may scale relative moves (pointer speed, "enhance pointer precision"), so only
            // the direction and rough size are asserted.
            Assert.InRange(after.X - before.X, 1, 400);
            Assert.InRange(Math.Abs(after.Y - before.Y), 0, 2);
        }
        finally
        {
            SetCursorPos(before.X, before.Y);
        }
    }

    [Fact]
    public static void RealKeyAndButtonCalls_DoNotThrow_AndPerMonitorDpiIsReported()
    {
        if (!Enabled)
        {
            return;
        }

        var injector = new SendInputInjector(NullLogger<SendInputInjector>.Instance);

        // Shift alone and a zero-length wheel are harmless wherever focus happens to be.
        KeyCatalog.TryGet("ShiftLeft", out var shift);
        injector.Key(shift, down: true);
        injector.Key(shift, down: false);
        injector.Wheel(0, 0);

        // The test host is not per-monitor aware; this only proves the call works.
        _ = DpiAwareness.IsPerMonitorV2();
    }
}
