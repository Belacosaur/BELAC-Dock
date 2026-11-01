using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Belac.Dock.App;
using Belac.Dock.App.Views;
using Belac.Dock.Core.Models;
using Belac.Dock.Core.Settings;

// Run on an interactive Windows desktop. Uses a unique disposable config, no startup registration,
// no provider requests, no application launches, and no taskbar separation.
internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--host-fixture") return HostFixture.Run(args[1]);
        var home = Path.GetFullPath(Path.Combine("artifacts", "ui-check-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(home);
        Environment.SetEnvironmentVariable("BELAC_DOCK_HOME", home);
        var settings = DockSettings.CreateDefault();
        settings.OnboardingCompleted = true;
        settings.Usage.Enabled = false;
        settings.Usage.NativeProviders = [];
        settings.YieldOnFullscreen = false;
        settings.Animation = false;
        settings.Apps.Add(new DockApp { DisplayName = "UI fixture", ExePath = Environment.ProcessPath!, GroupId = settings.Groups[0].Id });
        File.WriteAllText(Path.Combine(home, "config.json"), JsonSerializer.Serialize(settings, DockJson.Options));
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var file in new[] { "Colors", "Typography", "Controls", "Workspace" })
            application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/BELAC.Dock;component/Styles/{file}.xaml") });
        application.Resources["BoolVis"] = new BoolVisibilityConverter();
        application.Resources["BoolVisInvert"] = new BoolVisibilityConverter { Invert = true };
        application.Resources["EnumVis"] = new EnumVisibilityConverter();
        application.Resources["NullVis"] = new NullVisibilityConverter();
        application.Resources["NullVisInvert"] = new NullVisibilityConverter { Invert = true };
        Palette.Apply(false);
        using var host = new AppHost(syncStartup: false);
        application.Exit += (_, _) => host.Dispose();
        var dock = new DockWindow(host);
        var failures = new List<string>();
        void Check(bool success, string label)
        {
            Console.WriteLine((success ? "PASS " : "FAIL ") + label);
            if (!success) failures.Add(label);
        }
        application.DispatcherUnhandledException += (_, e) =>
        {
            failures.Add(e.Exception.ToString());
            Console.Error.WriteLine(e.Exception);
            e.Handled = true;
            application.Shutdown(1);
        };
        dock.Loaded += async (_, _) =>
        {
            try
            {
                await Task.Delay(150);
                Check(!host.Appbar.IsRegistered, "Floating dock does not reserve desktop work area");
                var initialHandle = new System.Windows.Interop.WindowInteropHelper(dock).Handle;
                Belac.Dock.Windows.Native.NativeMethods.GetWindowRect(initialHandle, out var initialRect);
                var initialMonitor = Belac.Dock.Windows.MonitorCatalog.Find(host.Monitors.GetMonitors(), host.Session.Current.AssignedMonitorDeviceName)!;
                var initialScale = System.Windows.Interop.HwndSource.FromHwnd(initialHandle)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
                Console.WriteLine($"Placement actual={initialRect.Left},{initialRect.Top}; expectedLeft={initialMonitor.Work.Left + 12 * initialScale}; edge={host.Session.Current.Edge}");
                Check(host.Session.Current.Edge == "Left" && Math.Abs(initialRect.Left - initialMonitor.Work.Left - 12 * initialScale) <= 3,
                    "Taskbar starts at the left edge of the assigned monitor");
                Check(dock.Width >= 76 && dock.Width <= 112 && dock.Height >= 400, "Initial usable size");
                Check(!HoverMotion.AllowAnimation, "Reduced motion follows app preference");
                var grip = Descendants<Thumb>(dock).First(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Resize dock");
                grip.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                grip.RaiseEvent(new DragDeltaEventArgs(44, 90) { RoutedEvent = Thumb.DragDeltaEvent });
                grip.RaiseEvent(new DragCompletedEventArgs(44, 90, false) { RoutedEvent = Thumb.DragCompletedEvent });
                await Task.Delay(400);
                var width = dock.Width;
                var height = dock.Height;
                Check(width >= 76 && width <= 112 && height >= 410, "Resize gesture changes window dimensions");
                Check(File.Exists(Path.Combine(home, "floating-layout.json")), "Geometry persisted separately from backend settings");
                host.Dock.ToggleCollapseCommand.Execute(null);
                await Task.Delay(100);
                Check(dock.Width == 52 && dock.Height == 76, "Collapse releases the large window footprint");
                host.Dock.ToggleCollapseCommand.Execute(null);
                await Task.Delay(100);
                Check(Math.Abs(dock.Width - width) < 1 && Math.Abs(dock.Height - height) < 1, "Expand restores resized dimensions");
                var provider = new Belac.Dock.App.ViewModels.ProviderRowViewModel { DisplayName = "Fixture", Monogram = "F", HasRing = true, OuterPercent = 42 };
                provider.Details.Add("Weekly: 42% used. Resets in 3 days.");
                provider.Details.Add("Fetched today");
                host.Dock.Usage.Providers.Add(provider);
                host.Dock.Usage.HasData = true;
                host.Dock.Usage.IsEnabled = true;
                await Task.Delay(50);
                var appList = (ListBox)dock.FindName("AppList");
                var usageRail = (Border)dock.FindName("UsageRail");
                Check(appList.IsVisible && usageRail.IsVisible && appList.ActualHeight > 0,
                    "Applications and LLMs remain visible together");
                Check(usageRail.TranslatePoint(new Point(0, 0), dock).Y > appList.TranslatePoint(new Point(0, 0), dock).Y + appList.ActualHeight,
                    "LLM rail is anchored below the taskbar applications");
                var usageScroll = (ScrollViewer)dock.FindName("UsageScroll");
                var compactRing = Descendants<ProviderRingControl>(usageRail).First();
                foreach (var narrowWidth in new[] { 76d, 84d })
                {
                    dock.Width = narrowWidth;
                    dock.UpdateLayout();
                    var presenter = Descendants<ScrollContentPresenter>(usageScroll).First();

                    
                    var bounds = compactRing.TransformToAncestor(presenter).TransformBounds(new Rect(compactRing.RenderSize));
                    // Include the maximum hover expansion around the ring center.
                    bounds.Inflate(bounds.Width * .045 / 2, bounds.Height * .045 / 2);
                    Check(bounds.Left >= 0 && bounds.Right <= presenter.ActualWidth,
                        $"LLM ring including hover fits {narrowWidth}px bar");

                }
                dock.Width = width;
                var fixture = appList.Items.OfType<Belac.Dock.App.ViewModels.AppItemViewModel>().First();
                var menu = dock.BuildAppContextMenu(fixture)!;
                Check(menu.Items.OfType<MenuItem>().Count() <= 7, "Context menu keeps primary actions compact");
                var more = menu.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "More"));
                Check(more.Items.OfType<MenuItem>().Any(m => Equals(m.Header, "Taskbar options")), "Advanced taskbar controls remain available under More");
                menu.PlacementTarget = dock;
                menu.IsOpen = true;
                await Task.Delay(50);
                more.IsSubmenuOpen = true;
                await Task.Delay(50);
                Check(menu.ActualWidth >= 220 && more.IsSubmenuOpen, "Styled context menu and submenu open");
                var stableButton = Descendants<Button>(appList).First(b => b.DataContext == fixture);
                var changes = 0;
                System.Collections.Specialized.NotifyCollectionChangedEventHandler onRows = (_, _) => changes++;
                host.Dock.Rows.CollectionChanged += onRows;
                var menuPosition = menu.PointToScreen(new Point());
                for (var refresh = 0; refresh < 12; refresh++)
                {
                    host.Dock.Rebuild();
                    await Task.Delay(40);
                }
                host.Dock.Rows.CollectionChanged -= onRows;
                Check(changes == 0 && ReferenceEquals(stableButton, Descendants<Button>(appList).First(b => b.DataContext == fixture)), "Tracker refresh preserves app visuals and hover state");
                Check(menu.IsOpen && more.IsSubmenuOpen && menu.PointToScreen(new Point()) == menuPosition, "Menu and submenu remain open and stationary during tracker refresh");
                more.IsSubmenuOpen = false;
                menu.IsOpen = false;
                Check(new[] { "Cursor", "Codex", "Codex · WSL", "Claude", "Gemini", "Grok", "DeepSeek", "Copilot" }.All(name => ProviderLogo.Find(name) is not null), "Known LLM providers resolve to local vector logos");
                host.Dock.ShowUsageCommand.Execute(provider);
                await Task.Delay(100);
                var usagePopup = (Popup)dock.FindName("FlyoutPopup");
                var metric = Descendants<ContentControl>(usagePopup.Child).Select(c => c.Content).OfType<UsageMetricPresentation>().FirstOrDefault(m => m.IsMetric);
                Check(metric is { Label: "Weekly", Value: "42% used", Percent: 42 }, "Usage details preserve metric values and labels");
                host.Dock.FlyoutOpen = false;
                await Task.Delay(200);
                Check(!usagePopup.IsOpen, "LLM panel closes cleanly");
                host.Dock.ShowSearchCommand.Execute(null);
                await Task.Delay(150);
                var popup = (Popup)dock.FindName("FlyoutPopup");
                Check(popup.IsOpen && popup.PopupAnimation == PopupAnimation.None, "Search opens with reduced motion");
                var result = Descendants<Button>(popup.Child).FirstOrDefault(b => b.DataContext is Belac.Dock.App.ViewModels.AppItemViewModel);
                Check(result?.Command == host.Dock.ActivateItemCommand, "Search result command crosses popup boundary");
                host.Dock.SearchText = "no-such-fixture";
                await Task.Delay(30);
                Check(host.Dock.SearchResults.Count == 0, "Search filtering updates immediately");
                host.Dock.FlyoutOpen = false;
                dock.HideDock();
                dock.ShowDock();
                await Task.Delay(80);
                Check(Math.Abs(dock.Width - width) < 1 && Math.Abs(dock.Height - height) < 1, "Hide and show preserve geometry");
                var ring = new ProviderRingControl { OuterPercent = 50, InnerPercent = double.NaN };
                ring.Measure(new Size(52, 52)); ring.Arrange(new Rect(0, 0, 52, 52));
                Check(ring.ActualWidth == 52, "Usage ring accepts unknown inner readings");
                var window = new SettingsWindow(host);
                window.Show();
                await Task.Delay(30);
                Check(window.IsLoaded, "Settings templates load");
                var setupSource = new SetupSource();
                var setup = new ConnectUsageWindow(host, setupSource);
                setup.Show();
                var setupNext = (Button)setup.FindName("NextButton");
                setupNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                setupNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var setupCheck = (Button)setup.FindName("CheckButton");
                for (var attempt = 0; attempt < 100 && !setupCheck.IsEnabled; attempt++) await Task.Delay(30);
                Check(!setupNext.IsEnabled && ((TextBlock)setup.FindName("ConnectionTitle")).Text.Contains("No connection"), "Wizard blocks finish when connection is unavailable");
                setupSource.Snapshot = UsageSnapshot.Empty(DateTimeOffset.Now);
                setupCheck.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (var attempt = 0; attempt < 100 && !setupCheck.IsEnabled; attempt++) await Task.Delay(30);
                Check(!setupNext.IsEnabled && ((TextBlock)setup.FindName("ConnectionTitle")).Text.Contains("waiting"), "Wizard distinguishes empty connected service from usable readings");
                setupSource.Snapshot = new UsageSnapshot(DateTimeOffset.Now, [new ProviderUsage("setup-fixture", "Setup fixture", null,
                    [new UsageMetric("quota", "Quota", 0, 0, 100, 100, "%", null, null, null, null)], DateTimeOffset.Now, false, null)], null);
                host.Session.Current.Usage.HiddenProviderIds.Add("setup-fixture");
                setupCheck.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (var attempt = 0; attempt < 100 && !setupCheck.IsEnabled; attempt++) await Task.Delay(30);
                Check(setupNext.IsEnabled && ((ItemsControl)setup.FindName("Readings")).Items.Count == 1, "Wizard lists real readings and allows finish");
                setupNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!setup.IsVisible && host.Session.Current.Usage.Enabled && !host.Session.Current.Usage.HiddenProviderIds.Contains("setup-fixture"), "Finish enables readings and reveals connected providers");
                host.UsagePoll.Stop();
                host.Session.Current.Usage.Enabled = false;
                host.Session.Save();

                var themePicker = (ListBox)window.FindName("ThemePicker");
                var liveRingBrush = Palette.BandBrush(RingBand.Normal, false);
                foreach (var theme in DockThemes.All)
                {
                    themePicker.SelectedItem = theme;
                    await Task.Delay(50);
                    Check(host.Session.Current.ThemeId == theme.Id &&
                        ((SolidColorBrush)application.Resources["Brush.Accent"]).Color == (Color)ColorConverter.ConvertFromString(theme.Accent),
                        $"{theme.Name} selection applies live and saves preference");
                }
                Check(ReferenceEquals(liveRingBrush, Palette.BandBrush(RingBand.Normal, false)), "Existing usage ring brushes update without replacement");
                Palette.Apply(true);
                Check(((SolidColorBrush)application.Resources["Brush.Background"]).Color == SystemColors.WindowColor, "High contrast overrides theme");
                Palette.Apply(false);
                Check(((SolidColorBrush)application.Resources["Brush.Accent"]).Color == (Color)ColorConverter.ConvertFromString(DockThemes.Find(host.Session.Current.ThemeId).Accent), "Theme returns after high contrast ends");
                window.Close();
                dock.PrepareExit();
                dock.Close();
                var layoutPath = Path.Combine(home, "floating-layout.json");
                var savedLayout = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(layoutPath))!;
                savedLayout["X"] = -100000;
                savedLayout["Y"] = -100000;
                File.WriteAllText(layoutPath, savedLayout.ToJsonString());
                host.Session.Current.Animation = true;
                var restored = new DockWindow(host);
                restored.Show();
                await Task.Delay(80);
                Check(Math.Abs(restored.Width - width) < 1 && Math.Abs(restored.Height - height) < 1, "New window restores saved dimensions");
                var handle = new System.Windows.Interop.WindowInteropHelper(restored).Handle;
                Belac.Dock.Windows.Native.NativeMethods.GetWindowRect(handle, out var rect);
                Check(host.Monitors.GetMonitors().Any(m => rect.Left >= m.Work.Left && rect.Top >= m.Work.Top
                    && rect.Right <= m.Work.Right + 2 && rect.Bottom <= m.Work.Bottom + 2), "Disconnected monitor placement returns to visible work area");
                Check(HoverMotion.AllowAnimation, "Animated presentation can be re-enabled");
                restored.PrepareExit(); restored.Close();
            }
            catch (Exception ex) { failures.Add(ex.ToString()); Console.Error.WriteLine(ex); }
            finally { dock.PrepareExit(); application.Shutdown(failures.Count == 0 ? 0 : 1); }
        };
        dock.Show();
        var resultCode = application.Run();
        File.WriteAllLines(Path.Combine(home, "result.txt"), failures.Count == 0 ? ["PASS: all floating UI smoke checks"] : failures);
        return resultCode;
    }

    private sealed class SetupSource : Belac.Dock.Core.Abstractions.IUsageProviderSource
    {
        public UsageSnapshot Snapshot { get; set; } = UsageSnapshot.Failed(DateTimeOffset.Now, "offline");
        public Task<UsageSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(Snapshot);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
