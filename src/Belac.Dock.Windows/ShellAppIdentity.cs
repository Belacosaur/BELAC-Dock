using System.Runtime.InteropServices;

namespace Belac.Dock.Windows;

// Optional read-only Windows 8+ Shell resolver. Its interface is not a public SDK
// contract, so failure falls back to documented process/shortcut/path identities.
// ABI reference: System Informer phlib/include/appresolverp.h (IApplicationResolver2).
internal static class ShellAppIdentity
{
    private static readonly object Gate = new();
    private static readonly Dictionary<(long Hwnd,int Pid,DateTime Start),(DateTime At,string? Id)> Cache = [];
    internal static string? Resolve(IntPtr hwnd, int pid, DateTime start)
    {
        var key=(hwnd.ToInt64(),pid,start);
        lock(Gate)
        {
            if(Cache.TryGetValue(key,out var cached) && DateTime.UtcNow-cached.At<TimeSpan.FromSeconds(3))return cached.Id;
        }
        IResolver? resolver=null;
        IntPtr text=IntPtr.Zero;
        string? id=null;
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(6,2)) return null;
            resolver=(IResolver)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("660B90C8-73A9-4B58-8CAE-355B7F55341B"))!)!;
            if(resolver.GetAppIDForWindow(hwnd,out text,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero)>=0 && text!=IntPtr.Zero)
                id=Marshal.PtrToStringUni(text);
        }
        catch(Exception ex) when(ex is COMException or InvalidCastException or NotSupportedException) { }
        finally {if(text!=IntPtr.Zero)Marshal.FreeCoTaskMem(text);if(resolver is not null)Marshal.ReleaseComObject(resolver);}
        if(string.IsNullOrWhiteSpace(id)) id=null;
        lock(Gate)
        {
            foreach(var stale in Cache.Where(p=>DateTime.UtcNow-p.Value.At>TimeSpan.FromMinutes(1)).Select(p=>p.Key).ToArray())Cache.Remove(stale);
            Cache[key]=(DateTime.UtcNow,id);
        }
        return id;
    }
    [ComImport,Guid("DE25675A-72DE-44B4-9373-05170450C140"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IResolver
    {
        [PreserveSig]int GetAppIDForShortcut(IntPtr item,out IntPtr id);
        [PreserveSig]int GetAppIDForShortcutObject(IntPtr link,IntPtr item,out IntPtr id);
        [PreserveSig]int GetAppIDForWindow(IntPtr hwnd,out IntPtr id,IntPtr preventPinning,IntPtr explicitId,IntPtr embeddedShortcut);
    }
}
