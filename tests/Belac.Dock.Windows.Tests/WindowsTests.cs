using System.IO;
using Belac.Dock.Core.Models;
using Belac.Dock.Infrastructure;
using Belac.Dock.Windows;

namespace Belac.Dock.Windows.Tests;

public class DesktopQueryTests
{
    [Fact]
    public void Monitors_and_taskbar_are_present()
    {
        var monitors = new MonitorCatalog().GetMonitors();
        Assert.NotEmpty(monitors);
        Assert.Contains(monitors, monitor => monitor.Primary);
        Assert.True(monitors[0].Work.Width > 100);
        Assert.True(monitors[0].Dpi >= 96);
        Assert.True(TaskbarMetrics.TryGetTaskbarRect(out var taskbar));
        Assert.True(taskbar.Width > 0);
        Assert.True(taskbar.Height > 0);
    }

    [Fact]
    public void Notepad_icon_extracts_to_png()
    {
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var png = new IconExtractor().GetPng(notepad, 64);
        Assert.NotNull(png);
        Assert.True(png!.Length > 32);
        Assert.Equal(0x89, png[0]);
    }

    [Fact]
    public void Shortcut_resolves_to_the_target_without_launching_it()
    {
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var directory = Path.Combine(Path.GetTempPath(), "belac-dock-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var shortcut = Path.Combine(directory, "Notepad.lnk");
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        Assert.NotNull(shellType);
        dynamic shell = Activator.CreateInstance(shellType!)!;
        dynamic link = shell.CreateShortcut(shortcut);
        link.TargetPath = notepad;
        link.Arguments = "/belac-test";
        link.WorkingDirectory = Environment.SystemDirectory;
        link.Save();

        var resolved = new ShortcutResolver().Resolve(shortcut);
        Assert.NotNull(resolved);
        Assert.EndsWith("notepad.exe", resolved!.TargetPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/belac-test", resolved.Arguments);
    }
}

public class SingleInstanceTests
{
    [Fact]
    public void Second_instance_signals_the_first()
    {
        var name = "BELAC.Dock.Test." + Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceManager(name);
        SingleInstanceManager? second = null;
        var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            second = new SingleInstanceManager(name);
            ready.Set();
        });
        thread.Start();
        Assert.True(ready.Wait(TimeSpan.FromSeconds(3)));
        Assert.True(first.IsPrimary);
        Assert.NotNull(second);
        Assert.False(second!.IsPrimary);
        using var signaled = new ManualResetEventSlim();
        first.Activated += (_, _) => signaled.Set();
        second.Signal();
        Assert.True(signaled.Wait(TimeSpan.FromSeconds(3)));
        second.Dispose();
        thread.Join(TimeSpan.FromSeconds(2));
    }
}

public class SeparationSafetyTests
{
    [Fact]
    public void Access_denied_window_is_not_a_candidate_for_mutation()
    {
        var root = Path.Combine(Path.GetTempPath(), "belac-dock-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var service = new TaskbarSeparationService(new JsonRecoveryJournalStore(Path.Combine(root, "journal.json")), new WindowInspector());
        var app = new DockApp
        {
            DisplayName = "Protected",
            ExePath = @"C:\apps\protected.exe",
            TaskbarMode = TaskbarMode.DockOnly,
            AllowStyleCompatibility = true
        };
        var window = new WindowObservation
        {
            Hwnd = 42,
            ProcessId = 99,
            ProcessStartUtc = DateTime.UnixEpoch,
            ExecutablePath = app.ExePath,
            AccessDenied = true,
            IsVisible = true,
            Title = "Protected"
        };

        var result = service.Apply(app, window);
        Assert.Equal(TaskbarStatus.Unsupported, result.Status);
        Assert.False(result.ChangedStyle);
        Assert.Contains("unchanged", result.Detail, StringComparison.OrdinalIgnoreCase);
    }
}

public class TaskbarInteropRegressionTests
{
    [Fact]
    public void Taskbar_interface_can_be_created_and_initialized()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var assembly = typeof(TaskbarSeparationService).Assembly;
                var contract = assembly.GetType("Belac.Dock.Windows.Native.ITaskbarList")!;
                Assert.Equal(new Guid("56FDF342-FD6D-11D0-958A-006097C9A090"), contract.GUID);
                var factory = assembly.GetType("Belac.Dock.Windows.Native.TaskbarListFactory")!;
                var instance = factory.GetMethod("Create")!.Invoke(null, null)!;
                try { Assert.True((int)contract.GetMethod("HrInit")!.Invoke(instance, null)! >= 0); }
                finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(instance); }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }

    [Fact]
    public void Windows_11_known_folder_app_identity_matches_Bambu_executable()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Bambu Studio", "bambu-studio.exe");
        Assert.True(TaskbarButtonProbe.MatchesAppId(@"Appid: {6D809377-6AF0-444B-8957-A3773F02200E}\Bambu Studio\bambu-studio.exe", path, null));
        Assert.False(TaskbarButtonProbe.MatchesAppId(@"Appid: {6D809377-6AF0-444B-8957-A3773F02200E}\Other App\bambu-studio.exe", path, null));
    }

    [Fact]
    public void Packaged_and_electron_apps_match_their_app_identity()
    {
        Assert.True(TaskbarButtonProbe.MatchesAppId("Appid: Anysphere.Cursor", @"C:\apps\Cursor.exe", "Anysphere.Cursor"));
        Assert.False(TaskbarButtonProbe.MatchesAppId("Appid: Other.Cursor", @"C:\apps\Cursor.exe", "Anysphere.Cursor"));
    }

    [Fact]
    public void Full_path_identity_is_case_insensitive_but_not_a_filename_substring()
    {
        Assert.True(TaskbarButtonProbe.MatchesAppId(@"Appid: C:\Apps\Editor.exe", @"c:\apps\editor.exe", null));
        Assert.False(TaskbarButtonProbe.MatchesAppId(@"Appid: D:\Apps\Editor.exe", @"c:\apps\editor.exe", null));
    }
}

public class TaskbarIdentityLifecycleTests
{
    [Fact]
    public void Explicit_window_identity_takes_precedence_over_shared_executable_path()
    {
        Assert.False(TaskbarButtonProbe.MatchesAppId(@"Appid: C:\apps\host.exe", @"C:\apps\host.exe", "App.Window.One"));
        Assert.True(TaskbarButtonProbe.MatchesAppId("Appid: App.Window.One", @"C:\apps\host.exe", "App.Window.One"));
        Assert.False(TaskbarButtonProbe.MatchesAppId("Appid: App.Window.Two", @"C:\apps\host.exe", "App.Window.One"));
    }
    [Theory]
    [InlineData("Chrome pinned",true)]
    [InlineData("Chrome - 1 running window pinned",false)]
    [InlineData("Chrome - 2 running windows and 1 group pinned",false)]
    [InlineData("Unknown localized label",false)]
    public void Pinned_launcher_is_not_confused_with_a_running_window(string label,bool pinned)
        => Assert.Equal(pinned,TaskbarButtonProbe.IsPinnedLauncherOnly(label));
}
