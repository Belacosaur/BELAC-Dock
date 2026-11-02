using System.Runtime.InteropServices;
using Belac.Dock.Core.Abstractions;
using Belac.Dock.Core.Matching;
using Belac.Dock.Core.Models;
using Belac.Dock.Windows.Native;

namespace Belac.Dock.Windows;

public sealed class TaskbarSeparationService : ITaskbarSeparationService, IDisposable
{
    private readonly IRecoveryJournalStore _journal;
    private readonly IWindowInspector _inspector;
    private readonly List<WindowMutationSnapshot> _entries;
    private readonly Dictionary<string, int> _failures = new(StringComparer.Ordinal);
    private readonly List<string> _events = [];
    private ITaskbarList? _taskbar;
    private readonly Dictionary<string, DateTime> _attemptStarted = [];
    private readonly Dictionary<string, DateTime> _verified = [];
    private readonly Dictionary<string, SeparationResult> _verifiedResults = [];
    private readonly Dictionary<string, int> _reassertions = [];
    private readonly Dictionary<string, string> _failureDetails = [];
    private readonly Action<string>? _diagnostic;

    public TaskbarSeparationService(IRecoveryJournalStore journal, IWindowInspector inspector, Action<string>? diagnostic = null)
    {
        _diagnostic = diagnostic;
        _journal = journal;
        _inspector = inspector;
        _entries = journal.Load().ToList();
        foreach (var entry in _entries) _verified[$"{entry.Hwnd:X}:{entry.ProcessId}:{entry.ProcessStartUtc.Ticks}"] = DateTime.MinValue;
    }

    public bool IsPaused { get; private set; }
    public string LastDetail { get; private set; } = "Mirror mode.";
    public IReadOnlyList<string> RecentEvents => _events;
    public IReadOnlyList<WindowMutationSnapshot> Snapshots => _entries;

    public void ForgetAll()
    {
        _entries.Clear();
        Persist();
        LastDetail = "Recovery snapshots were discarded.";
        Note(LastDetail);
    }

    public void SetPaused(bool paused) => IsPaused = paused;

    public IReadOnlyList<SeparationResult> ApplyBatch(DockApp app, IReadOnlyList<WindowObservation> windows)
    {
        using var batch = TaskbarButtonProbe.Batch();
        var results = new List<SeparationResult>();
        var newWindows = windows.Where(window => !_entries.Any(entry => JournalValidator.IsSameWindow(entry, window))).ToArray();
        foreach (var window in newWindows) results.Add(Apply(app, window));
        var startedNew = results.Any(result => result.Status == TaskbarStatus.Unknown);
        foreach (var window in windows.Except(newWindows))
            results.Add(startedNew && app.TaskbarMode == TaskbarMode.DockOnly
                ? new SeparationResult(TaskbarStatus.Unknown, "Updating all windows in this app's taskbar group…", false)
                : Apply(app, window));
        return results;
    }

    public void Reconcile(IReadOnlyList<DockApp> apps)
    {
        var removed = false;
        foreach (var entry in _entries.ToArray())
        {
            if (!_inspector.TryInspect(entry.Hwnd, out var live))
            {
                if (!NativeMethods.IsWindow(new IntPtr(entry.Hwnd))) { _entries.Remove(entry); removed = true; }
                continue;
            }
            if (!JournalValidator.IsSameWindow(entry, live)) { _entries.Remove(entry); removed = true; continue; }
            var app = AppMatcher.Match(apps, live).App;
            if (IsPaused || app is null || app.TaskbarMode != TaskbarMode.DockOnly || (entry.Method == "ExStyle" && !app.AllowStyleCompatibility))
                RestoreOne(live);
        }
        if (removed) Persist();
        var active = _entries.Select(entry => $"{entry.Hwnd:X}:{entry.ProcessId}:{entry.ProcessStartUtc.Ticks}").ToHashSet();
        // Closed/restarted processes must not retain a previous instance's retry latch.
        var known = _failures.Keys.Concat(_verified.Keys).Concat(_attemptStarted.Keys).Concat(_reassertions.Keys).Distinct().ToArray();
        foreach (var key in known.Where(key => !active.Contains(key)))
        {
            var hwndText = key.Split(':')[0];
            if (!long.TryParse(hwndText, System.Globalization.NumberStyles.HexNumber, null, out var hwnd)) continue;
            var inspected = _inspector.TryInspect(hwnd, out var live);
            if ((!inspected && !NativeMethods.IsWindow(new IntPtr(hwnd))) || (inspected && Identity(live) != key))
            { _failures.Remove(key); _failureDetails.Remove(key); _attemptStarted.Remove(key); _verified.Remove(key); _verifiedResults.Remove(key); _reassertions.Remove(key); }
        }
    }

