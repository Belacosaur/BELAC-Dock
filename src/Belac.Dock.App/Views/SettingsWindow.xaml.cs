using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Belac.Dock.Core.Models;
using Belac.Dock.Core.Settings;
using Belac.Dock.Windows;
using Microsoft.Win32;

namespace Belac.Dock.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppHost _host;
    private bool _loading = true;

    public SettingsWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        var settings = host.Session.Current;
        ThemePicker.ItemsSource = DockThemes.All;
        ThemePicker.SelectedItem = DockThemes.Find(settings.ThemeId);
        WidthSlider.Value = Math.Clamp(settings.WidthDip - 12, 76, 112);
        IconSlider.Value = Math.Clamp(settings.IconSizeDip - 16, 16, 34);
        UpdateSizeLabel();
        AnimationBox.IsChecked = settings.Animation;
        MonitorBox.ItemsSource = host.Monitors.GetMonitors();
        MonitorBox.SelectedItem = MonitorCatalog.Find(host.Monitors.GetMonitors(), settings.AssignedMonitorDeviceName);
        EdgeBox.SelectedIndex = settings.RightEdge ? 0 : 1;
        ReserveBox.IsChecked = settings.ReserveWorkArea;
        TopmostBox.IsChecked = settings.AlwaysOnTop;
        YieldBox.IsChecked = settings.YieldOnFullscreen;
        MinimizeFocusedBox.IsChecked = settings.MinimizeIfFocused;
        ReloadApps();
        AutostartBox.IsChecked = settings.Autostart;
        MinimizedBox.IsChecked = settings.StartMinimizedToTray;
        ShortcutBox.Text = settings.KeyboardShortcut;
        ShortcutWarning.Text = host.ShortcutRegistered ? "Click the box and press a new shortcut." : host.ShortcutWarning;
        UsageEnabledBox.IsChecked = settings.Usage.Enabled;
        UsageHideBox.IsChecked = settings.Usage.AutoHideOnShortDisplay;
        RemainingBox.IsChecked = settings.Usage.ShowRemaining;
        IntervalSlider.Value = settings.Usage.RefreshIntervalSeconds;
        IntervalLabel.Text = $"Every {settings.Usage.RefreshIntervalSeconds} seconds";
        SeparationStatus.Text = host.Separation.LastDetail;
        DiagnosticsBox.Text = host.Diagnostics();
        ReloadProviders();
        _loading = false;
    }

    private sealed record ProviderEdit(string Id, string Name, bool Hidden)
    {
        public string Label => Hidden ? Name + " · hidden" : Name;
    }

    private void ThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemePicker.SelectedItem is not DockTheme theme) return;
        _host.Session.Current.ThemeId = theme.Id;
        _host.Session.Save();
    }

    private void WidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || WidthLabel is null)
            return;
        _host.Session.Current.WidthDip = Math.Round(WidthSlider.Value) + 12;
        UpdateSizeLabel();
        _host.Session.Save();
    }

    private void IconSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || WidthLabel is null)
            return;
        _host.Session.Current.IconSizeDip = Math.Round(IconSlider.Value) + 16;
        UpdateSizeLabel();
        _host.Session.Save();
    }

    private void AppearanceChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        _host.Session.Current.Animation = AnimationBox.IsChecked == true;
        _host.Session.Save();
    }

    private void UpdateSizeLabel() => WidthLabel.Text = $"{WidthSlider.Value:0} px bar width, {IconSlider.Value:0} px icons";

    private void MonitorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MonitorBox.SelectedItem is not MonitorInfo monitor)
            return;
        _host.Session.Current.AssignedMonitorDeviceName = monitor.DeviceName;
        _host.Session.Save();
    }

    private void EdgeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;
        _host.Session.Current.Edge = EdgeBox.SelectedIndex == 1 ? "Left" : "Right";
        _host.Session.Save();
    }

    private void PositionChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        var settings = _host.Session.Current;
        settings.ReserveWorkArea = ReserveBox.IsChecked == true;
        settings.AlwaysOnTop = TopmostBox.IsChecked == true;
        settings.YieldOnFullscreen = YieldBox.IsChecked == true;
        settings.MinimizeIfFocused = MinimizeFocusedBox.IsChecked == true;
        _host.Session.Save();
    }

    private void StartupChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        var settings = _host.Session.Current;
        settings.Autostart = AutostartBox.IsChecked == true;
        settings.StartMinimizedToTray = MinimizedBox.IsChecked == true;
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
            _host.Startup.SetEnabled(settings.Autostart, Environment.ProcessPath);
        _host.Session.Save();
    }

    private void UsageChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        var usage = _host.Session.Current.Usage;
        usage.Enabled = UsageEnabledBox.IsChecked == true;
        usage.AutoHideOnShortDisplay = UsageHideBox.IsChecked == true;
        usage.ShowRemaining = RemainingBox.IsChecked == true;
        _host.Session.Save();
        _host.RestartUsage();
    }

    private void IntervalChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || IntervalLabel is null)
            return;
        _host.Session.Current.Usage.RefreshIntervalSeconds = (int)Math.Round(IntervalSlider.Value);
        IntervalLabel.Text = $"Every {_host.Session.Current.Usage.RefreshIntervalSeconds} seconds";
        _host.Session.Save();
        _host.RestartUsage();
    }

    private void ShortcutBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
            return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var parts = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        var text = string.Join('+', parts);
        if (!HotkeyChord.TryParse(text, out var chord))
        {
            ShortcutWarning.Text = "Use at least one modifier and a letter, number, or function key.";
            return;
        }

        _host.Session.Current.KeyboardShortcut = chord.ToString();
        ShortcutBox.Text = chord.ToString();
        _host.Session.Save();
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            hwnd = new System.Windows.Interop.WindowInteropHelper(Owner).Handle;
        _host.RegisterHotkey(hwnd);
        ShortcutWarning.Text = _host.ShortcutRegistered ? "Shortcut updated." : _host.ShortcutWarning;
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddAppDialog(_host, null) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is DockApp app)
        {
            _host.Dock.ReplaceApp(app);
            ReloadApps();
        }
    }

    private void EditApp_Click(object sender, RoutedEventArgs e)
    {
        if (AppList.SelectedItem is not DockApp selected)
            return;
        var dialog = new AddAppDialog(_host, selected) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is DockApp app)
        {
            _host.Dock.ReplaceApp(app);
            ReloadApps();
        }
    }

    private void RemoveApp_Click(object sender, RoutedEventArgs e)
    {
        if (AppList.SelectedItem is not DockApp selected)
            return;
        if (MessageBox.Show($"Remove {selected.DisplayName} from the dock?", "BELAC Dock", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        _host.Session.Current.Apps.RemoveAll(app => app.Id == selected.Id);
        _host.Session.Save();
        ReloadApps();
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var count = _host.Dock.RestoreAll();
        SeparationStatus.Text = count == 0 ? "No managed window styles needed restoring." : $"Restored {count} window style(s).";
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => _host.Dock.TogglePauseCommand.Execute(null);

    private void OpenUsage_Click(object sender, RoutedEventArgs e) => _host.OpenUsageApp();

    private async void RefreshUsage_Click(object sender, RoutedEventArgs e)
    {
        await _host.UsagePoll.RefreshAsync(CancellationToken.None);
        ReloadProviders();
    }

    private void ProviderUp_Click(object sender, RoutedEventArgs e) => MoveProvider(-1);

    private void ProviderDown_Click(object sender, RoutedEventArgs e) => MoveProvider(1);

    private void ProviderHide_Click(object sender, RoutedEventArgs e)
    {
        if (ProviderList.SelectedItem is not ProviderEdit selected)
            return;
        var usage = _host.Session.Current.Usage;
        if (!usage.HiddenProviderIds.Contains(selected.Id, StringComparer.OrdinalIgnoreCase))
            usage.HiddenProviderIds.Add(selected.Id);
        _host.Session.Save();
        ReloadProviders();
    }

    private void ProviderShow_Click(object sender, RoutedEventArgs e)
    {
        if (ProviderList.SelectedItem is not ProviderEdit selected)
            return;
        _host.Session.Current.Usage.HiddenProviderIds.RemoveAll(id => string.Equals(id, selected.Id, StringComparison.OrdinalIgnoreCase));
        _host.Session.Save();
        ReloadProviders();
    }

    private void MoveProvider(int delta)
    {
        if (ProviderList.SelectedItem is not ProviderEdit selected || ProviderList.ItemsSource is not List<ProviderEdit> items)
            return;
        var index = items.FindIndex(item => string.Equals(item.Id, selected.Id, StringComparison.OrdinalIgnoreCase));
        var target = index + delta;
        if (index < 0 || target < 0 || target >= items.Count)
            return;
        (items[index], items[target]) = (items[target], items[index]);
        var usage = _host.Session.Current.Usage;
        usage.ProviderOrder = items.Where(item => !item.Hidden).Select(item => item.Id).ToList();
        _host.Session.Save();
        ReloadProviders();
        ProviderList.SelectedItem = ProviderList.Items.OfType<ProviderEdit>().FirstOrDefault(item => string.Equals(item.Id, selected.Id, StringComparison.OrdinalIgnoreCase));
    }

    private void ReloadProviders()
    {
        var usage = _host.Session.Current.Usage;
        var hidden = new HashSet<string>(usage.HiddenProviderIds, StringComparer.OrdinalIgnoreCase);
        var rows = _host.UsagePoll.Current.Providers
            .Select(provider => new ProviderEdit(provider.Id, provider.DisplayName, hidden.Contains(provider.Id)))
            .ToList();
        ProviderList.ItemsSource = rows;
    }

    private void RefreshDiagnostics_Click(object sender, RoutedEventArgs e) => DiagnosticsBox.Text = _host.Diagnostics();

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "ZIP archive (*.zip)|*.zip",
            FileName = "BELAC-Dock-diagnostics.zip"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var temp = Path.Combine(Path.GetTempPath(), "belac-diag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var clone = JsonSerializer.Deserialize<DockSettings>(JsonSerializer.Serialize(_host.Session.Current, DockJson.Options), DockJson.Options) ?? DockSettings.CreateDefault();
            foreach (var app in clone.Apps)
            {
                if (app.LaunchArgs is not null)
                    app.LaunchArgs = "(omitted)";
            }

            File.WriteAllText(Path.Combine(temp, "config.sanitized.json"), JsonSerializer.Serialize(clone, DockJson.Options));
            File.WriteAllText(Path.Combine(temp, "diagnostics.txt"), _host.Diagnostics());
            if (Directory.Exists(DockPaths.LogDirectory))
            {
                foreach (var log in Directory.GetFiles(DockPaths.LogDirectory, "*.log"))
                    File.Copy(log, Path.Combine(temp, Path.GetFileName(log)), true);
            }

            if (File.Exists(dialog.FileName))
                File.Delete(dialog.FileName);
            ZipFile.CreateFromDirectory(temp, dialog.FileName);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    private void ReloadApps() => AppList.ItemsSource = _host.Session.Current.Apps.ToList();
}
