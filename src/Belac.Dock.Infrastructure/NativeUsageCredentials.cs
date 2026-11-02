using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Belac.Dock.Infrastructure;

// Deliberately read-only: the provider app owns sign-in and refresh-token rotation.
public sealed class NativeUsageCredential
{
    public required string AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public string? AccountId { get; init; }
    public string? Plan { get; init; }
    public override string ToString() => "[Protected provider credential]";
}

public interface INativeUsageCredentials
{
    Task<NativeUsageCredential?> ReadAsync(string provider, CancellationToken token);
}

public sealed class NativeUsageCredentials : INativeUsageCredentials
{
    public async Task<NativeUsageCredential?> ReadAsync(string provider, CancellationToken token)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (provider == "cursor")
            return await Task.Run(() => ReadCursor(), token).ConfigureAwait(false);
        var directory = provider == "codex" ? Environment.GetEnvironmentVariable("CODEX_HOME") : Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var paths = !string.IsNullOrWhiteSpace(directory)
            ? new[] { Path.Combine(directory, provider == "codex" ? "auth.json" : ".credentials.json") }
            : provider == "codex" ? new[] { Path.Combine(home, ".codex", "auth.json"), Path.Combine(home, ".config", "codex", "auth.json") }
            : new[] { Path.Combine(home, ".claude", ".credentials.json") };
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            if (!File.Exists(path)) continue;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 1024 * 1024) continue;
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
            var root = json.RootElement;
            var entry = NativeUsageParser.Child(root, provider == "codex" ? "tokens" : "claudeAiOauth");
            var access = NativeUsageParser.Text(entry, provider == "codex" ? "access_token" : "accessToken");
            if (!string.IsNullOrWhiteSpace(access)) return new() { AccessToken = access, AccountId = NativeUsageParser.Text(entry, "account_id"), Plan = NativeUsageParser.Text(entry, "subscriptionType") };
        }
        return null;
    }

    private static NativeUsageCredential? ReadCursor()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor", "User", "globalStorage", "state.vscdb");
        if (!File.Exists(path)) return null;
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 2 }.ToString());
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT key, value FROM ItemTable WHERE key IN ('cursorAuth/accessToken','cursorAuth/refreshToken','cursorAuth/stripeMembershipType')";
        using var rows = command.ExecuteReader();
        string? access = null, refresh = null, plan = null;
        while (rows.Read())
        {
            if (rows.GetString(0) == "cursorAuth/accessToken") access = rows.GetString(1);
            else if (rows.GetString(0) == "cursorAuth/refreshToken") refresh = rows.GetString(1);
            else plan = rows.GetString(1);
        }
        access = CursorTokenCodec.Decode(access);
        refresh = CursorTokenCodec.Decode(refresh);
        return string.IsNullOrWhiteSpace(access) ? null : new() { AccessToken = access, RefreshToken = refresh, Plan = plan };
    }
}