    public SeparationResult Apply(DockApp app, WindowObservation window)
    {
        if (IsPaused || app.TaskbarMode == TaskbarMode.Mirror)
        {
            RestoreOne(window);
            return Finish(TaskbarStatus.Mirrored, IsPaused ? "Taskbar separation is paused." : "Mirror mode.", false);
        }

        if (window.AccessDenied)
            return Fail(window, "Elevated or protected window. Left unchanged.");

        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            return Fail(window, "Taskbar changes must run on the UI thread.");

        var hwnd = new IntPtr(window.Hwnd);
        if (!NativeMethods.IsWindow(hwnd) || !_inspector.TryInspect(window.Hwnd, out var live) || !JournalValidator.IsSameWindow(ToSnapshot(window, 0, "Check"), live))
            return Fail(window, "Window identity changed. Left unchanged.");

        if (!live.IsVisible && !live.IsMinimized)
            return Fail(window, "The window is not visible, so it was left unchanged.");

        var key = Identity(live);
        var now = DateTime.UtcNow;
        if (_failures.GetValueOrDefault(key) >= 2)
            return Finish(TaskbarStatus.Unsupported, "Dock-only failed twice: " + _failureDetails.GetValueOrDefault(key, "Windows rejected the change.") + " Switch to Mirror, then Dock only to retry.", false);

        var entry = _entries.FirstOrDefault(candidate => JournalValidator.IsSameWindow(candidate, live));
        if (entry is not null)
        {
            ParkMinimizedWindow(live);
            if (_verified.TryGetValue(key, out var checkedAt) && now - checkedAt < TimeSpan.FromSeconds(3) && _verifiedResults.TryGetValue(key, out var confirmed))
                return Finish(confirmed.Status, confirmed.Detail, confirmed.ChangedStyle);
            var probe = TaskbarButtonProbe.LookForWindow(live);
            _attemptStarted.TryAdd(key, now);
            var started = _attemptStarted[key];
            if (probe is TaskbarProbeResult.Absent or TaskbarProbeResult.PinnedOnly)
            {
                _verified[key] = now;
                _attemptStarted[key] = now;
                _reassertions[key] = 0;
                _failures.Remove(key);
                var result = Finish(TaskbarStatus.DockOnly, (probe == TaskbarProbeResult.PinnedOnly ? "Running window hidden; its pinned Windows launcher remains. Unpin it in Windows to remove that icon." : "Taskbar button verified hidden.") + (entry.Method == "ExStyle" ? " Compatibility fallback is active; Alt+Tab is hidden too." : " Alt+Tab is unchanged."), entry.Method == "ExStyle");
                _verifiedResults[key] = result;
                return result;
            }
            if (probe == TaskbarProbeResult.Unavailable && now - started < TimeSpan.FromSeconds(10))
                return Finish(TaskbarStatus.Unknown, "Waiting for Windows to verify the taskbar button…", false);
            if (probe == TaskbarProbeResult.Present && _verified.Remove(key) && _reassertions.GetValueOrDefault(key) < 2)
            {
                _reassertions[key] = _reassertions.GetValueOrDefault(key) + 1;
                _attemptStarted[key] = now;
                if (TryDeleteTab(hwnd)) return Finish(TaskbarStatus.Unknown, "Windows recreated the taskbar button; hiding it again…", false);
            }
            if (probe == TaskbarProbeResult.Present && now - started < TimeSpan.FromMilliseconds(700))
                return Finish(TaskbarStatus.Unknown, "Waiting for Windows to update the taskbar…", false);
            if (probe == TaskbarProbeResult.Present && entry.Method == "DeleteTab" && app.AllowStyleCompatibility)
            {
                if (!Remember(live, entry.OriginalExStyle, "ExStyle"))
                    return Finish(TaskbarStatus.Error, "Could not save recovery information. Compatibility fallback was not applied.", false);
                _verified.Remove(key);
                _attemptStarted[key] = now;
                if (ApplyToolStyle(hwnd, entry.OriginalExStyle, live.IsMinimized))
                    return Finish(TaskbarStatus.Unknown, "Verifying compatibility fallback; Alt+Tab is hidden too…", true);
            }
            var reason = probe == TaskbarProbeResult.Present
                ? "Windows kept the taskbar button visible." + (app.AllowStyleCompatibility ? " Compatibility fallback did not verify." : " Compatibility fallback is off.")
                : "Windows taskbar verification is unavailable; success could not be confirmed.";
            if (!RollBack(live)) return Finish(TaskbarStatus.Error, "Could not restore the taskbar button. Recovery information was retained.", false);
            CountFailure(key);
            _failureDetails[key] = reason;
            return Finish(TaskbarStatus.Unsupported, reason, false);
        }

        // Confirm we can identify this application's button before mutating it.
        var before = TaskbarButtonProbe.LookForWindow(live);
        if (before != TaskbarProbeResult.Present)
            return Finish(TaskbarStatus.Unknown, before == TaskbarProbeResult.Absent
                ? "No matching taskbar button could be identified. Dock-only has not been claimed."
                : "Waiting for Windows taskbar verification…", false);
        var originalStyle = NativeMethods.ReadExStyle(hwnd);
        if (!Remember(live, originalStyle, "DeleteTab"))
            return Finish(TaskbarStatus.Error, "Could not write the recovery journal. The window was not changed.", false);
        _attemptStarted[key] = now;
        if (TryDeleteTab(hwnd))
        {
            ParkMinimizedWindow(live);
            return Finish(TaskbarStatus.Unknown, "Hiding taskbar button; waiting for verification…", false);
        }
        if (app.AllowStyleCompatibility && Remember(live, originalStyle, "ExStyle") && ApplyToolStyle(hwnd, originalStyle, live.IsMinimized))
            return Finish(TaskbarStatus.Unknown, "Verifying compatibility fallback; Alt+Tab is hidden too…", true);
        if (!RollBack(live)) return Finish(TaskbarStatus.Error, "Taskbar API failed and restoration could not be confirmed. Recovery information was retained.", false);
        CountFailure(key);
        _failureDetails[key] = "Windows rejected the taskbar API call.";
        return Finish(TaskbarStatus.Unsupported, _failureDetails[key], false);
    }

