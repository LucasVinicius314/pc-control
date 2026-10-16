using System.Runtime.InteropServices;
using PcControl.Core.Input;

namespace PcControl.Windows;

[StructLayout(LayoutKind.Sequential)]
internal struct XInputGamepad
{
    public ushort Buttons;
    public byte LeftTrigger;
    public byte RightTrigger;
    public short ThumbLX;
    public short ThumbLY;
    public short ThumbRX;
    public short ThumbRY;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XInputState
{
    public uint PacketNumber;
    public XInputGamepad Gamepad;
}

internal static partial class XInputNative
{
    /// <summary>
    /// The undocumented "ex" version of XInputGetState (ordinal 100): the only one that reports the
    /// Guide (Xbox) button. Games and browsers never see that button; this is how it is read.
    /// </summary>
    [LibraryImport("xinput1_4.dll", EntryPoint = "#100")]
    internal static partial uint XInputGetStateEx(uint userIndex, out XInputState state);
}

/// <summary>
/// Watches Xbox controllers in the background and says when the Guide button is pressed, even while a
/// game has the focus. Reading is cheap; controllers that are not plugged in are only looked for now
/// and then, because asking for a missing one is slow.
/// </summary>
public sealed class XInputWatcher : IDisposable
{
    private const uint Success = 0;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan MissingCheckInterval = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource _stop = new();
    private readonly GuideButtonDetector[] _detectors = [new(), new(), new(), new()];
    private readonly DateTimeOffset[] _nextCheck = new DateTimeOffset[4];
    private Thread? _thread;

    /// <summary>Raised on the watcher's own thread, once per press of the Guide button.</summary>
    public event Action? GuidePressed;

    /// <summary>False if the controller library could not be used (nothing to watch then).</summary>
    public bool Start()
    {
        try
        {
            _ = XInputNative.XInputGetStateEx(0, out _);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "Controller watcher" };
        _thread.Start();
        return true;
    }

    private void Run()
    {
        while (!_stop.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            for (uint slot = 0; slot < 4; slot++)
            {
                if (now < _nextCheck[slot])
                {
                    continue;
                }

                if (XInputNative.XInputGetStateEx(slot, out var state) == Success)
                {
                    if (_detectors[slot].Update(state.Gamepad.Buttons, now))
                    {
                        GuidePressed?.Invoke();
                    }
                }
                else
                {
                    _detectors[slot].Reset();
                    _nextCheck[slot] = now + MissingCheckInterval;
                }
            }

            _stop.Token.WaitHandle.WaitOne(PollInterval);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(1));
        _stop.Dispose();
    }
}
