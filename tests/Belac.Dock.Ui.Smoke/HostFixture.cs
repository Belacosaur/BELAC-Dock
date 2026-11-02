using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Belac.Dock.App;
using Belac.Dock.App.ViewModels;
using Belac.Dock.Windows;

internal static class HostFixture
{
    internal static int Run(string home)
    {
        Environment.SetEnvironmentVariable("BELAC_DOCK_HOME", home);
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        // AppHost owns its isolated instance.
        var host=new AppHost(syncStartup:false,instance: new SingleInstanceManager("BELAC.Host."+Path.GetFileName(home)));
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(200)};
        timer.Tick+=(_,_)=>
        {
            var command=Path.Combine(home,"exit");
            if(File.Exists(command)){timer.Stop();host.ShutdownApp();return;}
            var activate = Path.Combine(home, "activate");
            if (File.Exists(activate))
            {
                if (long.TryParse(File.ReadAllText(activate), out var handle)) host.Controller.TryActivate(handle);
                File.Delete(activate);
            }
            var status=new {Pid=Environment.ProcessId,Rows=host.Dock.Rows.OfType<AppItemViewModel>().Select(r=>new{r.AppId,r.WindowCount,r.IsSeparated,r.TaskbarText}).ToArray()};
            var file=Path.Combine(home,"status.json");
            File.WriteAllText(file+".tmp",JsonSerializer.Serialize(status));File.Move(file+".tmp",file,true);
        };
        app.Exit+=(_,_)=>{timer.Stop();host.Dispose();};
        timer.Start();app.Run();return 0;
    }
}
