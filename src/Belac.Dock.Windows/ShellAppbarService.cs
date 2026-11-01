using Belac.Dock.Core.Abstractions;
using Belac.Dock.Core.Geometry;
using Belac.Dock.Core.Models;
using Belac.Dock.Windows.Native;

namespace Belac.Dock.Windows;

public sealed class ShellAppbarService : IAppbarHost
{
    private IntPtr _hwnd;
    private bool _registered;
    private bool _reserve;
    private bool _rightEdge = true;
    private int _widthPx;
    private int _applying;

    public bool IsRegistered { get; private set; }
    public PixelRect? DockBounds { get; private set; }
    public bool FullscreenActive { get; private set; }
    public uint CallbackMessage { get; private set; }
    public event EventHandler? LayoutChanged;
    public event EventHandler<bool>? FullscreenChanged;

    public void Attach(IntPtr hwnd)
    {
        _hwnd = hwnd;
        CallbackMessage = NativeMethods.RegisterWindowMessage("BELAC.Dock.AppBarCallback");
    }

    public void Apply(bool reserve, bool rightEdge, int widthPx, PixelRect workArea)
    {
        if (Interlocked.Exchange(ref _applying, 1) == 1)
            return;

        try
        {
            _reserve = reserve;
            _rightEdge = rightEdge;
            _widthPx = Math.Max(1, widthPx);
            if (_hwnd == IntPtr.Zero || workArea.IsEmpty)
                return;

            if (!reserve)
            {
                Remove();
                DockBounds = AppbarGeometry.Place(workArea, _widthPx, rightEdge);
                return;
            }

            if (CallbackMessage == 0 || (!_registered && !Register()))
            {
                DockBounds = AppbarGeometry.Place(workArea, _widthPx, rightEdge);
                return;
            }

            var data = CreateData();
            data.uEdge = (uint)(rightEdge ? NativeMethods.AbeRight : NativeMethods.AbeLeft);
            data.rc = ToNative(AppbarGeometry.Place(workArea, _widthPx, rightEdge));
            NativeMethods.SHAppBarMessage(NativeMethods.AbmQueryPos, ref data);
            if (rightEdge)
                data.rc.Left = data.rc.Right - _widthPx;
            else
                data.rc.Right = data.rc.Left + _widthPx;

            NativeMethods.SHAppBarMessage(NativeMethods.AbmSetPos, ref data);
            DockBounds = new PixelRect(data.rc.Left, data.rc.Top, data.rc.Right, data.rc.Bottom);
            IsRegistered = true;
        }
        finally
        {
            Interlocked.Exchange(ref _applying, 0);
        }
    }

    public void Remove()
    {
        if (!_registered || _hwnd == IntPtr.Zero)
        {
            _registered = false;
            IsRegistered = false;
            return;
        }

        var data = CreateData();
        NativeMethods.SHAppBarMessage(NativeMethods.AbmRemove, ref data);
        _registered = false;
        IsRegistered = false;
    }

    public void HandleCallback(IntPtr wParam, IntPtr lParam)
    {
        var code = wParam.ToInt32();
        if (code == NativeMethods.AbnPosChanged)
        {
            LayoutChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (code == NativeMethods.AbnFullscreenApp)
        {
            FullscreenActive = lParam != IntPtr.Zero;
            FullscreenChanged?.Invoke(this, FullscreenActive);
        }
    }

    public void NoteWindowPosChanged()
    {
        if (!_registered)
            return;

        var data = CreateData();
        NativeMethods.SHAppBarMessage(NativeMethods.AbmWindowPosChanged, ref data);
    }

    private bool Register()
    {
        var data = CreateData();
        var result = NativeMethods.SHAppBarMessage(NativeMethods.AbmNew, ref data);
        _registered = result != IntPtr.Zero;
        return _registered;
    }

    private NativeMethods.AppBarData CreateData() => new()
    {
        cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.AppBarData>(),
        hWnd = _hwnd,
        uCallbackMessage = CallbackMessage
    };

    private static NativeMethods.Rect ToNative(PixelRect rect) => new()
    {
        Left = rect.Left,
        Top = rect.Top,
        Right = rect.Right,
        Bottom = rect.Bottom
    };
}
