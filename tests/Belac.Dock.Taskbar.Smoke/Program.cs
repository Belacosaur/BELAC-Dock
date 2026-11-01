using System.IO;
using Belac.Dock.Windows;
using Belac.Dock.Windows.Native;
using Belac.Dock.Infrastructure;
using Belac.Dock.Core.Abstractions;
using Belac.Dock.Core.Models;

// Opt-in interactive test. Temporarily hides only the specified process's single window;
// restores it in finally. Never changes user settings or enables style compatibility.
internal static class Program
{
    [STAThread] static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--fixture") return Lifecycle.Fixture(args);
        if (args.Length == 2 && args[0] == "--host-lifecycle") return HostLifecycle.Run(args[1]);
        if (args.Length > 0 && args[0] == "--lifecycle") return Lifecycle.Run();
        if (args.Length != 2 || args[0] != "--process")
        { Console.Error.WriteLine("Usage: Belac.Dock.Taskbar.Smoke --process <running process name>"); return 2; }
        var inspector = new WindowInspector();
        var windows = new List<WindowObservation>();
        NativeMethods.EnumWindows((h, _) =>
        {
            if (inspector.TryInspect(h.ToInt64(), out var w) && w.ProcessName.Equals(args[1], StringComparison.OrdinalIgnoreCase)
                && w.IsVisible && !string.IsNullOrWhiteSpace(w.Title) && !w.HasOwner) windows.Add(w);
            return true;
        }, IntPtr.Zero);
        if (windows.Count != 1) { Console.Error.WriteLine("Expected exactly one visible main window; found " + windows.Count); return 2; }
        var window = windows[0];
        var path = Path.GetFullPath(Path.Combine("artifacts", "taskbar-test-" + Guid.NewGuid().ToString("N"), "journal.json"));
        var store = new JsonRecoveryJournalStore(path);
        using var service = new TaskbarSeparationService(store, inspector);
        var app = new DockApp { ExePath = window.ExecutablePath, TaskbarMode = TaskbarMode.DockOnly };
        var originalStyle = NativeMethods.ReadExStyle(new IntPtr(window.Hwnd));
        try
        {
            Check(Probe(window) == TaskbarProbeResult.Present, "Identifies existing running taskbar button");
            using (var failing = new TaskbarSeparationService(new FailingJournal(), inspector))
            {
                var failure = WaitForResult(failing, app, window);
                Check(failure.Status == TaskbarStatus.Error && failing.Snapshots.Count == 0, "Journal failure prevents mutation and leaves no phantom recovery entry");
                Check(Probe(window) == TaskbarProbeResult.Present, "Journal failure leaves taskbar button visible");
            }
            var denied = new WindowObservation { Hwnd=window.Hwnd, ProcessId=window.ProcessId, ProcessStartUtc=window.ProcessStartUtc, ExecutablePath=window.ExecutablePath, AccessDenied=true };
            service.Apply(app, denied); service.Apply(app, denied);
            Check(service.Apply(app, window).Status == TaskbarStatus.Unsupported, "Repeated failures stop additional mutation attempts");
            Check(!service.RestoreOne(window), "Mirror resets failure latch even without a recovery entry");
            Check(WaitForResult(service, app, window).Status == TaskbarStatus.DockOnly, "Normal API hides and verifies taskbar button");
            Check(Probe(window) is TaskbarProbeResult.Absent or TaskbarProbeResult.PinnedOnly, "Running taskbar button is absent");
            Check(NativeMethods.ReadExStyle(new IntPtr(window.Hwnd)) == originalStyle, "Normal hide preserves extended styles and Alt+Tab eligibility");
            var assembly = typeof(TaskbarSeparationService).Assembly;
            var contract = assembly.GetType("Belac.Dock.Windows.Native.ITaskbarList")!;
            var shell = assembly.GetType("Belac.Dock.Windows.Native.TaskbarListFactory")!.GetMethod("Create")!.Invoke(null, null)!;
            try
            {
                contract.GetMethod("HrInit")!.Invoke(shell, null);
                contract.GetMethod("AddTab")!.Invoke(shell, [new IntPtr(window.Hwnd)]);
            }
            finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(shell); }
            Check(Probe(window) == TaskbarProbeResult.Present, "Simulates Windows recreating the taskbar button");
            Thread.Sleep(3200);
            Check(WaitForResult(service, app, window).Status == TaskbarStatus.DockOnly && Probe(window) is TaskbarProbeResult.Absent or TaskbarProbeResult.PinnedOnly, "Rechecks and hides a recreated taskbar button");
            Check(service.RestoreOne(window) && Probe(window) == TaskbarProbeResult.Present, "Mirror restores the taskbar button");
            Check(WaitForResult(service, app, window).Status == TaskbarStatus.DockOnly, "Dock-only works again after Mirror");
            using var recovery = new TaskbarSeparationService(store, inspector);
            Check(recovery.RestoreAll(h => inspector.TryInspect(h, out var live) ? live : null) == 1, "New service restores persisted recovery snapshot");
            Check(Probe(window) == TaskbarProbeResult.Present && store.Load().Count == 0, "Recovery restores button and clears journal");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            service.RestoreOne(window);
            Console.WriteLine("Final button state: " + Probe(window));
        }
    }
    private static SeparationResult WaitForResult(TaskbarSeparationService service, DockApp app, WindowObservation window)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        SeparationResult result;
        do { result = service.Apply(app, window); if(result.Status != TaskbarStatus.Unknown) return result; Thread.Sleep(250); } while(DateTime.UtcNow < until);
        throw new TimeoutException(result.Detail);
    }
    private static TaskbarProbeResult Probe(WindowObservation window)
    {
        TaskbarButtonProbe.Invalidate();
        for(var i=0;i<40;i++) { var result=TaskbarButtonProbe.LookForWindow(window); if(result!=TaskbarProbeResult.Unavailable)return result; Thread.Sleep(100); }
        return TaskbarProbeResult.Unavailable;
    }
    private static void Check(bool success, string label)
    { Console.WriteLine((success ? "PASS " : "FAIL ") + label); if(!success)throw new InvalidOperationException(label); }
    private sealed class FailingJournal : IRecoveryJournalStore
    {
        public IReadOnlyList<WindowMutationSnapshot> Load() => [];
        public void Save(IReadOnlyList<WindowMutationSnapshot> entries) => throw new IOException("Simulated disk failure");
    }
}
