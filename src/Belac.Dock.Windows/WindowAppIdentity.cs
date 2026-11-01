using System.Runtime.InteropServices;

namespace Belac.Dock.Windows;

// Shell property store: a per-window AppUserModelID takes precedence over the process ID.
public static class WindowAppIdentity
{
    public static string? Read(IntPtr hwnd)
    {
        IPropertyStore? store = null;
        var value = new PropVariant();
        try
        {
            var iid = typeof(IPropertyStore).GUID;
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out store) < 0 || store is null) return null;
            return ReadValue(store, ref value);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return null; }
        finally
        {
            PropVariantClear(ref value);
            if (store is not null) Marshal.ReleaseComObject(store);
        }
    }
    internal static string? ReadShortcut(string path)
    {
        IPropertyStore? store = null;
        var value = new PropVariant();
        try
        {
            var shellLink = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!)!;
            store = (IPropertyStore)shellLink;
            ((Native.IPersistFile)shellLink).Load(path, 0);
            return ReadValue(store, ref value);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return null; }
        finally { PropVariantClear(ref value); if (store is not null) Marshal.ReleaseComObject(store); }
    }
    private static string? ReadValue(IPropertyStore store, ref PropVariant value)
    {
        var key = new PropertyKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 };
        if (store.GetValue(ref key, out value) < 0 || value.Type != 31 || value.Pointer == IntPtr.Zero) return null;
        var id = Marshal.PtrToStringUni(value.Pointer);
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size=24)] private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? store);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
}
