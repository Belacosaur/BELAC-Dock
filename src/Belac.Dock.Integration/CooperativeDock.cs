using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Belac.Dock.Integration;

public static class CooperativeDock
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExAppWindow = 0x00040000;
    private const uint FrameChanged = 0x0020 | 0x0002 | 0x0001 | 0x0004 | 0x0010;

    public static void HideFromTaskbar(Window window) => Apply(window, hide: true);

    public static void ShowInTaskbar(Window window) => Apply(window, hide: false);

    private static void Apply(Window window, bool hide)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var style = GetWindowLongPtr(hwnd, GwlExStyle);
        var updated = hide
            ? (style | WsExToolWindow) & ~WsExAppWindow
            : (style & ~WsExToolWindow) | WsExAppWindow;
        SetWindowLongPtr(hwnd, GwlExStyle, updated);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, FrameChanged);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
