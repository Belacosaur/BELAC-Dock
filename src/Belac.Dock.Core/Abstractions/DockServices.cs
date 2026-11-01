using Belac.Dock.Core.Models;

namespace Belac.Dock.Core.Abstractions;

public interface ISettingsStore
{
    SettingsLoadResult Load();
    void Save(DockSettings settings);
}

public interface IRecoveryJournalStore
{
    IReadOnlyList<WindowMutationSnapshot> Load();
    void Save(IReadOnlyList<WindowMutationSnapshot> entries);
}

public interface IUsageProviderSource
{
    Task<UsageSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}

public interface IWindowController
{
    string? LastFailure { get; }
    bool TryActivate(long hwnd);
    bool TryMinimize(long hwnd);
    bool TryClose(long hwnd);
}

public interface IWindowTracker : IDisposable
{
    bool IsHooked { get; }
    long ForegroundHwnd { get; }
    long TaskbarHwnd { get; }
    IReadOnlyList<WindowObservation> Windows { get; }
    event EventHandler? Changed;
    void Start();
    void SetManagedPaths(IEnumerable<string> paths);
}

public interface ITaskbarSeparationService
{
    bool IsPaused { get; }
    string LastDetail { get; }
    IReadOnlyList<string> RecentEvents { get; }
    SeparationResult Apply(DockApp app, WindowObservation window);
    bool RestoreOne(WindowObservation window);
    int RestoreAll(Func<long, WindowObservation?> probe);
    void SetPaused(bool paused);
}

public interface IAppbarHost
{
    bool IsRegistered { get; }
    PixelRect? DockBounds { get; }
    bool FullscreenActive { get; }
    uint CallbackMessage { get; }
    event EventHandler? LayoutChanged;
    event EventHandler<bool>? FullscreenChanged;
    void Attach(IntPtr hwnd);
    void Apply(bool reserve, bool rightEdge, int widthPx, PixelRect workArea);
    void Remove();
    void HandleCallback(IntPtr wParam, IntPtr lParam);
    void NoteWindowPosChanged();
}

public interface IMonitorCatalog
{
    IReadOnlyList<MonitorInfo> GetMonitors();
}

public interface IIconSource
{
    byte[]? GetPng(string path, int size);
}

public interface IShortcutResolver
{
    ShortcutTarget? Resolve(string shortcutPath);
}

public interface IStartupRegistration
{
    bool IsRegistered { get; }
    void SetEnabled(bool enabled, string executablePath);
}

public interface IHotkeyRegistrar
{
    bool TryRegister(IntPtr hwnd, int id, HotkeyChord chord);
    void Unregister(IntPtr hwnd, int id);
}

public interface ISingleInstance : IDisposable
{
    bool IsPrimary { get; }
    event EventHandler? Activated;
    void Signal();
}

public interface IWindowInspector
{
    bool TryInspect(long hwnd, out WindowObservation observation);
}

public sealed record UsageAppLocation(bool IsRunning, string? ExecutablePath, long MainWindowHandle);

public interface IUsageAppLocator
{
    UsageAppLocation Locate();
}
