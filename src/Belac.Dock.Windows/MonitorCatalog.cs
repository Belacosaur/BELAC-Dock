using Belac.Dock.Core.Abstractions;
using Belac.Dock.Core.Geometry;
using Belac.Dock.Core.Models;
using Belac.Dock.Windows.Native;

namespace Belac.Dock.Windows;

public sealed class MonitorCatalog : IMonitorCatalog
{
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        NativeMethods.MonitorProc callback = (monitor, _, _, _) =>
        {
            var info = new NativeMethods.MonitorInfoEx { cbSize = MarshalSize() };
            if (!NativeMethods.GetMonitorInfo(monitor, ref info))
                return true;

            var dpi = 96;
            if (NativeMethods.GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX is >= 72 and <= 480)
                dpi = (int)dpiX;

            monitors.Add(new MonitorInfo
            {
                DeviceName = info.szDevice ?? "",
                FriendlyName = ReadFriendlyName(info.szDevice),
                Bounds = ToRect(info.rcMonitor),
                Work = ToRect(info.rcWork),
                Dpi = dpi,
                Primary = (info.dwFlags & 1) != 0
            });
            return true;
        };

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return monitors.OrderByDescending(monitor => monitor.Primary).ThenBy(monitor => monitor.Bounds.Left).ToList();
    }

    public static MonitorInfo? Find(IReadOnlyList<MonitorInfo> monitors, string? deviceName)
    {
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            var match = monitors.FirstOrDefault(monitor => string.Equals(monitor.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        return monitors.FirstOrDefault(monitor => monitor.Primary) ?? monitors.FirstOrDefault();
    }

    private static string ReadFriendlyName(string? device)
    {
        if (string.IsNullOrWhiteSpace(device))
            return "";

        var display = new NativeMethods.DisplayDevice { cb = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.DisplayDevice>() };
        return NativeMethods.EnumDisplayDevices(device, 0, ref display, 0)
            ? display.DeviceString ?? ""
            : "";
    }

    private static int MarshalSize() => System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfoEx>();

    private static PixelRect ToRect(NativeMethods.Rect rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);
}

public static class TaskbarMetrics
{
    public static bool TryGetTaskbarRect(out PixelRect rect)
    {
        rect = default;
        var hwnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var raw))
            return false;

        rect = new PixelRect(raw.Left, raw.Top, raw.Right, raw.Bottom);
        return !rect.IsEmpty;
    }

    public static long CurrentTaskbarHandle() => NativeMethods.FindWindow("Shell_TrayWnd", null).ToInt64();
}