    private bool RollBack(WindowObservation window)
    {
        var key = Identity(window);
        var failures = _failures.GetValueOrDefault(key);
        var restored = RestoreOne(window);
        _failures[key] = failures;
        return restored;
    }

    // Windows normally parks taskbar-managed minimized windows outside the desktop.
    // DeleteTab / TOOLWINDOW can leave a legacy caption at the bottom left instead.
    // Move only that iconic representation; do not hide the HWND or change its restore bounds.
    public void ParkMinimizedWindow(WindowObservation window)
    {
        if (IsPaused || !_entries.Any(entry => JournalValidator.IsSameWindow(entry, window))) return;
        var hwnd = new IntPtr(window.Hwnd);
        if (!NativeMethods.IsIconic(hwnd) || !_inspector.TryInspect(window.Hwnd, out var live)
            || !JournalValidator.IsSameWindow(ToSnapshot(window, 0, "Check"), live)) return;
        if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return;
        var monitors = new MonitorCatalog().GetMonitors();
        if (!monitors.Any(m => rect.Left < m.Bounds.Right && rect.Right > m.Bounds.Left && rect.Top < m.Bounds.Bottom && rect.Bottom > m.Bounds.Top)) return;
        var left = (int)Math.Min(-32000, monitors.Min(m => m.Bounds.Left) - Math.Max(1000, rect.Right - rect.Left + 100));
        var top = (int)Math.Min(-32000, monitors.Min(m => m.Bounds.Top) - Math.Max(1000, rect.Bottom - rect.Top + 100));
        if (NativeMethods.IsIconic(hwnd))
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, left, top, 0, 0,
                NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }

