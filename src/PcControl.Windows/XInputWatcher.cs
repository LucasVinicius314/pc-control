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

[StructLayout(LayoutKind.Sequential)]
internal struct XInputBatteryInformation
{
    public byte BatteryType;
    public byte BatteryLevel;
}

internal static partial class XInputNative
{
    /// <summary>
    /// The undocumented "ex" version of XInputGetState (ordinal 100): the only one that reports the
    /// Guide (Xbox) button. Games and browsers never see that button; this is how it is read.
    /// </summary>
    [LibraryImport("xinput1_4.dll", EntryPoint = "#100")]
    internal static partial uint XInputGetStateEx(uint userIndex, out XInputState state);

    /// <summary>Battery type and level of a controller (device type 0 is the gamepad itself).</summary>
    [LibraryImport("xinput1_4.dll")]
    internal static partial uint XInputGetBatteryInformation(uint userIndex, byte deviceType, out XInputBatteryInformation information);
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
    private static readonly TimeSpan BatteryInterval = TimeSpan.FromSeconds(30);

    /// <summary>While no battery reading is to be had yet (Windows fills its gamepad list a moment after the first look), try again soon.</summary>
    private static readonly TimeSpan BatteryRetryInterval = TimeSpan.FromSeconds(3);

    private readonly CancellationTokenSource _stop = new();
    private readonly GuideButtonDetector[] _detectors = [new(), new(), new(), new()];
    private readonly DateTimeOffset[] _nextCheck = new DateTimeOffset[4];
    private readonly bool[] _connected = new bool[4];
    private readonly DateTimeOffset[] _nextBattery = new DateTimeOffset[4];
    private readonly ControllerInfo?[] _info = new ControllerInfo?[4];
    private IReadOnlyList<ControllerInfo> _published = [];
    private Thread? _thread;

    /// <summary>Raised on the watcher's own thread, once per press of the Guide button.</summary>
    public event Action? GuidePressed;

    /// <summary>Raised when the set of connected controllers, or a battery level, changes, with all controllers as they are now.</summary>
    public event Action<IReadOnlyList<ControllerInfo>>? ControllersChanged;

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

        _ = GamepadBatteries.Read(); // the first look starts Windows filling its gamepad list
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
                    if (!_connected[slot])
                    {
                        _connected[slot] = true;
                        _nextBattery[slot] = default;
                    }

                    if (now >= _nextBattery[slot])
                    {
                        _nextBattery[slot] = now + (ReadBattery(slot) ? BatteryInterval : BatteryRetryInterval);
                    }

                    if (_detectors[slot].Update(state.Gamepad.Buttons, now))
                    {
                        GuidePressed?.Invoke();
                    }
                }
                else
                {
                    if (_connected[slot])
                    {
                        _connected[slot] = false;
                        _info[slot] = null;
                        Publish();
                    }

                    _detectors[slot].Reset();
                    _nextCheck[slot] = now + MissingCheckInterval;
                }
            }

            _stop.Token.WaitHandle.WaitOne(PollInterval);
        }
    }

    /// <summary>Reads and publishes a controller's battery. False when Windows had no report for it yet.</summary>
    private bool ReadBattery(uint slot)
    {
        ControllerInfo fromXInput;
        try
        {
            fromXInput = XInputNative.XInputGetBatteryInformation(slot, 0, out var battery) == Success
                ? ControllerInfo.FromXInput((int)slot, battery.BatteryType, battery.BatteryLevel)
                : new ControllerInfo((int)slot, ControllerPower.Unknown, null);
        }
        catch (EntryPointNotFoundException)
        {
            fromXInput = new ControllerInfo((int)slot, ControllerPower.Unknown, null);
        }

        // Windows' gamepad list has no slot numbers, so the nth connected controller gets the nth report.
        // With one controller that is exact; with several the order is the order they connected in.
        var position = Enumerable.Range(0, (int)slot).Count(i => _connected[i]);
        var readings = GamepadBatteries.Read();
        var reading = position < readings.Count ? readings[position] : null;
        _info[slot] = ControllerInfo.FromReading((int)slot, reading, fromXInput);
        Publish();
        return reading is not null;
    }

    private void Publish()
    {
        var now = _info.Where(i => i is not null).Select(i => i!).ToList();
        if (now.SequenceEqual(_published))
        {
            return;
        }

        _published = now;
        ControllersChanged?.Invoke(now);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(1));
        _stop.Dispose();
    }
}
