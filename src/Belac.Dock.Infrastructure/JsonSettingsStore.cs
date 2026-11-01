using System.Text.Json;
using Belac.Dock.Core.Abstractions;
using Belac.Dock.Core.Models;
using Belac.Dock.Core.Settings;

namespace Belac.Dock.Infrastructure;

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _path;
    private readonly string _backupPath;

    public JsonSettingsStore()
        : this(DockPaths.Root)
    {
    }

    public JsonSettingsStore(string root)
    {
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "config.json");
        _backupPath = _path + ".bak";
    }

    public SettingsLoadResult Load()
    {
        if (!File.Exists(_path))
        {
            var created = DockSettings.CreateDefault();
            Save(created);
            return new SettingsLoadResult(created, false, true, null);
        }

        try
        {
            var settings = Read(_path);
            var warning = settings.SchemaVersion > SettingsMigrations.Current
                ? "These settings were written by a newer BELAC Dock. Unknown fields are kept until you save."
                : null;
            return new SettingsLoadResult(settings, false, false, warning);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
        {
            if (File.Exists(_backupPath))
            {
                try
                {
                    Quarantine(_path);
                    var restored = Read(_backupPath);
                    File.Copy(_backupPath, _path, true);
                    return new SettingsLoadResult(restored, true, false, "config.json was unreadable. Loaded the last good backup.");
                }
                catch (Exception backupEx) when (backupEx is JsonException or InvalidDataException or IOException)
                {
                }
            }

            var quarantine = Quarantine(_path);
            var defaults = DockSettings.CreateDefault();
            Save(defaults);
            var name = Path.GetFileName(quarantine);
            return new SettingsLoadResult(
                defaults,
                false,
                true,
                $"Settings could not be read. The original file is preserved as {name}. The dock is using safe defaults.");
        }
    }

    public void Save(DockSettings settings)
    {
        var normalized = SettingsNormalizer.Normalize(settings);
        if (normalized.SchemaVersion <= SettingsMigrations.Current)
            normalized.SchemaVersion = SettingsMigrations.Current;

        var json = JsonSerializer.Serialize(normalized, DockJson.Options);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, json);
        if (File.Exists(_path))
            File.Replace(temp, _path, _backupPath, ignoreMetadataErrors: true);
        else
            File.Move(temp, _path);
    }

    private DockSettings Read(string path)
    {
        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
            throw new JsonException("Settings file is empty.");

        var settings = JsonSerializer.Deserialize<DockSettings>(json, DockJson.Options)
                       ?? throw new JsonException("Settings file was empty.");
        return SettingsNormalizer.Normalize(settings);
    }

    private static string Quarantine(string path)
    {
        var destination = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        if (File.Exists(path))
            File.Copy(path, destination, true);
        return destination;
    }
}