    public void OnMinimizeStateChanged(WindowObservation window)
    {
        if (IsPaused || !_entries.Any(entry => JournalValidator.IsSameWindow(entry, window))) return;
        if (window.IsMinimized) ParkMinimizedWindow(window);
        else
        {
            // Restoring can cause Explorer to recreate the taskbar button.
            TryDeleteTab(new IntPtr(window.Hwnd));
            _verified.Remove(Identity(window));
        }
    }

    public bool RestoreOne(WindowObservation window)
    {
        var key = Identity(window);
        _failures.Remove(key);
        _failureDetails.Remove(key);
        _verified.Remove(key);
        _verifiedResults.Remove(key);
        _attemptStarted.Remove(key);
        _reassertions.Remove(key);
        var entry = _entries.FirstOrDefault(candidate => JournalValidator.IsSameWindow(candidate, window));
        if (entry is null)
            return false;

        return RestoreEntry(entry, window);
    }

    public int RestoreAll(Func<long, WindowObservation?> probe)
    {
        var restored = 0;
        foreach (var entry in _entries.ToArray())
        {
            var live = probe(entry.Hwnd);
            if (live is null)
            {
                if (!NativeMethods.IsWindow(new IntPtr(entry.Hwnd))) _entries.Remove(entry);
                continue; // Keep recovery data when a live window cannot currently be inspected.
            }
            if (!JournalValidator.IsSameWindow(entry, live))
            {
                _entries.Remove(entry);
                continue;
            }

            if (RestoreEntry(entry, live))
                restored++;
        }

        Persist();
        LastDetail = restored == 0 ? "No managed window styles needed restoring." : $"Restored {restored} window style{(restored == 1 ? "" : "s")}.";
        Note(LastDetail);
        return restored;
    }

    public void Dispose()
    {
        if (_taskbar is not null && Marshal.IsComObject(_taskbar))
            Marshal.ReleaseComObject(_taskbar);
        _taskbar = null;
    }

