using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PcControl.Core.Input;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace PcControl.Windows;

/// <summary>Puts input on the PC with SendInput.</summary>
public sealed class SendInputInjector(ILogger<SendInputInjector> logger) : IInputInjector
{
    private static readonly TimeSpan WarnInterval = TimeSpan.FromSeconds(30);
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    private long _lastWarnTicks;

    public void MoveRelative(int dx, int dy) =>
        SendOne(Mouse(MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE, dx, dy, 0));

    public void MouseButton(Core.Input.MouseButton button, bool down)
    {
        var flags = (button, down) switch
        {
            (Core.Input.MouseButton.Left, true) => MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN,
            (Core.Input.MouseButton.Left, false) => MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP,
            (Core.Input.MouseButton.Right, true) => MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN,
            (Core.Input.MouseButton.Right, false) => MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP,
            (Core.Input.MouseButton.Middle, true) => MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEDOWN,
            _ => MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEUP,
        };

        SendOne(Mouse(flags, 0, 0, 0));
    }

    public void Wheel(int vertical, int horizontal)
    {
        if (vertical != 0)
        {
            SendOne(Mouse(MOUSE_EVENT_FLAGS.MOUSEEVENTF_WHEEL, 0, 0, vertical));
        }

        if (horizontal != 0)
        {
            SendOne(Mouse(MOUSE_EVENT_FLAGS.MOUSEEVENTF_HWHEEL, 0, 0, horizontal));
        }
    }

    public void Key(KeyInfo key, bool down)
    {
        var flags = down ? 0 : KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
        if (key.Extended)
        {
            flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY;
        }

        // Some programs (emulators, games) read the scan code, so send it along with the virtual key.
        var scan = (ushort)PInvoke.MapVirtualKey(key.VirtualKey, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_VSC);
        SendOne(Keyboard((VIRTUAL_KEY)key.VirtualKey, scan, flags));
    }

    public void TypeText(string text)
    {
        // Each UTF-16 code unit goes as its own down/up pair, so characters outside the basic plane
        // (emoji) arrive as the surrogate pair Windows expects.
        var inputs = new INPUT[text.Length * 2];
        for (var i = 0; i < text.Length; i++)
        {
            inputs[2 * i] = Keyboard(0, text[i], KEYBD_EVENT_FLAGS.KEYEVENTF_UNICODE);
            inputs[(2 * i) + 1] = Keyboard(
                0,
                text[i],
                KEYBD_EVENT_FLAGS.KEYEVENTF_UNICODE | KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP);
        }

        Send(inputs);
    }

    private static INPUT Mouse(MOUSE_EVENT_FLAGS flags, int dx, int dy, int data) => new()
    {
        type = INPUT_TYPE.INPUT_MOUSE,
        Anonymous = new INPUT._Anonymous_e__Union
        {
            mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = unchecked((uint)data), dwFlags = flags },
        },
    };

    private static INPUT Keyboard(VIRTUAL_KEY vk, ushort scan, KEYBD_EVENT_FLAGS flags) => new()
    {
        type = INPUT_TYPE.INPUT_KEYBOARD,
        Anonymous = new INPUT._Anonymous_e__Union
        {
            ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags },
        },
    };

    private void SendOne(INPUT input) => Send([input]);

    private void Send(INPUT[] inputs)
    {
        var sent = PInvoke.SendInput(inputs, InputSize);
        if (sent == inputs.Length)
        {
            return;
        }

        // Windows refuses input aimed at elevated windows (UIPI), the lock screen and UAC prompts.
        // That is expected, so warn at most every 30 seconds instead of on every mouse move.
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastWarnTicks);
        if (now - last >= WarnInterval.TotalMilliseconds && Interlocked.CompareExchange(ref _lastWarnTicks, now, last) == last)
        {
            logger.LogWarning(
                "Windows accepted {Sent} of {Count} input events (error {Error}). The foreground window may be elevated, or the screen is locked.",
                sent,
                inputs.Length,
                Marshal.GetLastWin32Error());
        }
    }
}
