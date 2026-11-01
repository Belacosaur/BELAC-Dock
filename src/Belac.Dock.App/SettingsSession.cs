using Belac.Dock.Core.Abstractions;
using Belac.Dock.Core.Models;

namespace Belac.Dock.App;

public sealed class SettingsSession
{
    private readonly ISettingsStore _store;

    public SettingsSession(ISettingsStore store)
    {
        var loaded = store.Load();
        Current = loaded.Settings;
        Warning = loaded.Warning;
        UsedBackup = loaded.UsedBackup;
        UsedDefaults = loaded.UsedDefaults;
        _store = store;
    }

    public DockSettings Current { get; }
    public string? Warning { get; private set; }
    public bool UsedBackup { get; }
    public bool UsedDefaults { get; }
    public event EventHandler? Changed;

    public void DismissWarning() => Warning = null;

    public void Save()
    {
        _store.Save(Current);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
