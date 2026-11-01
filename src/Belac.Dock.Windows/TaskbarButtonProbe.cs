using System.Runtime.InteropServices;
using System.Windows.Automation;
using Belac.Dock.Core.Models;

namespace Belac.Dock.Windows;

public enum TaskbarProbeResult { Absent, Present, Unavailable, PinnedOnly }

public static class TaskbarButtonProbe
{
    private sealed record ButtonInfo(string Id, string Name);
    private sealed record Snapshot(DateTime Captured, bool Available, ButtonInfo[] Buttons);
    private static readonly object Gate = new();
    private static Task<Snapshot>? _pending;
    private static Snapshot? _snapshot;
    private static int _generation;
    private static int _pendingGeneration;
    private static int _batchDepth;
    private static bool _dirty;

    public static TaskbarProbeResult LookForTitle(string? title, int processId)
        => LookForWindow(new WindowObservation { Title = title ?? "", ProcessId = processId });

    public static TaskbarProbeResult LookForWindow(WindowObservation window)
    {
        if (window.ProcessId == Environment.ProcessId) return TaskbarProbeResult.Unavailable;
        lock (Gate)
        {
            if (_pending is { IsCompleted: true })
            {
                var result = _pending.GetAwaiter().GetResult();
                if (_pendingGeneration == _generation) _snapshot = result;
                _pending = null;
            }
            if (_pending is null && (_snapshot is null || DateTime.UtcNow - _snapshot.Captured > TimeSpan.FromMilliseconds(500)))
            {
                _pendingGeneration = _generation;
                _pending = Task.Run(Capture);
            }
            if (_snapshot is not { Available: true } snapshot || DateTime.UtcNow - snapshot.Captured > TimeSpan.FromSeconds(3))
                return TaskbarProbeResult.Unavailable;
            var matching = snapshot.Buttons.Where(button => MatchesAppId(button.Id, window.ExecutablePath, window.Aumid)
                || (!string.IsNullOrWhiteSpace(window.Title) && button.Name == window.Title)).ToArray();
            if (matching.Any(button => !IsPinnedLauncherOnly(button.Name))) return TaskbarProbeResult.Present;
            if (matching.Length > 0) return TaskbarProbeResult.PinnedOnly;
            return !string.IsNullOrWhiteSpace(window.ExecutablePath) || !string.IsNullOrWhiteSpace(window.Aumid)
                ? TaskbarProbeResult.Absent : TaskbarProbeResult.Unavailable;
        }
    }

    // One taskbar capture serves every app/window. Mutations invalidate pre-change results.
    public static void Invalidate()
    {
        lock (Gate)
        {
            if (_batchDepth > 0) { _dirty = true; return; }
            _generation++;
            _snapshot = null;
        }
    }
    internal static IDisposable Batch()
    {
        lock (Gate) _batchDepth++;
        return new BatchScope();
    }
    private sealed class BatchScope : IDisposable
    {
        public void Dispose()
        {
            lock (Gate)
                if (--_batchDepth == 0 && _dirty) { _dirty = false; Invalidate(); }
        }
    }

    private static Snapshot Capture()
    {
        try
        {
            var bars = AutomationElement.RootElement.FindAll(TreeScope.Children, new OrCondition(
                new PropertyCondition(AutomationElement.ClassNameProperty, "Shell_TrayWnd"),
                new PropertyCondition(AutomationElement.ClassNameProperty, "Shell_SecondaryTrayWnd")));
            var result = new List<ButtonInfo>();
            var supported = false;
            foreach (AutomationElement bar in bars)
            {
                var buttons = bar.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                foreach (AutomationElement button in buttons)
                {
                    var current = button.Current;
                    if (current.ClassName != "Taskbar.TaskListButtonAutomationPeer") continue;
                    supported = true;
                    result.Add(new ButtonInfo(current.AutomationId, current.Name));
                }
            }
            return new Snapshot(DateTime.UtcNow, supported, result.ToArray());
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or UnauthorizedAccessException or InvalidOperationException or COMException)
        {
            return new Snapshot(DateTime.UtcNow, false, []);
        }
    }

    public static bool IsPinnedLauncherOnly(string accessibleName)
    {
        // Recognize the observed English shell format only. Unknown/localized formats remain
        // present/unverified rather than guessing that a running button has disappeared.
        return accessibleName.EndsWith(" pinned", StringComparison.Ordinal)
            && !System.Text.RegularExpressions.Regex.IsMatch(accessibleName, @" - \d+ running window");
    }

    public static bool MatchesAppId(string automationId, string executablePath, string? aumid)
    {
        const string prefix = "Appid: ";
        if (!automationId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var id = automationId[prefix.Length..];
        if (!string.IsNullOrWhiteSpace(aumid)) return string.Equals(id, aumid, StringComparison.OrdinalIgnoreCase);
        if (id.StartsWith('{') && id.IndexOf('}') is var end && end > 0 && Guid.TryParse(id[..(end+1)], out var folder))
        {
            if (SHGetKnownFolderPath(ref folder, 0, IntPtr.Zero, out var path) == 0)
            {
                try { id = (Marshal.PtrToStringUni(path) ?? "") + id[(end+1)..]; }
                finally { Marshal.FreeCoTaskMem(path); }
            }
        }
        return !string.IsNullOrWhiteSpace(executablePath) && string.Equals(id, executablePath, StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
}
