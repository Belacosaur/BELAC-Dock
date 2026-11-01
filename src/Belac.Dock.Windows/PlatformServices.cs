using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Belac.Dock.Core.Abstractions;
using Belac.Dock.Core.Models;
using Belac.Dock.Windows.Native;
using Microsoft.Win32;

namespace Belac.Dock.Windows;

public sealed class IconExtractor : IIconSource
{
    public byte[]? GetPng(string path, int size)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var large = IntPtr.Zero;
        var small = IntPtr.Zero;
        try
        {
            var hr = NativeMethods.SHDefExtractIcon(path, 0, 0, out large, out small, (uint)Math.Clamp(size, 16, 256));
            if (hr < 0 || large == IntPtr.Zero)
                return Associated(path);

            using var icon = Icon.FromHandle(large);
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or ExternalException)
        {
            return null;
        }
        finally
        {
            if (large != IntPtr.Zero)
                NativeMethods.DestroyIcon(large);
            if (small != IntPtr.Zero)
                NativeMethods.DestroyIcon(small);
        }
    }

    private static byte[]? Associated(string path)
    {
        using var icon = Icon.ExtractAssociatedIcon(path);
        if (icon is null)
            return null;

        using var bitmap = icon.ToBitmap();
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}

public sealed class ShortcutResolver : IShortcutResolver
{
    public ShortcutTarget? Resolve(string shortcutPath)
    {
        if (string.IsNullOrWhiteSpace(shortcutPath) || !File.Exists(shortcutPath))
            return null;

        if (!shortcutPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return new ShortcutTarget(Path.GetFullPath(shortcutPath), null, null);

        IShellLinkW? link = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"));
            link = (IShellLinkW?)Activator.CreateInstance(type!);
            if (link is null)
                return null;

            ((IPersistFile)link).Load(shortcutPath, 0);
            link.Resolve(IntPtr.Zero, 0x1);
            var target = new System.Text.StringBuilder(1024);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
            var arguments = new System.Text.StringBuilder(1024);
            link.GetArguments(arguments, arguments.Capacity);
            var directory = new System.Text.StringBuilder(1024);
            link.GetWorkingDirectory(directory, directory.Capacity);
            var targetPath = target.ToString();
            return new ShortcutTarget(
                string.IsNullOrWhiteSpace(targetPath) ? null : targetPath,
                string.IsNullOrWhiteSpace(arguments.ToString()) ? null : arguments.ToString(),
                string.IsNullOrWhiteSpace(directory.ToString()) ? null : directory.ToString());
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or FileNotFoundException or IOException)
        {
            return null;
        }
        finally
        {
            if (link is not null && Marshal.IsComObject(link))
                Marshal.ReleaseComObject(link);
        }
    }
}

public sealed class StartupRegistration : IStartupRegistration
{
    private const string ValueName = "BELAC Dock";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsRegistered
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string;
        }
    }

    public void SetEnabled(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        key.SetValue(ValueName, "\"" + executablePath + "\"");
    }
}

public sealed class HotkeyRegistrar : IHotkeyRegistrar
{
    public bool TryRegister(IntPtr hwnd, int id, HotkeyChord chord)
    {
        Unregister(hwnd, id);
        uint modifiers = 0x4000;
        if (chord.Alt) modifiers |= 0x0001;
        if (chord.Ctrl) modifiers |= 0x0002;
        if (chord.Shift) modifiers |= 0x0004;
        if (chord.Win) modifiers |= 0x0008;
        return NativeMethods.RegisterHotKey(hwnd, id, modifiers, (uint)chord.VirtualKey);
    }

    public void Unregister(IntPtr hwnd, int id) => NativeMethods.UnregisterHotKey(hwnd, id);
}

public sealed class SingleInstanceManager : ISingleInstance
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _event;
    private readonly Thread? _thread;
    private readonly bool _ownsMutex;
    private EventHandler? _activated;
    private int _pending;
    private volatile bool _disposed;

    public SingleInstanceManager()
        : this("BELAC.Dock")
    {
    }

    public SingleInstanceManager(string name)
    {
        var baseName = name.StartsWith("Local\\", StringComparison.Ordinal) ? name : "Local\\" + name;
        _mutex = new Mutex(false, baseName);
        try
        {
            _ownsMutex = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
        }

        IsPrimary = _ownsMutex;
        _event = new EventWaitHandle(false, EventResetMode.AutoReset, baseName + ".Activate");
        if (!IsPrimary)
            return;

        _thread = new Thread(Listen)
        {
            IsBackground = true,
            Name = "BELAC.Dock.Instance"
        };
        _thread.Start();
    }

    public bool IsPrimary { get; }

    public event EventHandler? Activated
    {
        add
        {
            _activated += value;
            if (Interlocked.Exchange(ref _pending, 0) == 1)
                value?.Invoke(this, EventArgs.Empty);
        }
        remove => _activated -= value;
    }

    public void Signal() => _event.Set();

    public void Dispose()
    {
        _disposed = true;
        _event.Set();
        _thread?.Join(500);
        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _mutex.Dispose();
        _event.Dispose();
    }

    private void Listen()
    {
        while (!_disposed && _event.WaitOne())
        {
            if (_disposed)
                break;

            var handler = _activated;
            if (handler is null)
                Interlocked.Exchange(ref _pending, 1);
            else
                handler(this, EventArgs.Empty);
        }
    }
}

public sealed class OpenTokenUsageLocator : IUsageAppLocator
{
    public UsageAppLocation Locate()
    {
        long handle = 0;
        string? path = null;
        var running = false;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!process.ProcessName.Contains("OpenTokenUsage", StringComparison.OrdinalIgnoreCase))
                    continue;

                running = true;
                if (process.MainWindowHandle != IntPtr.Zero)
                    handle = process.MainWindowHandle.ToInt64();

                try
                {
                    path ??= process.MainModule?.FileName;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        path ??= FindInstalledExecutable();
        return new UsageAppLocation(running, path, handle);
    }

    private static string? FindInstalledExecutable()
    {
        using var appPaths = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\OpenTokenUsage.exe");
        if (appPaths?.GetValue(null) is string registeredPath && File.Exists(Clean(registeredPath)!))
            return Clean(registeredPath);

        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var root in new[]
            {
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
                @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            })
            {
                using var key = hive.OpenSubKey(root);
                if (key is null)
                    continue;

                foreach (var subName in key.GetSubKeyNames())
                {
                    using var sub = key.OpenSubKey(subName);
                    var displayName = sub?.GetValue("DisplayName") as string;
                    if (sub is null || displayName is null || !displayName.Contains("OpenTokenUsage", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var icon = Clean(sub.GetValue("DisplayIcon") as string);
                    if (icon is not null && File.Exists(icon))
                        return icon;

                    var location = sub.GetValue("InstallLocation") as string;
                    if (string.IsNullOrWhiteSpace(location))
                        continue;

                    var candidate = Path.Combine(location, "OpenTokenUsage.exe");
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
        }

        return null;
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim().Trim('"');
        var comma = text.LastIndexOf(',');
        if (comma > 2 && !File.Exists(text))
            text = text[..comma].Trim().Trim('"');

        return text;
    }
}
