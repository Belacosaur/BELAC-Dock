using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Belac.Dock.App.ViewModels;
using Belac.Dock.Core.Models;
using Belac.Dock.Windows.Native;
using Microsoft.Win32;

namespace Belac.Dock.App.Views;

public sealed class DockTemplateSelector : DataTemplateSelector
{
    public DataTemplate? AppTemplate { get; set; }
    public DataTemplate? GroupTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is GroupStubViewModel ? GroupTemplate : AppTemplate;
}

public partial class DockWindow : Window
{
    private readonly AppHost _host;
    private readonly DockViewModel _viewModel;
    private bool _yielded;
    private Point _dragStart;
    private AppItemViewModel? _dragItem;
    private bool _placing;

    public DockWindow(AppHost host)
    {
        _host = host;
        _viewModel = host.Dock;
        InitializeComponent();
        DataContext = _viewModel;
        InitializeFloatingLayout();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DockViewModel.Flyout) && _viewModel.Flyout != "Usage")
            {
                FlyoutPopup.PlacementTarget = Shell;
                FlyoutPopup.VerticalOffset = 0;
            }
        };
        _viewModel.AddRequested += (_, _) => AddApps();
        _viewModel.EditRequested += (_, id) => EditApp(id);
        _viewModel.SettingsRequested += (_, _) => ShowSettings();
        _viewModel.LayoutRequested += (_, _) => Dispatcher.BeginInvoke(Place);
        _host.Appbar.LayoutChanged += (_, _) => Dispatcher.BeginInvoke(Place);
        _host.Instance.Activated += (_, _) => Dispatcher.BeginInvoke(ShowDock);
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(Place);
        SystemEvents.SessionEnding += (_, _) =>
        {
            try
            {
                _host.Separation.RestoreAll(hwnd => _host.Inspector.TryInspect(hwnd, out var observation) ? observation : null);
                _host.Appbar.Remove();
            }
            catch (Exception)
            {
            }
        };
        SourceInitialized += (_, _) => OnSourceInitialized();
        UpdateMotion();
        Closing += (_, args) =>
        {
            if (!_hostExit && !_host.Exiting)
            {
                args.Cancel = true;
                HideDock();
            }
        };
    }

    private bool _hostExit;

    public void ShowDock()
    {
        _hiddenByUser = false;
        if (!IsVisible)
            Show();
        _yielded = false;
        Place();
    }

    public void HideDock()
    {
        _hiddenByUser = true;
        _viewModel.FlyoutOpen = false;
        SaveFloatingLayout();
        _host.Appbar.Remove();
        Hide();
    }

    public void PrepareExit() => _hostExit = true;

    private void OnSourceInitialized()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);
        var style = NativeMethods.ReadExStyle(hwnd);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, style | NativeMethods.WsExToolWindow);
        var dark = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        _host.Appbar.Attach(hwnd);
        _host.RegisterHotkey(hwnd);
        Place();
        Dispatcher.BeginInvoke(OfferRecovery);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_host.Appbar.CallbackMessage != 0 && msg == (int)_host.Appbar.CallbackMessage)
        {
            handled = true;
            _host.Appbar.HandleCallback(wParam, lParam);
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WmHotkey)
        {
            handled = true;
            ShowDock();
            _viewModel.ShowSearchCommand.Execute(null);
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WmDpiChanged)
            Dispatcher.BeginInvoke(Place);

        return IntPtr.Zero;
    }

    private void Place()
    {
        if (_placing || new WindowInteropHelper(this).Handle == IntPtr.Zero)
            return;

        _placing = true;
        try
        {
            if (_yielded)
            {
                HideDock();
                return;
            }
            PlaceFloatingDock();
            UpdateMotion();
        }
        finally
        {
            _placing = false;
        }
    }

    private void OfferRecovery()
    {
        // Only records loaded at startup warrant a recovery prompt. Tracking can create
        // new records before this dispatcher callback runs, even after a clean launch.
        if (!_host.RecoveryPending) return;
        _host.RecoveryPending = false;
        var entries = _host.Separation.Snapshots;
        if (entries.Count == 0)
            return;

        var valid = entries.Count(entry => _host.Inspector.TryInspect(entry.Hwnd, out var observation) && Belac.Dock.Core.Matching.JournalValidator.IsSameWindow(entry, observation));
        var dialog = new RecoveryWindow(valid, entries.Count - valid) { Owner = this, Topmost = true };
        if (dialog.ShowDialog() == true)
            _viewModel.RestoreAll();
        else
            _host.Separation.ForgetAll();
    }

    private void UpdateMotion()
    {
        var motion = _host.Session.Current.Animation && SystemParameters.ClientAreaAnimation;
        FlyoutPopup.PopupAnimation = motion ? PopupAnimation.Fade : PopupAnimation.None;
        HoverMotion.AllowAnimation = motion;
    }

    private void AddApps()
    {
        var dialog = new AddAppDialog(_host, null) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is DockApp app)
            _viewModel.ReplaceApp(app);
    }

    private void EditApp(Guid id)
    {
        var existing = _host.Session.Current.Apps.FirstOrDefault(app => app.Id == id);
        if (existing is null)
            return;
        var dialog = new AddAppDialog(_host, existing) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is DockApp app)
            _viewModel.ReplaceApp(app);
    }

    private void ShowSettings()
    {
        var window = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault() ?? new SettingsWindow(_host);
        window.Owner = this;
        if (!window.IsVisible)
            window.Show();
        window.Activate();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.ShowSearchCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && _viewModel.FlyoutOpen)
        {
            _viewModel.FlyoutOpen = false;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && AppList.SelectedItem is AppItemViewModel item)
        {
            _viewModel.ActivateItemCommand.Execute(item);
            e.Handled = true;
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
            _viewModel.AddFromPaths((string[])e.Data.GetData(DataFormats.FileDrop)!);
    }

    private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(AppList);
        _dragItem = FindRow(e.OriginalSource as DependencyObject);
    }

    private void List_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null)
            return;
        var position = e.GetPosition(AppList);
        if (Math.Abs(position.Y - _dragStart.Y) < 14)
            return;
        var id = _dragItem.AppId;
        _dragItem = null;
        DragDrop.DoDragDrop(AppList, id.ToString(), DragDropEffects.Move);
    }

    private void List_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            _viewModel.AddFromPaths((string[])e.Data.GetData(DataFormats.FileDrop)!);
            return;
        }

        if (e.Data.GetData(DataFormats.StringFormat) is string text && Guid.TryParse(text, out var source))
        {
            var target = FindRow(e.OriginalSource as DependencyObject);
            if (target is not null)
                _viewModel.MoveBefore(source, target.AppId);
        }
    }

    private void Row_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var item = FindRow(e.OriginalSource as DependencyObject);
        if (item is null)
        {
            e.Handled = true;
            return;
        }

        var menu = BuildAppContextMenu(item);
        if (menu is not null)
        {
            _viewModel.FlyoutOpen = false;
            var container = ItemsControl.ContainerFromElement(AppList, e.OriginalSource as DependencyObject) as FrameworkElement;
            var top = container?.TranslatePoint(new Point(0, 0), Shell).Y ?? Mouse.GetPosition(Shell).Y;
            menu.PlacementTarget = Shell;
            menu.PlacementRectangle = new Rect(0, Math.Max(0, top), Shell.ActualWidth, container?.ActualHeight ?? 1);
            menu.Placement = FlyoutPopup.Placement;
            menu.HorizontalOffset = menu.Placement == PlacementMode.Right ? 8 : -8;
            menu.IsOpen = true;
        }
        e.Handled = true;
    }

    private static AppItemViewModel? FindRow(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement element && element.DataContext is AppItemViewModel item)
                return item;
            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }
}
