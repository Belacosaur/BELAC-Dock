using System.Text.Json;
using Belac.Dock.Core.Abstractions;
using Belac.Dock.Core.Models;
using Belac.Dock.Core.Settings;

namespace Belac.Dock.Infrastructure;

public sealed class JsonRecoveryJournalStore : IRecoveryJournalStore
{
    private readonly string _path;

    public JsonRecoveryJournalStore()
        : this(DockPaths.JournalPath)
    {
    }

    public JsonRecoveryJournalStore(string path)
    {
        _path = path;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    public IReadOnlyList<WindowMutationSnapshot> Load()
    {
        if (!File.Exists(_path))
            return [];

        try
        {
            var document = JsonSerializer.Deserialize<JournalDocument>(File.ReadAllText(_path), DockJson.Options);
            return document?.Entries ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<WindowMutationSnapshot> entries)
    {
        var document = new JournalDocument { Entries = entries.ToList() };
        var json = JsonSerializer.Serialize(document, DockJson.Options);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, _path, overwrite: true);
    }

    private sealed class JournalDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public List<WindowMutationSnapshot> Entries { get; set; } = [];
    }
}
