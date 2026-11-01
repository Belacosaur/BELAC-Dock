using System.Text.Json;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Belac.Dock.Core.Settings;
using Belac.Dock.Windows;
using Belac.Dock.Windows.Native;

namespace Belac.Dock.App.Views;

// Presentation-only state: deliberately separate from the shared backend settings schema.
public partial class DockWindow
{
    private sealed record FloatingLayout(double X, double Y, double Width, double Height, int Version = 0);
    private FloatingLayout? _floating;
    private bool _compact;
    private bool _hiddenByUser;
    private readonly DispatcherTimer _fullscreenTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private bool _interacting;
    private string? _assignedMonitor;
    private bool _rightEdge;
    private double _settingWidth;
    private readonly DispatcherTimer _layoutSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private static string LayoutPath => Path.Combine(DockPaths.Root, "floating-layout.json");

    private void InitializeFloatingLayout()
    {
        try
        {
            var saved = JsonSerializer.Deserialize<FloatingLayout>(File.ReadAllText(LayoutPath));
            if (saved is not null && double.IsFinite(saved.X) && double.IsFinite(saved.Y)
                && double.IsFinite(saved.Width) && double.IsFinite(saved.Height)
                && saved.Width >= 76 && saved.Height >= 400 && saved.Version >= 4)
                _floating = saved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        if (_floating is null && _host.Session.Current.Edge != "Left")
        {
            _host.Session.Current.Edge = "Left";
            _host.Session.Save();
        }
        _assignedMonitor = _host.Session.Current.AssignedMonitorDeviceName;
        _rightEdge = _host.Session.Current.RightEdge;
        _settingWidth = _host.Session.Current.WidthDip;
        Loaded += (_, _) =>
        {
            Reveal(_compact ? CompactShell : Shell);
            Dispatcher.BeginInvoke(() => { Place(); SaveFloatingLayout(); }, DispatcherPriority.Background);
        };
        _layoutSaveTimer.Tick += (_, _) => SaveFloatingLayout();
        LocationChanged += (_, _) => QueueLayoutSave();
        SizeChanged += (_, _) => QueueLayoutSave();
        Closed += (_, _) => { SaveFloatingLayout(); _fullscreenTimer.Stop(); };
        _fullscreenTimer.Tick += (_, _) => CheckFullscreen();
        _fullscreenTimer.Start();
        FlyoutPopup.Opened += (_, _) =>
        {
            // Native popup fade handles both opening and outside-click dismissal.
            if (_viewModel.Flyout == "Search")
                Dispatcher.BeginInvoke(() => { SearchField.Focus(); Keyboard.Focus(SearchField); SearchField.SelectAll(); }, DispatcherPriority.Input);
            else
                Dispatcher.BeginInvoke(() => ClosePanelButton.Focus(), DispatcherPriority.Input);
        };
    }

    private void PlaceFloatingDock()
    {
        var settings = _host.Session.Current;
        // A floating dock never takes space away from maximized applications.
        _host.Appbar.Remove();
        Topmost = settings.AlwaysOnTop;
        var monitors = _host.Monitors.GetMonitors();
        var monitor = MonitorCatalog.Find(monitors, settings.AssignedMonitorDeviceName);
        if (monitor is null) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        var scale = (HwndSource.FromHwnd(hwnd)?.CompositionTarget?.TransformToDevice.M11) ?? 1;
        var reset = _assignedMonitor != settings.AssignedMonitorDeviceName || _rightEdge != settings.RightEdge;
        _assignedMonitor = settings.AssignedMonitorDeviceName;
        _rightEdge = settings.RightEdge;
        if (_floating is null || reset)
        {
            var width = Math.Clamp(settings.WidthDip - 12, 76, 112);
            var contentHeight = 840;
            var height = Math.Min(contentHeight, monitor.Work.Height / scale - 40);
            _floating = new FloatingLayout(
                reset && settings.RightEdge ? monitor.Work.Right - (width + 12) * scale : monitor.Work.Left + 12 * scale,
                monitor.Work.Top + (monitor.Work.Height - height * scale) / 2, width, height, 4);
        }
        if (_settingWidth != settings.WidthDip)
            _floating = _floating with { Width = Math.Clamp(settings.WidthDip - 12, 76, 112) };
        _settingWidth = settings.WidthDip;
        if (!_interacting)
        {
            // Use the monitor containing the saved position, including negative-coordinate displays.
            var layout = _floating;
            monitor = monitors.FirstOrDefault(m => layout.X >= m.Work.Left && layout.X < m.Work.Right
                && layout.Y >= m.Work.Top && layout.Y < m.Work.Bottom) ?? monitor;
            var changedMode = _compact != _viewModel.IsCollapsed;
            _compact = _viewModel.IsCollapsed;
            MinWidth = _compact ? 52 : 76;
            MinHeight = _compact ? 76 : 400;
            var width = _compact ? 52 : Math.Clamp(layout.Width, 76, 112);
            var height = _compact ? 76 : Math.Clamp(layout.Height, 400, Math.Max(400, monitor.Work.Height / scale - 24));
            var x = Math.Clamp(layout.X, monitor.Work.Left, Math.Max(monitor.Work.Left, monitor.Work.Right - width * scale));
            var y = Math.Clamp(layout.Y, monitor.Work.Top, Math.Max(monitor.Work.Top, monitor.Work.Bottom - height * scale));
            Width = width;
            Height = height;
            Left = x / scale;
            Top = y / scale;
            NativeMethods.SetWindowPos(hwnd, settings.AlwaysOnTop ? NativeMethods.HwndTopmost : NativeMethods.HwndNotTopmost,
                (int)x, (int)y, (int)(width * scale), (int)(height * scale), NativeMethods.SwpNoActivate);
            if (changedMode) Reveal(_compact ? CompactShell : Shell);
        }
        // Open toward the usable screen area, even after the rail has been dragged.
        FlyoutPopup.Placement = _floating.X > monitor.Work.Left + monitor.Work.Width / 2
            ? PlacementMode.Left : PlacementMode.Right;
        FlyoutPopup.HorizontalOffset = FlyoutPopup.Placement == PlacementMode.Left ? -8 : 8;
        UsageScroll.MaxHeight = Math.Max(56, Math.Min(280, Height * .38));
    }

    private void QueueLayoutSave()
    {
        if (_placing || _compact || !IsLoaded) return;
        _layoutSaveTimer.Stop();
        _layoutSaveTimer.Start();
    }

    private void SaveFloatingLayout()
    {
        _layoutSaveTimer.Stop();
        if (!IsLoaded) return;
        if (!NativeMethods.GetWindowRect(new WindowInteropHelper(this).Handle, out var rect)) return;
        _floating = _compact && _floating is not null
            ? _floating with { X = rect.Left, Y = rect.Top }
            : new FloatingLayout(rect.Left, rect.Top, Width, Height, 4);
        try
        {
            Directory.CreateDirectory(DockPaths.Root);
            var temporary = LayoutPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_floating));
            File.Move(temporary, LayoutPath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _host.Logger.LogWarning("Could not save taskbar placement: {Reason}", ex.Message); }
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _viewModel.FlyoutOpen = false;
        _interacting = true;
        try { DragMove(); }
        finally
        {
            _interacting = false;
            if (_compact && _floating is not null && NativeMethods.GetWindowRect(new WindowInteropHelper(this).Handle, out var rect))
                _floating = _floating with { X = rect.Left, Y = rect.Top };
            SaveFloatingLayout();
            Place();
        }
        e.Handled = true;
    }

