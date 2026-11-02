using System.Text;
using System.Windows.Forms;
using Belac.Dock.Core.Catalog;
using Belac.Dock.Core.Models;
using Belac.Dock.Core.Settings;
using Belac.Dock.Infrastructure;
using Belac.Dock.Windows;
using Belac.Dock.App.ViewModels;
using Microsoft.Extensions.Logging;
using DrawingIcon = System.Drawing.Icon;

namespace Belac.Dock.App;

public sealed class AppHost : IDisposable
{
    private readonly RollingFileLoggerProvider _logs;
    private readonly NativeUsageSource _usageSource;
    public NativeUsageSource NativeUsage => _usageSource;
    private IntPtr _hotkeyWindow;
    private bool _closed;

    public AppHost(bool syncStartup = true, SingleInstanceManager? instance = null)
    {
        _logs = new RollingFileLoggerProvider(DockPaths.LogDirectory);
        Logger = _logs.CreateLogger("BELAC.Dock");
        Session = new SettingsSession(new JsonSettingsStore());
        Palette.Select(Session.Current.ThemeId);
        Session.Changed += (_, _) => Palette.Select(Session.Current.ThemeId);
        Inspector = new WindowInspector();
        Tracker = new WindowTracker();
        Controller = new WindowController();
        Separation = new TaskbarSeparationService(new JsonRecoveryJournalStore(), Inspector, detail => Logger.LogInformation("Taskbar separation: {Detail}", detail));
        RecoveryPending = Separation.Snapshots.Count > 0;
        Tracker.MinimizeStateChanged += hwnd =>
        {
            try
            {
                if (Inspector.TryInspect(hwnd, out var window))
                    Separation.OnMinimizeStateChanged(window, Belac.Dock.Core.Matching.AppMatcher.Match(Session.Current.Apps, window).App);
            }
            catch (Exception ex) { Logger.LogWarning("Minimize handling will retry on refresh: {Error}", ex.GetType().Name); }
        };
        Appbar = new ShellAppbarService();
        Monitors = new MonitorCatalog();
        Icons = new IconExtractor();
        Shortcuts = new ShortcutResolver();
        Startup = new StartupRegistration();
        Hotkeys = new HotkeyRegistrar();
        UsageApps = new OpenTokenUsageLocator();
        _usageSource = new NativeUsageSource(() => Session.Current.Usage);
        UsagePoll = new UsagePollingCoordinator(_usageSource, () => Session.Current.Usage.RefreshIntervalSeconds);
        Instance = instance ?? new SingleInstanceManager();
        Separation.SetPaused(Session.Current.SeparationPaused);
        Tracker.SetManagedPaths(Session.Current.Apps.Select(app => app.ExePath));
        Session.Changed += (_, _) => Tracker.SetManagedPaths(Session.Current.Apps.Select(app => app.ExePath));
        if (syncStartup && !string.IsNullOrWhiteSpace(Environment.ProcessPath))
            Startup.SetEnabled(Session.Current.Autostart, Environment.ProcessPath);
        Dock = new DockViewModel(this);
        Tracker.Start();
        if (Session.Current.Usage.Enabled)
            UsagePoll.Start();
        Tray = CreateTray();
        Logger.LogInformation("BELAC Dock started.");
    }

    public SettingsSession Session { get; }
    public WindowTracker Tracker { get; }
    public WindowController Controller { get; }
    public TaskbarSeparationService Separation { get; }
    public ShellAppbarService Appbar { get; }
    public MonitorCatalog Monitors { get; }
    public IconExtractor Icons { get; }
    public ShortcutResolver Shortcuts { get; }
    public StartupRegistration Startup { get; }
    public HotkeyRegistrar Hotkeys { get; }
    public WindowInspector Inspector { get; }
    public OpenTokenUsageLocator UsageApps { get; }
    public UsagePollingCoordinator UsagePoll { get; }
    public SingleInstanceManager Instance { get; }
    public DockViewModel Dock { get; }
    public NotifyIcon Tray { get; }
    public ILogger Logger { get; }
    public bool Exiting { get; private set; }
    public bool RecoveryPending { get; set; }
    public bool ShortcutRegistered { get; private set; }
    public string ShortcutWarning { get; private set; } = "";

    public Action? ShowDock { get; set; }
    public Action? HideDock { get; set; }
    public Action? OpenSettings { get; set; }

