using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Belac.Dock.Core.Models;
using Belac.Dock.Core.Settings;
using Belac.Dock.Infrastructure;
using Belac.Dock.Windows;

internal static class HostLifecycle
{
    internal static int Run(string hostExe)
    {
        var home=Path.GetFullPath(Path.Combine("artifacts","host-lifecycle-"+Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(home);
        var label="HostLifecycle"+Guid.NewGuid().ToString("N");
        var processes=new List<Process>();Process? host=null;
        var inspector=new WindowInspector();
        var appA=new DockApp{DisplayName=label+"A",ExePath=Environment.ProcessPath!,TaskbarMode=TaskbarMode.DockOnly,
            MatchRules=[new MatchRule{Kind=MatchKind.ExecutablePath,Pattern=Environment.ProcessPath!,TitleRegex="^"+label+"A"}]};
        var appB=new DockApp{DisplayName=label+"B",ExePath=Environment.ProcessPath!,TaskbarMode=TaskbarMode.DockOnly,
            MatchRules=[new MatchRule{Kind=MatchKind.ExecutablePath,Pattern=Environment.ProcessPath!,TitleRegex="^"+label+"B"}]};
        var settings=DockSettings.CreateDefault();settings.OnboardingCompleted=true;settings.Autostart=false;settings.Usage.Enabled=false;
        appA.GroupId=appB.GroupId=settings.Groups[0].Id;settings.Apps.Add(appA);settings.Apps.Add(appB);
        File.WriteAllText(Path.Combine(home,"config.json"),JsonSerializer.Serialize(settings,DockJson.Options));
        (Process Process,WindowObservation Window) Fixture(DockApp target,int count)
        {
            var file=Path.Combine(home,Guid.NewGuid()+".json");
            var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false};
            foreach(var arg in new[]{"--fixture",target.DisplayName,target.DisplayName+".AppID",count.ToString(),file})start.ArgumentList.Add(arg);
            var process=Process.Start(start)!;processes.Add(process);
            Wait(()=>File.Exists(file),"Disposable app started");
            var h=JsonSerializer.Deserialize<List<long>>(File.ReadAllText(file))![0];
            if(!inspector.TryInspect(h,out var window))throw new Exception("Missing fixture window");
            return(process,window);
        }
        void StartHost()
        {
            File.Delete(Path.Combine(home,"exit"));
            var start=new ProcessStartInfo(Path.GetFullPath(hostExe)){UseShellExecute=false};
            start.ArgumentList.Add("--host-fixture");start.ArgumentList.Add(home);
            host=Process.Start(start)!;processes.Add(host);
            Wait(()=>Ready(host.Id),"Full dock host discovers and hides both app groups");
        }
        bool Ready(int pid)
        {
            try
            {
                using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(home,"status.json")));
                var root=doc.RootElement;
                var rows=root.GetProperty("Rows").EnumerateArray().ToArray();
                return root.GetProperty("Pid").GetInt32()==pid && rows.Length==2 && rows.All(r=>r.GetProperty("IsSeparated").GetBoolean())
                    && rows.Sum(r=>r.GetProperty("WindowCount").GetInt32())==3;
            }
            catch(Exception ex)when(ex is IOException or JsonException){return false;}
        }
        void ExitHost()
        {
            File.WriteAllText(Path.Combine(home,"exit"),"");
            Wait(()=>host!.HasExited,"Dock host exits cleanly");
        }
        try
        {
            var first=Fixture(appA,2);var second=Fixture(appB,1);
            StartHost();
            host!.Kill();host.WaitForExit();
            StartHost();
            Console.WriteLine("PASS Full host restart recovers persisted Dock-only windows after abrupt termination");
            ExitHost();
            TaskbarButtonProbe.Invalidate();
            Wait(()=>TaskbarButtonProbe.LookForWindow(first.Window)==TaskbarProbeResult.Present && TaskbarButtonProbe.LookForWindow(second.Window)==TaskbarProbeResult.Present,"Graceful host exit restores both taskbar groups");
            StartHost();Console.WriteLine("PASS Full normal host restart reapplies persisted app preferences");
            var oldPid=first.Process.Id;first.Process.Kill();first.Process.WaitForExit();
            first=Fixture(appA,2);
            Wait(()=>Ready(host!.Id) && new JsonRecoveryJournalStore(Path.Combine(home,"recovery-journal.json")).Load().Any(e=>e.ProcessId==first.Process.Id)
                && !new JsonRecoveryJournalStore(Path.Combine(home,"recovery-journal.json")).Load().Any(e=>e.ProcessId==oldPid),"Tracker discovers restarted app and replaces old recovery identities");
            ExitHost();return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally
        {
            if(host is {HasExited:false}) {File.WriteAllText(Path.Combine(home,"exit"),"");host.WaitForExit(5000);}
            foreach(var process in processes){if(!process.HasExited){process.Kill();process.WaitForExit();}process.Dispose();}
        }
    }
    private static void Wait(Func<bool> condition,string label)
    {
        var until=DateTime.UtcNow.AddSeconds(35);
        while(DateTime.UtcNow<until){if(condition()){Console.WriteLine("PASS "+label);return;}Thread.Sleep(250);}
        throw new TimeoutException(label);
    }
}
