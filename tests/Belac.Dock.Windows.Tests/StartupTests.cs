using System.IO;
using Belac.Dock.Windows;
using Microsoft.Win32;

namespace Belac.Dock.Windows.Tests;

public class StartupTests
{
    [Fact]
    public void Startup_routes_follow_updated_executable_and_are_both_removed_when_disabled()
    {
        var name = "BELAC.Startup.Test." + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), name);
        var startup = new StartupRegistration(name, directory);
        var first = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var second = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        try
        {
            startup.SetEnabled(true, first);
            Assert.True(startup.IsRegistered);
            Assert.Equal(first, new ShortcutResolver().Resolve(Path.Combine(directory, name + ".lnk"))!.TargetPath, ignoreCase: true);
            startup.SetEnabled(true, second);
            Assert.Equal(second, new ShortcutResolver().Resolve(Path.Combine(directory, name + ".lnk"))!.TargetPath, ignoreCase: true);
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                Assert.Equal("\"" + second + "\"", key!.GetValue(name));
            startup.SetEnabled(false, second);
            Assert.False(startup.IsRegistered);
            Assert.False(File.Exists(Path.Combine(directory, name + ".lnk")));
        }
        finally { startup.SetEnabled(false, second); if (Directory.Exists(directory)) Directory.Delete(directory); }
    }
}