    public void RegisterHotkey(IntPtr hwnd)
    {
        _hotkeyWindow = hwnd;
        if (!HotkeyChord.TryParse(Session.Current.KeyboardShortcut, out var chord))
        {
            ShortcutRegistered = false;
            ShortcutWarning = "The saved shortcut is not valid.";
            return;
        }

        ShortcutRegistered = Hotkeys.TryRegister(hwnd, 1, chord);
        ShortcutWarning = ShortcutRegistered ? "" : "Ctrl+Alt+Shift+D is already in use. The tray icon still opens the dock.";
        if (!ShortcutRegistered)
            Logger.LogWarning("Global shortcut was not registered.");
    }

    public void UnregisterHotkey()
    {
        if (_hotkeyWindow != IntPtr.Zero)
            Hotkeys.Unregister(_hotkeyWindow, 1);
    }

    public void RestartUsage()
    {
        UsagePoll.Stop();
        if (Session.Current.Usage.Enabled)
            UsagePoll.Start();
        else
            Dock.Usage.Apply(UsageSnapshot.Empty(DateTimeOffset.Now), Session.Current.Usage);
    }

    public void ConnectUsage(System.Windows.Window? owner = null)
    {
        var existing = System.Windows.Application.Current.Windows.OfType<Belac.Dock.App.Views.ConnectUsageWindow>().FirstOrDefault();
        if (existing is not null) { existing.Activate(); return; }
        var wizard = new Belac.Dock.App.Views.ConnectUsageWindow(this);
        if (owner is not null && owner.IsVisible) wizard.Owner = owner;
        wizard.Show();
    }

    public void OpenUsageApp()
    {
        var location = UsageApps.Locate();
        if (location.IsRunning && location.MainWindowHandle != 0 && Controller.TryActivate(location.MainWindowHandle))
            return;

        if (!string.IsNullOrWhiteSpace(location.ExecutablePath) && File.Exists(location.ExecutablePath))
        {
            Process.Start(LaunchRequest.Create(location.ExecutablePath, null, null));
            return;
        }

        var message = location.IsRunning
            ? "OpenTokenUsage is already running in the notification area."
            : "BELAC Dock could not find OpenTokenUsage. Start it, then retry the usage rail.";
        System.Windows.MessageBox.Show(message, "OpenTokenUsage", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    public void ShutdownApp()
    {
        if (_closed)
            return;
        _closed = true;
        Exiting = true;
        Logger.LogInformation("BELAC Dock is exiting.");
        try
        {
            Separation.RestoreAll(hwnd => Inspector.TryInspect(hwnd, out var observation) ? observation : null);
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Restore during exit failed: {Error}", ex.GetType().Name);
        }

        try { Appbar.Remove(); } catch (Exception ex) { Logger.LogWarning("Appbar removal failed: {Error}", ex.GetType().Name); }
        try { UnregisterHotkey(); } catch (Exception) { }
        try { UsagePoll.Stop(); } catch (Exception) { }
        try { Session.Save(); } catch (Exception ex) { Logger.LogWarning("Settings save failed: {Error}", ex.GetType().Name); }
        try { Tracker.Dispose(); } catch (Exception) { }
        Tray.Visible = false;
        Tray.Dispose();
        Separation.Dispose();
        _usageSource.Dispose();
        Instance.Dispose();
        _logs.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    public string Diagnostics()
    {
        var builder = new StringBuilder();
        builder.AppendLine(Dock.BuildDiagnostics());
        builder.AppendLine("Windows: " + Environment.OSVersion.VersionString);
        builder.AppendLine("Runtime: " + Environment.Version);
        return builder.ToString();
    }

    public void Dispose() => ShutdownApp();

    private NotifyIcon CreateTray()
    {
        DrawingIcon icon;
        try
        {
            icon = DrawingIcon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application;
        }
        catch (Exception)
        {
            icon = System.Drawing.SystemIcons.Application;
        }

        var tray = new NotifyIcon
        {
            Icon = icon,
            Text = "BELAC Dock",
            Visible = true
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show dock", null, (_, _) => ShowDock?.Invoke());
        menu.Items.Add("Hide dock", null, (_, _) => HideDock?.Invoke());
        menu.Items.Add("Pause taskbar separation", null, (_, _) => Dock.TogglePauseCommand.Execute(null));
        menu.Items.Add("Restore all managed windows", null, (_, _) =>
        {
            var count = Dock.RestoreAll();
            tray.ShowBalloonTip(2000, "BELAC Dock", count == 0 ? "No window styles needed restoring." : $"Restored {count} window style(s).", ToolTipIcon.Info);
        });
        menu.Items.Add("Settings", null, (_, _) => OpenSettings?.Invoke());
        menu.Items.Add("Exit", null, (_, _) => ShutdownApp());
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowDock?.Invoke();
        return tray;
    }
}
