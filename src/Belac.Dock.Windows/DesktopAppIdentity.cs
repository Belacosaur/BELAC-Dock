using System.Runtime.InteropServices;

namespace Belac.Dock.Windows;

// Standard shortcut AppUserModelID metadata identifies unpackaged apps that set only
// a process-level explicit ID. Never guess from display names or executable basenames.
public static class DesktopAppIdentity
{
    private static readonly object Gate = new();
    private static Dictionary<string, string[]> _ids = new(StringComparer.OrdinalIgnoreCase);
    private static Task<Dictionary<string, string[]>>? _scan;
    private static DateTime _expires;
    public static string? Find(string executablePath)
    {
        lock (Gate)
        {
            if (_scan is { IsCompleted: true })
            {
                _ids = _scan.GetAwaiter().GetResult(); _scan = null;
                _expires = DateTime.UtcNow.AddMinutes(2);
            }
            if (_scan is null && DateTime.UtcNow >= _expires) _scan = Task.Run(Scan);
            return _ids.TryGetValue(executablePath, out var ids) && ids.Length == 1 ? ids[0] : null;
        }
    }
    private static Dictionary<string, string[]> Scan()
    {
        var found = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar") };
        var resolver = new ShortcutResolver();
        foreach (var root in roots)
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                var options = new EnumerationOptions { RecurseSubdirectories=true, IgnoreInaccessible=true, AttributesToSkip=FileAttributes.ReparsePoint };
                foreach (var shortcut in Directory.EnumerateFiles(root, "*.lnk", options))
                {
                    try
                    {
                        var id = WindowAppIdentity.ReadShortcut(shortcut);
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        var target = resolver.Resolve(shortcut)?.TargetPath;
                        if (string.IsNullOrWhiteSpace(target)) continue;
                        target = Path.GetFullPath(target);
                        if (!found.TryGetValue(target, out var ids)) found[target] = ids = new(StringComparer.OrdinalIgnoreCase);
                        ids.Add(id);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or ArgumentException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return found.ToDictionary(pair=>pair.Key,pair=>pair.Value.ToArray(),StringComparer.OrdinalIgnoreCase);
    }
}
