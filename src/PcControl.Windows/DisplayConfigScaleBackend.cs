using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PcControl.Core.Capture;
using PcControl.Core.Display;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;

namespace PcControl.Windows;

/// <summary>
/// Reads and changes a monitor's scale with the display-configuration calls Windows' own Settings
/// app uses. The two request types involved are not documented by Microsoft; their numbers (-3 to
/// read, -4 to set) and layouts come from the open-source SetDPI / windows-DPI-scaling-sample
/// projects and are checked on this machine by the startup self-test. A Windows update can change
/// them, which is why <see cref="DpiToggleService"/> tests before relying on this.
/// </summary>
public sealed class DisplayConfigScaleBackend(ILogger<DisplayConfigScaleBackend> logger) : IDisplayScaleBackend
{
    // Request types that Microsoft has not put in the SDK headers.
    private const int GetDpiScale = -3;
    private const int SetDpiScale = -4;

    // Sizes the original source asserts; a mismatch means these layouts are wrong, so do nothing.
    private const int HeaderSize = 20;
    private const int GetPacketSize = 0x20;
    private const int SetPacketSize = 0x18;

    public string Name => "display-config";

    public bool CanSet => true;

    [StructLayout(LayoutKind.Sequential)]
    private struct GetPacket
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER Header;
        public int MinScaleRel;
        public int CurScaleRel;
        public int MaxScaleRel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SetPacket
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER Header;
        public int ScaleRel;
    }

    public unsafe ScaleInfo? ReadScale(MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        if (!LayoutsMatch() || FindSource(monitor.Name) is not { } source)
        {
            return null;
        }

        var packet = new GetPacket();
        packet.Header.type = (DISPLAYCONFIG_DEVICE_INFO_TYPE)GetDpiScale;
        packet.Header.size = (uint)sizeof(GetPacket);
        packet.Header.adapterId = source.Adapter;
        packet.Header.id = source.Id;

        var result = PInvoke.DisplayConfigGetDeviceInfo(&packet.Header);
        if (result != 0)
        {
            logger.LogWarning("Windows refused the display scale read for {Monitor} (error {Error}).", monitor.Name, result);
            return null;
        }

        return DpiScaleTable.TryRead(packet.MinScaleRel, packet.CurScaleRel, packet.MaxScaleRel, out var info)
            ? info
            : null;
    }

    public unsafe bool ApplyScale(MonitorInfo monitor, int percent)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        // Steps are counted from the recommended scale, so read that first.
        if (!LayoutsMatch()
            || FindSource(monitor.Name) is not { } source
            || ReadScale(monitor) is not { Recommended: { } recommended }
            || !DpiScaleTable.TryToRelative(percent, recommended, out var relative))
        {
            return false;
        }

        var packet = new SetPacket { ScaleRel = relative };
        packet.Header.type = (DISPLAYCONFIG_DEVICE_INFO_TYPE)SetDpiScale;
        packet.Header.size = (uint)sizeof(SetPacket);
        packet.Header.adapterId = source.Adapter;
        packet.Header.id = source.Id;

        var result = PInvoke.DisplayConfigSetDeviceInfo(&packet.Header);
        if (result != 0)
        {
            logger.LogWarning("Windows refused to set the display scale of {Monitor} to {Percent}% (error {Error}).", monitor.Name, percent, result);
        }

        return result == 0;
    }

    private static unsafe bool LayoutsMatch() =>
        sizeof(DISPLAYCONFIG_DEVICE_INFO_HEADER) == HeaderSize
        && sizeof(GetPacket) == GetPacketSize
        && sizeof(SetPacket) == SetPacketSize;

    /// <summary>Finds the adapter and source id Windows uses for a monitor, from its GDI name (\\.\DISPLAY1).</summary>
    private static unsafe (LUID Adapter, uint Id)? FindSource(string gdiName)
    {
        var flags = QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS;
        if (PInvoke.GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount) != WIN32_ERROR.ERROR_SUCCESS)
        {
            return null;
        }

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

        // The topology argument must be left out when asking only for the active paths.
        if (PInvoke.QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes) != WIN32_ERROR.ERROR_SUCCESS)
        {
            return null;
        }

        for (var i = 0; i < pathCount; i++)
        {
            var source = paths[i].sourceInfo;
            var name = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
            name.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
            name.header.size = (uint)sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME);
            name.header.adapterId = source.adapterId;
            name.header.id = source.id;

            if (PInvoke.DisplayConfigGetDeviceInfo(&name.header) == 0
                && string.Equals(name.viewGdiDeviceName.ToString(), gdiName, StringComparison.OrdinalIgnoreCase))
            {
                return (source.adapterId, source.id);
            }
        }

        return null;
    }
}
