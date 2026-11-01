using System.Windows;
using System.Windows.Interop;
using Belac.Dock.Core.Geometry;
using Belac.Dock.Core.Models;
using Belac.Dock.Windows;
using Belac.Dock.Windows.Native;

namespace Belac.Dock.App.Views;

public sealed class SelfTestWindow : Window
{
    private readonly string _outputPath;
    private bool _defer;

    public SelfTestWindow(string outputPath)
    {
        _outputPath = outputPath;
        Width = 96;
        Height = 400;
        WindowStyle = WindowStyle.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        ShowActivated = false;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Title = "BELAC Dock self-test";
        SourceInitialized += (_, _) => Dispatcher.BeginInvoke(Run, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Run()
    {
        var appbar = new ShellAppbarService();
        var result = "FAIL";
        var detail = "not started";
        try
        {
            var monitors = new MonitorCatalog().GetMonitors();
            var monitor = MonitorCatalog.Find(monitors, null);
            if (monitor is null)
            {
                detail = "No monitor was found.";
                return;
            }

            var before = monitor.Work;
            var hwnd = new WindowInteropHelper(this).Handle;
            appbar.Attach(hwnd);
            var width = AppbarGeometry.DipToPx(96, monitor.Dpi);
            appbar.Apply(true, true, width, monitor.Work);
            var bounds = appbar.DockBounds;
            if (bounds is null)
            {
                detail = "Appbar did not return a dock rectangle.";
                return;
            }

            var source = HwndSource.FromHwnd(hwnd);
            var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
            var origin = fromDevice.Transform(new Point(bounds.Value.Left, bounds.Value.Top));
            var far = fromDevice.Transform(new Point(bounds.Value.Right, bounds.Value.Bottom));
            Left = origin.X;
            Top = origin.Y;
            Width = Math.Max(1, far.X - origin.X);
            Height = Math.Max(1, far.Y - origin.Y);
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndTopmost, bounds.Value.Left, bounds.Value.Top, bounds.Value.Width, bounds.Value.Height, NativeMethods.SwpShowWindow | NativeMethods.SwpNoActivate);
            _defer = true;
            var captured = monitor;
            var capturedBefore = before;
            var capturedWidth = width;
            Dispatcher.BeginInvoke(() => Finish(appbar, captured, capturedBefore, capturedWidth), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        catch (Exception ex)
        {
            detail = ex.GetType().Name + ": " + ex.Message;
        }
        finally
        {
            if (!_defer)
                Write(appbar, result, detail);
        }
    }

    private void Finish(ShellAppbarService appbar, MonitorInfo monitor, PixelRect before, int width)
    {
        var result = "FAIL";
        var detail = "not measured";
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (!NativeMethods.GetWindowRect(hwnd, out var actual))
            {
                detail = "GetWindowRect failed.";
                return;
            }

            var rightDelta = Math.Abs(actual.Right - monitor.Work.Right);
            var widthDelta = Math.Abs((actual.Right - actual.Left) - width);
            if (!appbar.IsRegistered)
            {
                detail = "SHAppBarMessage did not register the dock.";
                return;
            }

            if (rightDelta > 8 || widthDelta > 12)
            {
                detail = $"Dock rect {actual.Left},{actual.Top},{actual.Right},{actual.Bottom} did not match work right {monitor.Work.Right} width {width}.";
                return;
            }

            appbar.Remove();
            var after = MonitorCatalog.Find(new MonitorCatalog().GetMonitors(), monitor.DeviceName);
            if (after is null)
            {
                detail = "Monitor disappeared after unregister.";
                return;
            }

            if (Math.Abs(after.Work.Right - before.Right) > 8)
            {
                detail = $"Work area right stayed at {after.Work.Right}; expected about {before.Right}.";
                return;
            }

            result = "PASS";
            detail = $"bounds={actual.Left},{actual.Top},{actual.Right},{actual.Bottom}; workRight={before.Right}; dpi={monitor.Dpi}; registered-then-removed";
        }
        catch (Exception ex)
        {
            detail = ex.GetType().Name + ": " + ex.Message;
        }
        finally
        {
            Write(appbar, result, detail);
        }
    }

    private void Write(ShellAppbarService appbar, string result, string detail)
    {
        try { appbar.Remove(); } catch (Exception) { }
        try
        {
            var directory = Path.GetDirectoryName(_outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(_outputPath, result + Environment.NewLine + detail);
        }
        catch (Exception)
        {
        }

        Environment.Exit(result == "PASS" ? 0 : 1);
    }
}
