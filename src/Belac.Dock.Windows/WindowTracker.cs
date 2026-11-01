using System.Windows.Threading;
using Belac.Dock.Core.Abstractions;
using Belac.Dock.Core.Matching;
using Belac.Dock.Core.Models;
using Belac.Dock.Windows.Native;

namespace Belac.Dock.Windows;

public sealed class WindowTracker : IWindowTracker
{
    private readonly WindowInspector _inspector = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly object _gate = new();
    private NativeMethods.WinEventDelegate? _callback;
    private readonly List<IntPtr> _hooks = [];
    private Thread? _worker;
    private Dispatcher? _dispatcher;
    private string[] _managedPaths = [];
    private volatile bool _stop;
    private volatile bool _started;

    public bool IsHooked { get; private set; }
    public long ForegroundHwnd { get; private set; }
    public long TaskbarHwnd { get; private set; }
    public IReadOnlyList<WindowObservation> Windows { get; private set; } = [];
    public event EventHandler? Changed;

    public void SetManagedPaths(IEnumerable<string> paths)
    {
        var normalized = new List<string>();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            try
            {
                normalized.Add(Path.GetFullPath(path));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
        }

        lock (_gate)
            _managedPaths = normalized.ToArray();
    }

    public void Start()
    {
        if (_started)
            return;

        _started = true;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _callback = OnEvent;
        _hooks.Add(NativeMethods.SetWinEventHook(NativeMethods.EventObjectCreate, NativeMethods.EventObjectHide, IntPtr.Zero, _callback, 0, 0, NativeMethods.WinEventOutOfContext | NativeMethods.WinEventSkipOwnProcess));
        _hooks.Add(NativeMethods.SetWinEventHook(NativeMethods.EventObjectNameChange, NativeMethods.EventObjectNameChange, IntPtr.Zero, _callback, 0, 0, NativeMethods.WinEventOutOfContext | NativeMethods.WinEventSkipOwnProcess));
        _hooks.Add(NativeMethods.SetWinEventHook(NativeMethods.EventSystemForeground, NativeMethods.EventSystemForeground, IntPtr.Zero, _callback, 0, 0, NativeMethods.WinEventOutOfContext | NativeMethods.WinEventSkipOwnProcess));
        _hooks.Add(NativeMethods.SetWinEventHook(NativeMethods.EventSystemMinimizeStart, NativeMethods.EventSystemMinimizeEnd, IntPtr.Zero, _callback, 0, 0, NativeMethods.WinEventOutOfContext | NativeMethods.WinEventSkipOwnProcess));
        IsHooked = _hooks.TrueForAll(hook => hook != IntPtr.Zero);
        _worker = new Thread(Loop)
        {
            IsBackground = true,
            Name = "BELAC.Dock.Windows"
        };
        _worker.Start();
        _signal.Set();
    }

    public void Dispose()
    {
        _stop = true;
        _signal.Set();
        _worker?.Join(1500);
        foreach (var hook in _hooks)
        {
            if (hook != IntPtr.Zero)
                NativeMethods.UnhookWinEvent(hook);
        }

        _hooks.Clear();
        _signal.Dispose();
        IsHooked = false;
    }

    private void OnEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (idObject != 0 && eventType is not (NativeMethods.EventSystemForeground or NativeMethods.EventSystemMinimizeStart or NativeMethods.EventSystemMinimizeEnd))
            return;

        _signal.Set();
    }

    private void Loop()
    {
        while (!_stop)
        {
            _signal.WaitOne(3000);
            if (_stop)
                break;

            Thread.Sleep(200);
            while (_signal.WaitOne(0))
            {
            }

            if (_stop)
                break;

            try
            {
                Publish(Capture());
            }
            catch
            {
                // The next wake retries. A single enumeration failure must not stop tracking.
            }
        }
    }

    private List<WindowObservation> Capture()
    {
        string[] managed;
        lock (_gate)
            managed = _managedPaths;

        var cache = new Dictionary<int, ProcessFacts>();
        var windows = new List<WindowObservation>();
        NativeMethods.EnumWindowsDelegate callback = (hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
                return true;

            NativeMethods.GetWindowThreadProcessId(hwnd, out var pidValue);
            var pid = (int)pidValue;
            if (pid == Environment.ProcessId)
                return true;

            if (!cache.TryGetValue(pid, out var facts))
            {
                facts = ProcessFacts.Query(pid);
                cache[pid] = facts;
            }

            var exStyle = NativeMethods.ReadExStyle(hwnd);
            var observation = new WindowObservation
            {
                Hwnd = hwnd.ToInt64(),
                ProcessId = pid,
                ProcessStartUtc = facts.StartUtc,
                ExecutablePath = facts.ExecutablePath,
                ProcessName = facts.Name,
                Aumid = WindowAppIdentity.Read(hwnd) ?? facts.Aumid ?? ShellAppIdentity.Resolve(hwnd, pid, facts.StartUtc) ?? DesktopAppIdentity.Find(facts.ExecutablePath),
                Title = NativeMethods.ReadTitle(hwnd),
                ClassName = NativeMethods.ReadClass(hwnd),
                IsVisible = true,
                IsMinimized = NativeMethods.IsIconic(hwnd),
                IsCloaked = WindowInspector.IsCloaked(hwnd),
                HasOwner = NativeMethods.GetWindow(hwnd, NativeMethods.GwOwner) != IntPtr.Zero,
                IsToolWindow = (exStyle & NativeMethods.WsExToolWindow) != 0,
                IsAppWindow = (exStyle & NativeMethods.WsExAppWindow) != 0,
                AccessDenied = facts.Denied
            };

            if (WindowFilters.ShouldTrack(observation, path => managed.Any(item => AppMatcher.PathsEqual(item, path))))
                windows.Add(observation);

            return true;
        };

        NativeMethods.EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return windows;
    }

    private void Publish(List<WindowObservation> windows)
    {
        var distinct = WindowIdentity.DistinctByHwnd(windows);
        var foreground = NativeMethods.GetForegroundWindow().ToInt64();
        var taskbar = TaskbarMetrics.CurrentTaskbarHandle();
        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
            return;

        dispatcher.BeginInvoke(() =>
        {
            Windows = distinct;
            ForegroundHwnd = foreground;
            TaskbarHwnd = taskbar;
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }
}
