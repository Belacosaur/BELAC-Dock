using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Belac.Dock.Core.Models;
using Belac.Dock.Infrastructure;
using Belac.Dock.Windows;
using Belac.Dock.Windows.Native;

internal static class Lifecycle
{
    internal static int Fixture(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnLastWindowClose };
        var handles = new List<long>();
        for (int i=0;i<int.Parse(args[3]);i++)
        {
            var window = new Window { Title=args[1]+" "+i, Width=260, Height=120, Left=300+i*280, Top=100, ShowInTaskbar=true, ShowActivated=false };
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            if (args[2] != "default") SetIdentity(hwnd, args[2]);
            window.Show();
            handles.Add(hwnd.ToInt64());
        }
        System.Windows.Threading.DispatcherTimer? resist = null;
        object? shell = null;
        if(args.Length>5 && args[5]=="resist")
        {
            var assembly=typeof(TaskbarSeparationService).Assembly;
            var contract=assembly.GetType("Belac.Dock.Windows.Native.ITaskbarList")!;
            shell=assembly.GetType("Belac.Dock.Windows.Native.TaskbarListFactory")!.GetMethod("Create")!.Invoke(null,null)!;
            contract.GetMethod("HrInit")!.Invoke(shell,null);
            resist=new(){Interval=TimeSpan.FromMilliseconds(50)};
            resist.Tick+=(_,_)=>{foreach(var h in handles)if((NativeMethods.ReadExStyle(new IntPtr(h))&NativeMethods.WsExToolWindow)==0)contract.GetMethod("AddTab")!.Invoke(shell,[new IntPtr(h)]);};
            resist.Start();
        }
        app.Exit+=(_,_)=>{resist?.Stop();if(shell is not null)Marshal.ReleaseComObject(shell);};
        File.WriteAllText(args[4], JsonSerializer.Serialize(handles));
        app.Run();
        return 0;
    }

    internal static int Run()
    {
        var root=Path.GetFullPath(Path.Combine("artifacts", "lifecycle-"+Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var inspector=new WindowInspector();
        var store=new JsonRecoveryJournalStore(Path.Combine(root,"journal.json"));
        TaskbarSeparationService service=new(store, inspector);
        var fixtures=new List<Process>();
        var label="BELAC lifecycle "+Guid.NewGuid().ToString("N");
        var a=MakeApp(label+" A"); var b=MakeApp(label+" B");
        var apps=new List<DockApp>{a,b};
        (Process Process,List<WindowObservation> Windows) Start(string title, string id, int count, bool resist=false)
        {
            var file=Path.Combine(root,Guid.NewGuid()+".json");
            var start=new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false };
            foreach(var argument in new[]{"--fixture",title,id,count.ToString(),file,resist ? "resist" : "normal"}) start.ArgumentList.Add(argument);
            var process=Process.Start(start)!;fixtures.Add(process);
            Wait(() => File.Exists(file), "Fixture starts");
            var windows=JsonSerializer.Deserialize<List<long>>(File.ReadAllText(file))!.Select(h => inspector.TryInspect(h,out var w) ? w : throw new Exception("Missing fixture")).ToList();
            return (process,windows);
        }
        void HideAll(params (DockApp App,List<WindowObservation> Windows)[] groups)
        {
            Wait(() =>
            {
                var results=groups.SelectMany(g=>service.ApplyBatch(g.App,g.Windows)).ToArray();
                return results.Length>0 && results.All(r=>r.Status==TaskbarStatus.DockOnly);
            }, "All app windows reach verified Dock-only");
        }
        void ExerciseMinimize(WindowObservation window)
        {
            var h = new IntPtr(window.Hwnd);
            NativeMethods.GetWindowRect(h, out var before);
            var controller = new WindowController();
            for (var cycle = 0; cycle < 3; cycle++)
            {
                var expected = before;
                if (cycle == 2)
                {
                    NativeMethods.ShowWindowAsync(h, 3);
                    Wait(() => IsZoomed(h), "Fixture maximized before minimizing");
                    NativeMethods.GetWindowRect(h, out expected);
                }
                if (cycle == 1) NativeMethods.PostMessage(h, 0x0112, new IntPtr(0xF020), IntPtr.Zero); // app caption/system-menu minimize
                else controller.TryMinimize(window.Hwnd);
                Wait(() => NativeMethods.IsIconic(h), "Fixture enters native minimized state");
                service.ParkMinimizedWindow(window);
                Check(NativeMethods.IsIconic(h) && NativeMethods.IsWindowVisible(h), "Minimized app remains tracked and restorable");
                NativeMethods.GetWindowRect(h, out var minimized);
                Check(new MonitorCatalog().GetMonitors().All(m => minimized.Right <= m.Bounds.Left || minimized.Left >= m.Bounds.Right || minimized.Bottom <= m.Bounds.Top || minimized.Top >= m.Bounds.Bottom), "No minimized title bar appears on any monitor");
                Check(Probe(window) is TaskbarProbeResult.Absent or TaskbarProbeResult.PinnedOnly, "Minimized app has no running Windows taskbar button");
                controller.TryActivate(window.Hwnd);
                Wait(() => !NativeMethods.IsIconic(h), "Dock activation restores minimized app");
                NativeMethods.GetWindowRect(h, out var restored);
                Check(restored.Left == expected.Left && restored.Top == expected.Top && restored.Right == expected.Right && restored.Bottom == expected.Bottom, "Restore preserves original window bounds");
                if (cycle == 2) Check(IsZoomed(h), "Restore preserves maximized state");
                // Native restore may recreate the button; reconcile just like the dock host.
                var app = apps.Single(a => Belac.Dock.Core.Matching.AppMatcher.Match([a], window).App is not null);
                HideAll((app, new List<WindowObservation>{window}));
            }
        }
        try
        {
            var first=Start(a.DisplayName,"BELAC.Test."+Guid.NewGuid().ToString("N"),2);
            var second=Start(b.DisplayName,"default",1);
            Check(first.Windows.All(w=>w.Aumid?.StartsWith("BELAC.Test.")==true), "Reads per-window app identity");
            Wait(()=>Probe(first.Windows[0])==TaskbarProbeResult.Present && Probe(second.Windows[0])==TaskbarProbeResult.Present,"Both app groups visible before hiding");
            HideAll((a,first.Windows));
            Check(Probe(first.Windows[0])==TaskbarProbeResult.Absent && Probe(second.Windows[0])==TaskbarProbeResult.Present,"Explicit app group is independent from same-executable default group");
            HideAll((a,first.Windows),(b,second.Windows));
            Check(service.Snapshots.Count==3,"Tracks every window across both app groups");
            Check(Probe(first.Windows[0])==TaskbarProbeResult.Absent && Probe(second.Windows[0])==TaskbarProbeResult.Absent,"Both taskbar groups hidden");
            ExerciseMinimize(first.Windows[0]);
            service.Dispose();service=new TaskbarSeparationService(store,inspector);
            service.Reconcile(apps);
            HideAll((a,first.Windows),(b,second.Windows));
            Check(Probe(first.Windows[0])==TaskbarProbeResult.Absent,"Crash-style dock restart retains verified hiding");
            Check(service.RestoreAll(h=>inspector.TryInspect(h,out var w)?w:null)==3,"Graceful dock exit restores all managed windows");
            Wait(()=>Probe(first.Windows[0])==TaskbarProbeResult.Present && Probe(second.Windows[0])==TaskbarProbeResult.Present,"Restored groups visible");
            service.Dispose();service=new TaskbarSeparationService(store,inspector);
            HideAll((a,first.Windows),(b,second.Windows));
            Console.WriteLine("PASS Normal dock restart reapplies saved Dock-only choices");
            var oldPid=first.Process.Id;
            var id=first.Windows[0].Aumid!;
            first.Process.Kill();first.Process.WaitForExit();
            service.Reconcile(apps);
            Check(service.Snapshots.All(e=>e.ProcessId!=oldPid),"Closed app recovery entries are pruned");
            first=Start(a.DisplayName,id,2);
            HideAll((a,first.Windows),(b,second.Windows));
            Check(service.Snapshots.Count==3 && service.Snapshots.Any(e=>e.ProcessId==first.Process.Id),"Restarted app is managed using its new process identity");
            apps.Remove(a);service.Reconcile(apps);
            Wait(()=>Probe(first.Windows[0])==TaskbarProbeResult.Present,"Removing an app restores all of its windows");
            Check(service.Snapshots.Count==1,"Other app remains independently managed");
            service.SetPaused(true);service.Reconcile(apps);
            Wait(()=>Probe(second.Windows[0])==TaskbarProbeResult.Present,"Pausing restores remaining windows");
            Check(store.Load().Count==0,"No stale recovery entries remain");
            service.SetPaused(false);
            var c=MakeApp(label+" C");c.AllowStyleCompatibility=true;apps.Add(c);
            var resistant=Start(c.DisplayName,c.DisplayName+".AppID",1,true);
            var originalStyle=NativeMethods.ReadExStyle(new IntPtr(resistant.Windows[0].Hwnd));
            HideAll((c,resistant.Windows));
            Check(service.Snapshots.Single().Method=="ExStyle","Configured fallback handles an app that recreates its taskbar button");
            ExerciseMinimize(resistant.Windows[0]);
            service.Dispose();service=new TaskbarSeparationService(store,inspector);service.Reconcile(apps);
            HideAll((c,resistant.Windows));
            Check((NativeMethods.ReadExStyle(new IntPtr(resistant.Windows[0].Hwnd))&NativeMethods.WsExToolWindow)!=0,"Fallback remains active after dock restart");
            c.AllowStyleCompatibility=false;service.Reconcile(apps);
            Check(NativeMethods.ReadExStyle(new IntPtr(resistant.Windows[0].Hwnd))==originalStyle,"Disabling fallback restores original window styles");
            Wait(()=>Probe(resistant.Windows[0])==TaskbarProbeResult.Present,"Disabling fallback restores the running taskbar button");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally
        {
            service.RestoreAll(h=>inspector.TryInspect(h,out var w)?w:null);service.Dispose();
            foreach(var process in fixtures){if(!process.HasExited){process.Kill();process.WaitForExit();}process.Dispose();}
        }
    }
    private static DockApp MakeApp(string title)=>new(){DisplayName=title,ExePath=Environment.ProcessPath!,TaskbarMode=TaskbarMode.DockOnly,
        MatchRules=[new MatchRule{Kind=MatchKind.ExecutablePath,Pattern=Environment.ProcessPath!,TitleRegex="^"+System.Text.RegularExpressions.Regex.Escape(title)}]};
    private static TaskbarProbeResult Probe(WindowObservation window)=>TaskbarButtonProbe.LookForWindow(window);
    private static void Wait(Func<bool> condition,string label)
    {
        var until=DateTime.UtcNow.AddSeconds(25);
        while(DateTime.UtcNow<until){if(condition()){Console.WriteLine("PASS "+label);return;}Thread.Sleep(200);}
        throw new TimeoutException(label);
    }
    private static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);

    // Fixture-only setter: production code only reads the property.
    [StructLayout(LayoutKind.Sequential)] private struct Key { public Guid Format;public uint Id; }
    [StructLayout(LayoutKind.Explicit,Size=24)] private struct Value { [FieldOffset(0)]public ushort Type;[FieldOffset(8)]public IntPtr Pointer; }
    [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface Store
    {
        [PreserveSig]int GetCount(out uint count);[PreserveSig]int GetAt(uint i,out Key key);
        [PreserveSig]int GetValue(ref Key key,out Value value);[PreserveSig]int SetValue(ref Key key,ref Value value);[PreserveSig]int Commit();
    }
    [DllImport("shell32.dll")]private static extern int SHGetPropertyStoreForWindow(IntPtr h,ref Guid id,[MarshalAs(UnmanagedType.Interface)]out Store store);
    private static void SetIdentity(IntPtr hwnd,string id)
    {
        var iid=typeof(Store).GUID;Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd,ref iid,out var store));
        var key=new Key{Format=new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),Id=5};
        var value=new Value{Type=31,Pointer=Marshal.StringToCoTaskMemUni(id)};
        try{Marshal.ThrowExceptionForHR(store.SetValue(ref key,ref value));}
        finally{Marshal.FreeCoTaskMem(value.Pointer);Marshal.ReleaseComObject(store);}
    }
}
