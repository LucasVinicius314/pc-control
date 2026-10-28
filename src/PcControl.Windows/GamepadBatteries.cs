using PcControl.Core.Input;
using Windows.Devices.Power;
using Windows.Gaming.Input;
using Windows.System.Power;

namespace PcControl.Windows;

/// <summary>
/// The batteries of the connected gamepads, from Windows' gamepad API. It is exact (a percentage, and whether
/// the controller is charging) and works for Bluetooth Xbox controllers, which the older XInput calls report as
/// disconnected. The list fills in a moment after the first look, so a reading may be missing at first.
/// </summary>
internal static class GamepadBatteries
{
    public static IReadOnlyList<BatteryReading?> Read()
    {
        try
        {
            return [.. Gamepad.Gamepads.Select(Reading)];
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static BatteryReading? Reading(Gamepad pad)
    {
        try
        {
            return pad.TryGetBatteryReport() is { } report
                ? new BatteryReading(
                    report.Status != BatteryStatus.NotPresent,
                    report.Status == BatteryStatus.Charging,
                    report.RemainingCapacityInMilliwattHours,
                    report.FullChargeCapacityInMilliwattHours)
                : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
