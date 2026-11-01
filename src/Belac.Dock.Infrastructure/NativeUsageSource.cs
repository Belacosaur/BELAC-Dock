using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Belac.Dock.Core.Abstractions;
using Belac.Dock.Core.Models;

namespace Belac.Dock.Infrastructure;

public sealed class NativeUsageSource : IUsageProviderSource, IDisposable
{
    private readonly HttpClient _http;
    private readonly INativeUsageCredentials _credentials;
    private readonly Func<UsageSettings> _settings;
    private readonly IUsageProviderSource _fallback;
    private readonly bool _ownsFallback;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly Dictionary<string, (string Fingerprint, ProviderUsage Reading, DateTimeOffset Next)> _cache = new();
    public NativeUsageSource(Func<UsageSettings> settings, INativeUsageCredentials? credentials = null, HttpMessageHandler? handler = null,
        IUsageProviderSource? fallback = null, Func<DateTimeOffset>? clock = null)
    {
        _settings = settings;
        _credentials = credentials ?? new NativeUsageCredentials();
        _http = handler is null ? new(new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(5) }) : new(handler, false);
        _http.Timeout = TimeSpan.FromSeconds(12);
        _fallback = fallback ?? new OpenTokenUsageSource();
        _ownsFallback = fallback is null;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }
    public Task<UsageSnapshot> GetSnapshotAsync(CancellationToken token) => GetSnapshotAsync(token, null);
    public async Task<UsageSnapshot> GetSnapshotAsync(CancellationToken token, UsageSettings? selection)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var settings = selection ?? _settings();
            var selected = (settings.NativeProviders ?? []).Where(id => id is "codex" or "claude" or "cursor").Distinct().ToArray();
            // Serialize provider requests to keep credentials and cache isolated; never block the UI thread.
            var providers = new List<ProviderUsage>();
            foreach (var id in selected) providers.Add(await FetchAsync(id, token).ConfigureAwait(false));
            if (settings.CompanionFallback)
            {
                var other = await _fallback.GetSnapshotAsync(token).ConfigureAwait(false);
                if (other.SourceError is null)
                    foreach (var row in other.Providers.Where(p => !p.IsStale && p.Error is null && p.Metrics.Count > 0))
                    {
                        var index = providers.FindIndex(p => p.Id == row.Id);
                        if (index < 0) providers.Add(row);
                        else if (providers[index].Error is not null) providers[index] = row;
                    }
            }
            return new(_clock(), providers, null);
        }
        finally { _gate.Release(); }
    }
    private async Task<ProviderUsage> FetchAsync(string id, CancellationToken token)
    {
        var now = _clock();
        ProviderUsage Failure(string error) => new(id, NativeUsageParser.Name(id), null, [], null, false, error);
        NativeUsageCredential? credential;
        try { credential = await _credentials.ReadAsync(id, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { _cache.Remove(id); return Failure("Couldn't read this app's sign-in. Open the app, sign in, then check again."); }
        if (credential is null)
        {
            _cache.Remove(id);
            return Failure(id == "claude" ? "Sign in to Claude Code on Windows, then check again." : $"Sign in to {NativeUsageParser.Name(id)} on Windows, then check again. A website login alone is not enough.");
        }
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential.AccessToken + "\n" + credential.AccountId)));
        var hasPrevious = _cache.TryGetValue(id, out var previous) && previous.Fingerprint == fingerprint;
        if (hasPrevious && now < previous.Next) return previous.Reading;
        var next = now.AddMinutes(id == "claude" ? 5 : 2);
        ProviderUsage result;
        try
        {
            var url = id switch
            {
                "codex" => "https://chatgpt.com/backend-api/wham/usage",
                "claude" => "https://api.anthropic.com/api/oauth/usage",
                _ => "https://api2.cursor.sh/aiserver.v1.DashboardService/GetCurrentPeriodUsage"
            };
            using var request = new HttpRequestMessage(id == "cursor" ? HttpMethod.Post : HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (id == "codex" && !string.IsNullOrWhiteSpace(credential.AccountId)) request.Headers.Add("ChatGPT-Account-Id", credential.AccountId);
            if (id == "claude") request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
            if (id == "cursor") { request.Content = new StringContent("{}", Encoding.UTF8, "application/json"); request.Headers.Add("Connect-Protocol-Version", "1"); }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                result = Failure($"Sign-in expired or usage access was denied. Open {NativeUsageParser.Name(id)}{(id == "claude" ? " Code" : "")}, sign in again, then check here.");
            else if ((int)response.StatusCode == 429)
            {
                var retry = response.Headers.RetryAfter;
                next = retry?.Date ?? now.Add(retry?.Delta ?? TimeSpan.FromMinutes(30));
                if (next < now.AddMinutes(5)) next = now.AddMinutes(5);
                result = Failure($"Provider rate limit reached. Retrying after {next.ToLocalTime():t}.");
            }
            else if (!response.IsSuccessStatusCode)
                result = Failure($"Usage service returned HTTP {(int)response.StatusCode}. It will retry automatically.");
            else
            {
                await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                using var data = new MemoryStream();
                var buffer = new byte[8192];
                int count;
                while ((count = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    if (data.Length + count > 1024 * 1024) throw new InvalidDataException();
                    data.Write(buffer, 0, count);
                }
                using var json = JsonDocument.Parse(data.ToArray());
                result = NativeUsageParser.Parse(id, json.RootElement, credential.Plan, now);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { result = Failure("Usage couldn't be refreshed. Check your connection; the dock will retry automatically."); }
        // Retain same-account data as visibly stale, never reuse a different account's reading.
        if (result.Error is not null && hasPrevious && previous.Reading.Metrics.Count > 0)
            result = previous.Reading with { IsStale = true, Error = result.Error };
        _cache[id] = (fingerprint, result, next);
        return result;
    }
    public void Dispose() { _http.Dispose(); if (_ownsFallback && _fallback is IDisposable d) d.Dispose(); }
}