    private bool RestoreEntry(WindowMutationSnapshot entry, WindowObservation window)
    {
        var hwnd = new IntPtr(window.Hwnd);
        if (!NativeMethods.IsWindow(hwnd))
        {
            _entries.Remove(entry);
            Persist();
            return false;
        }

        if (string.Equals(entry.Method, "DeleteTab", StringComparison.Ordinal) && !TryAddTab(hwnd))
            return false;

        if (string.Equals(entry.Method, "ExStyle", StringComparison.Ordinal))
        {
            RestoreStyle(hwnd, entry.OriginalExStyle, entry.WasVisible);
            if (NativeMethods.ReadExStyle(hwnd) != entry.OriginalExStyle) return false;
            if (!TryAddTab(hwnd)) return false;
        }

        _entries.Remove(entry);
        _failures.Remove(Identity(window));
        try { Persist(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _entries.Add(entry);
            Note("Window restored, but recovery journal cleanup failed. Recovery data was retained.");
        }
        return true;
    }

    private bool ApplyToolStyle(IntPtr hwnd, int originalStyle, bool minimized)
    {
        var updated = (originalStyle | NativeMethods.WsExToolWindow) & ~NativeMethods.WsExAppWindow;
        NativeMethods.ShowWindow(hwnd, NativeMethods.SwHide);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, updated);
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged);
        NativeMethods.ShowWindow(hwnd, minimized ? NativeMethods.SwShowMinNoActive : NativeMethods.SwShowNa);
        if (minimized && _inspector.TryInspect(hwnd.ToInt64(), out var minimizedWindow)) ParkMinimizedWindow(minimizedWindow);
        var now = NativeMethods.ReadExStyle(hwnd);
        var available = NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd);
        TaskbarButtonProbe.Invalidate();
        return available && (now & NativeMethods.WsExToolWindow) != 0;
    }

    private static void RestoreStyle(IntPtr hwnd, int originalStyle, bool wasVisible)
    {
        var minimized = NativeMethods.IsIconic(hwnd);
        if (wasVisible) NativeMethods.ShowWindow(hwnd, NativeMethods.SwHide);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, originalStyle);
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged);
        if (wasVisible)
            NativeMethods.ShowWindow(hwnd, minimized ? NativeMethods.SwShowMinNoActive : NativeMethods.SwShowNa);
    }

    private bool TryDeleteTab(IntPtr hwnd)
    {
        try
        {
            var list = Taskbar();
            var initialized = list.HrInit();
            var result = initialized < 0 ? initialized : list.DeleteTab(hwnd);
            if (result < 0) { Note($"DeleteTab failed (0x{result:X8})."); Dispose(); }
            if (result >= 0) TaskbarButtonProbe.Invalidate();
            return result >= 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or InvalidCastException)
        {
            Note($"DeleteTab failed: {ex.GetType().Name} (0x{ex.HResult:X8}).");
            Dispose();
            return false;
        }
    }

    private bool TryAddTab(IntPtr hwnd)
    {
        try
        {
            var list = Taskbar();
            var initialized = list.HrInit();
            var result = initialized < 0 ? initialized : list.AddTab(hwnd);
            if (result < 0) { Note($"AddTab failed (0x{result:X8})."); Dispose(); }
            if (result >= 0) TaskbarButtonProbe.Invalidate();
            return result >= 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or InvalidCastException)
        {
            Note($"AddTab failed: {ex.GetType().Name} (0x{ex.HResult:X8}).");
            Dispose();
            return false;
        }
    }

    private ITaskbarList Taskbar() => _taskbar ??= TaskbarListFactory.Create();

    private bool Remember(WindowObservation window, int originalStyle, string method)
    {
        var previous = _entries.ToList();
        try
        {
            _entries.RemoveAll(entry => entry.Hwnd == window.Hwnd && entry.ProcessId == window.ProcessId);
            _entries.Add(new WindowMutationSnapshot
            {
                Hwnd = window.Hwnd,
                ProcessId = window.ProcessId,
                ProcessStartUtc = window.ProcessStartUtc,
                ExecutablePath = window.ExecutablePath,
                OriginalExStyle = originalStyle,
                WasVisible = window.IsVisible || window.IsMinimized,
                Method = method,
                CapturedUtc = DateTime.UtcNow
            });
            Persist();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _entries.Clear();
            _entries.AddRange(previous);
            return false;
        }
    }

    private void Persist() => _journal.Save(_entries);

    private void CountFailure(string key) => _failures[key] = _failures.GetValueOrDefault(key) + 1;

    private SeparationResult Fail(WindowObservation window, string detail)
    {
        CountFailure(Identity(window));
        _failureDetails[Identity(window)] = detail;
        return Finish(TaskbarStatus.Unsupported, detail, false);
    }

    private SeparationResult Finish(TaskbarStatus status, string detail, bool changed)
    {
        LastDetail = detail;
        Note(detail);
        return new SeparationResult(status, detail, changed);
    }

    private void Note(string detail)
    {
        if (_events.LastOrDefault()?.EndsWith(detail, StringComparison.Ordinal) == true) return;
        _diagnostic?.Invoke(detail);
        _events.Add($"{DateTimeOffset.Now:HH:mm:ss} {detail}");
        if (_events.Count > 40)
            _events.RemoveRange(0, _events.Count - 40);
    }

    private static string Identity(WindowObservation window) => $"{window.Hwnd:X}:{window.ProcessId}:{window.ProcessStartUtc.Ticks}";

    private static WindowMutationSnapshot ToSnapshot(WindowObservation window, int style, string method) => new()
    {
        Hwnd = window.Hwnd,
        ProcessId = window.ProcessId,
        ProcessStartUtc = window.ProcessStartUtc,
        ExecutablePath = window.ExecutablePath,
        OriginalExStyle = style,
        Method = method
    };
}

internal static class ObservationVisibility
{
    public static bool WasVisible(this WindowObservation window) => window.IsVisible || window.IsMinimized;
}
