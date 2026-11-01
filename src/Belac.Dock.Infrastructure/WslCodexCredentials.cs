using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Belac.Dock.Infrastructure;

public sealed record WslCodexAccount(string Id, string DisplayName, NativeUsageCredential? Credential);
public interface IWslCodexCredentials
{
    Task<IReadOnlyList<WslCodexAccount>> ReadAsync(CancellationToken token);
}

public sealed class WslCodexCredentials : IWslCodexCredentials
{
    public static string IdFor(string distro) => "codex-wsl-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(distro))).ToLowerInvariant()[..16];
    public async Task<IReadOnlyList<WslCodexAccount>> ReadAsync(CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) return [];
        var running = await RunAsync(["--list", "--running", "--quiet"], Encoding.Unicode, token).ConfigureAwait(false);
        var accounts = new List<WslCodexAccount>();
        foreach (var distro in running.Replace("\0", "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct())
        {
            if (distro.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase)) continue;
            NativeUsageCredential? credential = null;
            try
            {
                // Arguments are separate; the distribution name never becomes shell code.
                // Do not run a login shell or write/refresh provider credentials.
                const string script = "if [ -n \"$CODEX_HOME\" ]; then p=\"$CODEX_HOME/auth.json\"; else p=\"$HOME/.codex/auth.json\"; if [ ! -f \"$p\" ]; then p=\"$HOME/.config/codex/auth.json\"; fi; fi; if [ -f \"$p\" ]; then head -c 1048577 -- \"$p\"; fi";
                var payload = await RunAsync(["--distribution", distro, "--exec", "sh", "-c", script], Encoding.UTF8, token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(payload)) continue;
                using var json = JsonDocument.Parse(payload);
                var entry = NativeUsageParser.Child(json.RootElement, "tokens");
                var access = NativeUsageParser.Text(entry, "access_token");
                if (!string.IsNullOrWhiteSpace(access)) credential = new() { AccessToken = access, AccountId = NativeUsageParser.Text(entry, "account_id") };
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { /* Never include credential-bearing command output in errors. */ }
            accounts.Add(new(IdFor(distro), $"Codex · WSL ({distro})", credential));
        }
        return accounts;
    }
    private static async Task<string> RunAsync(string[] arguments, Encoding encoding, CancellationToken token)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = encoding, StandardErrorEncoding = encoding };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var process = Process.Start(start) ?? throw new IOException("WSL unavailable.");
        async Task<string> ReadBoundedAsync(StreamReader reader)
        {
            var result = new StringBuilder();
            var buffer = new char[4096];
            int count;
            while ((count = await reader.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (result.Length + count > 1024 * 1024) { timeout.Cancel(); throw new IOException("WSL response too large."); }
                result.Append(buffer, 0, count);
            }
            return result.ToString();
        }
        try
        {
            var output = ReadBoundedAsync(process.StandardOutput);
            var error = ReadBoundedAsync(process.StandardError);
            await Task.WhenAll(output, error, process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException("WSL query failed.");
            return await output.ConfigureAwait(false);
        }
        finally { if (!process.HasExited) process.Kill(); }
    }
}
