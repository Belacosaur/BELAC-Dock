using System.Text;
using System.Windows;
using System.Windows.Interop;
using Belac.Dock.App.Views;
using Belac.Dock.Core.Catalog;
using Belac.Dock.Core.Models;
using Belac.Dock.Windows;
using Belac.Dock.Windows.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Belac.Dock.App;

public partial class App : Application
{
    private AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                _host?.Separation.RestoreAll(hwnd => _host.Inspector.TryInspect(hwnd, out var observation) ? observation : null);
                _host?.Appbar.Remove();
                _host?.Logger.LogError("Unhandled UI exception: {Error}", args.Exception.GetType().Name);
            }
            catch (Exception)
            {
            }

            MessageBox.Show("BELAC Dock hit an unexpected error and restored what it could. The Windows taskbar was not restarted.", "BELAC Dock", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        var selfTest = Array.IndexOf(e.Args, "--self-test");
        if (selfTest >= 0)
        {
            var output = Path.Combine(AppContext.BaseDirectory, "self-test.txt");
            var outFlag = Array.IndexOf(e.Args, "--out");
            if (outFlag >= 0 && outFlag + 1 < e.Args.Length)
                output = e.Args[outFlag + 1];
            new SelfTestWindow(output).Show();
            return;
        }

        var smoke = Array.IndexOf(e.Args, "--smoke");
        if (smoke >= 0)
        {
            var output = Path.Combine(AppContext.BaseDirectory, "smoke.txt");
            if (smoke + 1 < e.Args.Length && !e.Args[smoke + 1].StartsWith('-'))
                output = e.Args[smoke + 1];
            StartSmoke(output);
            return;
        }

        Palette.Apply(false);
        SystemParameters.StaticPropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(SystemParameters.HighContrast))
                Palette.Apply(false);
        };
        SystemEvents.UserPreferenceChanged += (_, _) => Palette.Apply(false);

        var instance = new SingleInstanceManager();
        if (!instance.IsPrimary)
        {
            instance.Signal();
            instance.Dispose();
            Shutdown();
            return;
        }

        var host = new AppHost(instance: instance);
        _host = host;
        if (!host.Session.Current.OnboardingCompleted && host.Session.Current.Apps.Count == 0)
        {
            var onboarding = new OnboardingWindow(host);
            onboarding.ShowDialog();
            if (!host.Session.Current.OnboardingCompleted)
            {
                host.Session.Current.OnboardingCompleted = true;
                host.Session.Save();
            }
        }

        var dock = new DockWindow(host);
        host.ShowDock = dock.ShowDock;
        host.HideDock = dock.HideDock;
        host.OpenSettings = () =>
        {
            var settings = Current.Windows.OfType<SettingsWindow>().FirstOrDefault() ?? new SettingsWindow(host);
            if (!settings.IsVisible)
                settings.Show();
            settings.Activate();
        };
        if (host.Session.Current.StartMinimizedToTray)
            dock.HideDock();
        else
            dock.ShowDock();

        if (!string.IsNullOrWhiteSpace(host.Session.Warning))
            MessageBox.Show(host.Session.Warning, "BELAC Dock", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void StartSmoke(string output)
    {
        _ = RunSmokeAsync(output);
    }

    private async Task RunSmokeAsync(string output)
    {
        var report = new StringBuilder();
        var code = 1;
        AppHost? host = null;
        DockWindow? dock = null;
        Process? notepad = null;
        try
        {
            host = new AppHost(syncStartup: false);
            _host = host;
            host.Session.Current.OnboardingCompleted = true;
            host.Session.Save();
            var notepadPath = Path.Combine(Environment.SystemDirectory, "notepad.exe");
            if (File.Exists(notepadPath))
                host.Dock.AddFromPaths([notepadPath]);

            dock = new DockWindow(host);
            host.ShowDock = dock.ShowDock;
            host.HideDock = dock.HideDock;
            dock.ShowDock();
            await Task.Delay(900);

            var hwnd = new WindowInteropHelper(dock).Handle;
            var hasRect = NativeMethods.GetWindowRect(hwnd, out var rect);
            report.AppendLine(host.Appbar.IsRegistered ? "appbar: registered" : "appbar: not registered");
            report.AppendLine("bounds: " + (host.Appbar.DockBounds?.ToString() ?? "none"));
            report.AppendLine(hasRect ? $"window: {rect.Left},{rect.Top},{rect.Right},{rect.Bottom}" : "window: unavailable");

            await host.UsagePoll.RefreshAsync(CancellationToken.None);
            var snapshot = host.UsagePoll.Current;
            report.AppendLine(snapshot.SourceError is null ? "usage: ok" : "usage: " + snapshot.SourceError);
            report.AppendLine("providers: " + snapshot.Providers.Count);
            foreach (var provider in snapshot.Providers)
            {
                var percents = provider.Metrics.Count(metric => metric.UsedPercent is not null);
                report.AppendLine($"provider: {provider.Id} metrics={provider.Metrics.Count} percentMetrics={percents}");
            }

            if (File.Exists(notepadPath))
            {
                notepad = Process.Start(LaunchRequest.Create(notepadPath, null, null));
                var seen = false;
                for (var attempt = 0; attempt < 20 && !seen; attempt++)
                {
                    await Task.Delay(200);
                    seen = host.Tracker.Windows.Any(window => window.ExecutablePath.EndsWith("notepad.exe", StringComparison.OrdinalIgnoreCase));
                }

                var running = host.Dock.Rows.OfType<Belac.Dock.App.ViewModels.AppItemViewModel>().Any(item => item.IsRunning && item.DisplayName.Contains("notepad", StringComparison.OrdinalIgnoreCase));
                report.AppendLine(seen ? "tracking: notepad window observed" : "tracking: notepad window not observed");
                report.AppendLine(running ? "match: notepad is running on the dock" : "match: notepad was not matched");
                var notepadWindow = host.Tracker.Windows.FirstOrDefault(window => window.ExecutablePath.EndsWith("notepad.exe", StringComparison.OrdinalIgnoreCase));
                if (notepadWindow is not null)
                {
                    var app = new DockApp
                    {
                        DisplayName = "Notepad",
                        ExePath = notepadWindow.ExecutablePath,
                        TaskbarMode = TaskbarMode.DockOnly
                    };
                    var result = host.Separation.Apply(app, notepadWindow);
                    report.AppendLine("separation: " + result.Status);
                    report.AppendLine("separation-detail: " + result.Detail);
                    var restored = host.Separation.RestoreAll(value => host.Inspector.TryInspect(value, out var live) ? live : null);
                    report.AppendLine("separation-restored: " + restored);
                }
            }

            await Task.Delay(400);
            CaptureWindow(hwnd, Path.ChangeExtension(output, ".png"));

            code = !host.Appbar.IsRegistered && hasRect && rect.Right > rect.Left && rect.Bottom > rect.Top ? 0 : 1;
        }
        catch (Exception ex)
        {
            report.AppendLine("exception: " + ex.GetType().Name + " " + ex.Message);
            code = 1;
        }
        finally
        {
            try
            {
                if (notepad is not null && !notepad.HasExited)
                {
                    notepad.CloseMainWindow();
                    if (!notepad.WaitForExit(1000))
                        notepad.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
            }

            try
            {
                var directory = Path.GetDirectoryName(output);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(output, report.ToString());
            }
            catch (Exception)
            {
            }

            try
            {
                if (host is not null)
                {
                    foreach (var window in host.Tracker.Windows)
                    {
                        if (window.ExecutablePath.Contains("notepad", StringComparison.OrdinalIgnoreCase))
                            EndProcess(window.ProcessId);
                    }
                }
            }
            catch (Exception)
            {
            }

            try { dock?.PrepareExit(); } catch (Exception) { }
            var exitCode = code;
            var guard = new Thread(() =>
            {
                Thread.Sleep(3000);
                ExitProcess((uint)exitCode);
            })
            { IsBackground = true };
            guard.Start();
            try { host?.ShutdownApp(); } catch (Exception) { }
            ExitProcess((uint)exitCode);
        }
    }

    private static void EndProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
                return;
            if (!process.CloseMainWindow() || !process.WaitForExit(800))
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static void CaptureWindow(IntPtr hwnd, string path)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            return;
        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);
        using var bitmap = new System.Drawing.Bitmap(width, height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            var hdc = graphics.GetHdc();
            NativeMethods.PrintWindow(hwnd, hdc, 2);
            graphics.ReleaseHdc(hdc);
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        var topHeight = Math.Min(height, 640);
        using var top = bitmap.Clone(new System.Drawing.Rectangle(0, 0, width, topHeight), bitmap.PixelFormat);
        top.Save(Path.Combine(directory ?? ".", "dock-top.png"), System.Drawing.Imaging.ImageFormat.Png);
        var bottomHeight = Math.Min(height, 520);
        using var bottom = bitmap.Clone(new System.Drawing.Rectangle(0, height - bottomHeight, width, bottomHeight), bitmap.PixelFormat);
        bottom.Save(Path.Combine(directory ?? ".", "dock-bottom.png"), System.Drawing.Imaging.ImageFormat.Png);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern void ExitProcess(uint exitCode);
}