    private void ResizeGrip_DragStarted(object sender, DragStartedEventArgs e)
    {
        _interacting = true;
        _viewModel.FlyoutOpen = false;
    }

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Clamp(Width + e.HorizontalChange, 76, 112);
        Height = Math.Clamp(Height + e.VerticalChange, 400, 1400);
        UsageScroll.MaxHeight = Math.Max(56, Math.Min(280, Height * .38));
    }

    private void ResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _interacting = false;
        SaveFloatingLayout();
        Place();
    }

    private void Reveal(FrameworkElement surface)
    {
        if (!_host.Session.Current.Animation || !SystemParameters.ClientAreaAnimation) return;
        surface.RenderTransform = new TranslateTransform();
        var duration = TimeSpan.FromMilliseconds(180);
        surface.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { FillBehavior = FillBehavior.Stop });
        surface.RenderTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, duration)
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }

    private void CheckFullscreen()
    {
        if (_hiddenByUser || _interacting) return;
        var active = false;
        if (_host.Session.Current.YieldOnFullscreen)
        {
            var foreground = NativeMethods.GetForegroundWindow();
            var own = new WindowInteropHelper(this).Handle;
            var name = NativeMethods.ReadClass(foreground);
            if (foreground != IntPtr.Zero && foreground != own && name is not "Progman" and not "WorkerW"
                && NativeMethods.GetWindowRect(foreground, out var rect)
                && NativeMethods.GetWindowRect(own, out var dockRect))
            {
                active = _host.Monitors.GetMonitors().Any(m =>
                    dockRect.Left < m.Bounds.Right && dockRect.Right > m.Bounds.Left
                    && dockRect.Top < m.Bounds.Bottom && dockRect.Bottom > m.Bounds.Top
                    && rect.Left <= m.Bounds.Left && rect.Top <= m.Bounds.Top
                    && rect.Right >= m.Bounds.Right && rect.Bottom >= m.Bounds.Bottom);
            }
        }
        if (active == _yielded) return;
        _yielded = active;
        if (active) { _viewModel.FlyoutOpen = false; Hide(); }
        else { Show(); Place(); }
    }

    private void ResizeGrip_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
        Width = Math.Clamp(Width + (e.Key == Key.Right ? 8 : e.Key == Key.Left ? -8 : 0), 76, 112);
        Height = Math.Clamp(Height + (e.Key == Key.Down ? 16 : e.Key == Key.Up ? -16 : 0), 400, 1400);
        SaveFloatingLayout();
        Place();
        e.Handled = true;
    }

    private void Flyout_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        _viewModel.FlyoutOpen = false;
        e.Handled = true;
    }

    private void UsageProvider_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button)
        {
            FlyoutPopup.PlacementTarget = button;
            FlyoutPopup.VerticalOffset = -18;
        }
    }

    private void CloseFlyout_Click(object sender, RoutedEventArgs e) => _viewModel.FlyoutOpen = false;

    private void SearchField_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _viewModel.FlyoutOpen = false; e.Handled = true; }
        else if (e.Key == Key.Enter && _viewModel.SearchResults.FirstOrDefault() is { } item)
        {
            _viewModel.ActivateItemCommand.Execute(item);
            e.Handled = true;
        }
    }
}
