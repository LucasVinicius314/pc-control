namespace PcControl.Core.Input;

/// <summary>Where a controller gets its power from.</summary>
public enum ControllerPower
{
    Unknown,
    Wired,
    Battery,
}

/// <summary>How full a controller's battery is. Windows only reports these four steps.</summary>
public enum BatteryLevel
{
    Empty,
    Low,
    Medium,
    Full,
}

/// <summary>What Windows' gamepad API says about a controller's battery: whether there is one, if it is charging, and its charge in the units the controller uses.</summary>
public sealed record BatteryReading(bool Present, bool Charging, int? Remaining, int? Full);

/// <summary>
/// One connected controller: which of the four slots, and what it runs on. <paramref name="Percent"/> is the
/// exact charge when Windows gives it; <paramref name="Level"/> is the same in four steps (or the only thing
/// the older XInput interface can say).
/// </summary>
public sealed record ControllerInfo(int Slot, ControllerPower Power, BatteryLevel? Level, int? Percent = null, bool Charging = false)
{
    /// <summary>
    /// Prefers Windows' gamepad report (exact, and it works for Bluetooth controllers, which XInput calls
    /// "disconnected"); without one, what XInput said stands.
    /// </summary>
    public static ControllerInfo FromReading(int slot, BatteryReading? reading, ControllerInfo xinput)
    {
        ArgumentNullException.ThrowIfNull(xinput);
        if (reading is null)
        {
            return xinput;
        }

        // A controller that is connected but has no battery is on a cable.
        if (!reading.Present)
        {
            return new ControllerInfo(slot, ControllerPower.Wired, null, null, true);
        }

        int? percent = reading.Remaining is { } remaining && reading.Full is > 0 and var full
            ? Math.Clamp((int)Math.Round(remaining * 100.0 / full), 0, 100)
            : null;
        BatteryLevel? level = percent switch
        {
            null => xinput.Level,
            >= 66 => BatteryLevel.Full,
            >= 33 => BatteryLevel.Medium,
            >= 12 => BatteryLevel.Low,
            _ => BatteryLevel.Empty,
        };
        return new ControllerInfo(slot, ControllerPower.Battery, level, percent, reading.Charging);
    }

    /// <summary>Reads what XInput reports: a battery type (1 wired, 2 alkaline, 3 rechargeable, 255 unknown) and a level (0 to 3).</summary>
    public static ControllerInfo FromXInput(int slot, byte batteryType, byte batteryLevel) => batteryType switch
    {
        1 => new ControllerInfo(slot, ControllerPower.Wired, null),
        2 or 3 => new ControllerInfo(slot, ControllerPower.Battery, batteryLevel switch
        {
            0 => BatteryLevel.Empty,
            1 => BatteryLevel.Low,
            2 => BatteryLevel.Medium,
            3 => BatteryLevel.Full,
            _ => null,
        }),
        _ => new ControllerInfo(slot, ControllerPower.Unknown, null),
    };
}

/// <summary>The controllers that are plugged in or connected right now, kept up to date by the watcher and read by the web server.</summary>
public sealed class ControllerStatus
{
    private volatile IReadOnlyList<ControllerInfo> _controllers = [];

    public IReadOnlyList<ControllerInfo> Controllers
    {
        get => _controllers;
        set => _controllers = value;
    }
}
