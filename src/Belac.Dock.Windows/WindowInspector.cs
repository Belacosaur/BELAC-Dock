using Belac.Dock.Core.Abstractions;
using Belac.Dock.Core.Models;
using Belac.Dock.Windows.Native;

namespace Belac.Dock.Windows;

public sealed class WindowInspector : IWindowInspector
{
    public bool TryInspect(long hwndValue, out WindowObservation observation)
    {
        observation = new WindowObservation();
        var hwnd = new IntPtr(hwndValue);
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
            return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pidValue);
        var pid = (int)pidValue;
        var facts = ProcessFacts.Query(pid);
        var exStyle = NativeMethods.ReadExStyle(hwnd);
        observation = new WindowObservation
        {
            Hwnd = hwndValue,
            ProcessId = pid,
            ProcessStartUtc = facts.StartUtc,
            ExecutablePath = facts.ExecutablePath,
            ProcessName = facts.Name,
            Aumid = WindowAppIdentity.Read(hwnd) ?? facts.Aumid ?? ShellAppIdentity.Resolve(hwnd, pid, facts.StartUtc) ?? DesktopAppIdentity.Find(facts.ExecutablePath),
            Title = NativeMethods.ReadTitle(hwnd),
            ClassName = NativeMethods.ReadClass(hwnd),
            IsVisible = NativeMethods.IsWindowVisible(hwnd),
            IsMinimized = NativeMethods.IsIconic(hwnd),
            IsCloaked = IsCloaked(hwnd),
            HasOwner = NativeMethods.GetWindow(hwnd, NativeMethods.GwOwner) != IntPtr.Zero,
            IsToolWindow = (exStyle & NativeMethods.WsExToolWindow) != 0,
            IsAppWindow = (exStyle & NativeMethods.WsExAppWindow) != 0,
            AccessDenied = facts.Denied,
            MonitorDeviceName = MonitorName(hwnd)
        };
        return true;
    }

    public static bool IsCloaked(IntPtr hwnd)
    {
        var result = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaCloaked, out var cloaked, sizeof(int));
        return result == 0 && cloaked != 0;
    }

    private static string MonitorName(IntPtr hwnd)
    {
        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return "";

        var info = new NativeMethods.MonitorInfoEx
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfoEx>()
        };
        return NativeMethods.GetMonitorInfo(monitor, ref info) ? info.szDevice ?? "" : "";
    }
}

internal readonly record struct ProcessFacts(DateTime StartUtc, string ExecutablePath, string Name, string? Aumid, bool Denied)
{
    public static ProcessFacts Query(int processId)
    {
        if (processId <= 0)
            return new ProcessFacts(DateTime.UnixEpoch, "", "", null, true);

        const uint queryLimited = 0x1000;
        var handle = NativeMethods.OpenProcess(queryLimited, false, (uint)processId);
        if (handle == IntPtr.Zero)
            return new ProcessFacts(DateTime.UnixEpoch, "", FallbackName(processId), null, true);

        try
        {
            var start = DateTime.UnixEpoch;
            if (NativeMethods.GetProcessTimes(handle, out var created, out _, out _, out _) && created > 0)
                start = DateTime.FromFileTimeUtc(created);

            var path = "";
            var buffer = new System.Text.StringBuilder(1024);
            var size = buffer.Capacity;
            if (NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size))
                path = buffer.ToString();

            return new ProcessFacts(start, path, NameFrom(path, processId), ReadAumid(handle), false);
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static string? ReadAumid(IntPtr handle)
    {
        var length = 0;
        var needed = NativeMethods.GetApplicationUserModelId(handle, ref length, null);
        if (needed != 0 && (length <= 1 || length > 1024))
            return null;

        var buffer = new System.Text.StringBuilder(Math.Max(length, 2));
        length = buffer.Capacity;
        return NativeMethods.GetApplicationUserModelId(handle, ref length, buffer) == 0
            ? buffer.ToString()
            : null;
    }

    private static string NameFrom(string path, int processId) =>
        string.IsNullOrWhiteSpace(path) ? FallbackName(processId) : System.IO.Path.GetFileNameWithoutExtension(path);

    private static string FallbackName(int processId)
    {
        try
        {
            return System.Diagnostics.Process.GetProcessById(processId).ProcessName;
        }
        catch
        {
            return "";
        }
    }
}

public sealed class WindowController : IWindowController
{
    public string? LastFailure { get; private set; }

    public bool TryActivate(long hwndValue)
    {
        LastFailure = null;
        var hwnd = new IntPtr(hwndValue);
        if (!NativeMethods.IsWindow(hwnd))
        {
            LastFailure = "That window is no longer open.";
            return false;
        }

        if (NativeMethods.IsIconic(hwnd))
            NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SwRestore);

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        NativeMethods.AllowSetForegroundWindow((int)processId);
        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var targetThread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        var current = NativeMethods.GetCurrentThreadId();
        var attachedForeground = false;
        var attachedTarget = false;
        try
        {
            if (foregroundThread != 0 && foregroundThread != current)
                attachedForeground = NativeMethods.AttachThreadInput(current, foregroundThread, true);
            if (targetThread != 0 && targetThread != current && targetThread != foregroundThread)
                attachedTarget = NativeMethods.AttachThreadInput(current, targetThread, true);

            NativeMethods.BringWindowToTop(hwnd);
            NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attachedForeground)
                NativeMethods.AttachThreadInput(current, foregroundThread, false);
            if (attachedTarget)
                NativeMethods.AttachThreadInput(current, targetThread, false);
        }

        if (NativeMethods.GetForegroundWindow() == hwnd)
            return true;

        var flash = new NativeMethods.FlashInfo
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.FlashInfo>(),
            hwnd = hwnd,
            dwFlags = NativeMethods.FlashTray | NativeMethods.FlashTimerNoForeground,
            uCount = 3,
            dwTimeout = 0
        };
        NativeMethods.FlashWindowEx(ref flash);
        LastFailure = "Windows did not allow BELAC Dock to take focus. The window was flashed instead.";
        return false;
    }

    public bool TryMinimize(long hwndValue)
    {
        LastFailure = null;
        var hwnd = new IntPtr(hwndValue);
        if (!NativeMethods.IsWindow(hwnd))
        {
            LastFailure = "That window is no longer open.";
            return false;
        }

        return NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SwMinimize);
    }

    public bool TryClose(long hwndValue)
    {
        LastFailure = null;
        var hwnd = new IntPtr(hwndValue);
        if (!NativeMethods.IsWindow(hwnd))
        {
            LastFailure = "That window is no longer open.";
            return false;
        }

        if (NativeMethods.PostMessage(hwnd, NativeMethods.WmClose, IntPtr.Zero, IntPtr.Zero))
            return true;

        LastFailure = "Windows did not accept the close request.";
        return false;
    }
}
