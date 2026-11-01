using System.Net;
using System.Text.Json;
using Belac.Dock.Core.Abstractions;
using Belac.Dock.Core.Models;
using Belac.Dock.Infrastructure;

namespace Belac.Dock.Core.Tests;

public class NativeUsageTests
{
    private const string Codex = """{"plan_type":"pro","rate_limit":{"primary_window":{"used_percent":0,"reset_at":1800000000,"limit_window_seconds":18000},"secondary_window":{"used_percent":52,"limit_window_seconds":604800}}}""";
    [Theory]
    [InlineData("codex", Codex, 2, 0)]
    [InlineData("claude", "{\"five_hour\":{\"utilization\":12,\"resets_at\":\"2026-11-02T12:00:00Z\"}}", 1, 12)]
    [InlineData("cursor", "{\"billingCycleEnd\":\"1800000000000\",\"planUsage\":{\"totalPercentUsed\":38.3,\"autoPercentUsed\":0}}", 2, 38.3)]
    [InlineData("cursor", "{\"planUsage\":{\"limit\":100,\"remaining\":75}}", 1, 25)]
    public void Parses_provider_limits_without_inventing_missing_values(string id, string json, int count, double percent)
    {
        using var doc = JsonDocument.Parse(json);
        var result = NativeUsageParser.Parse(id, doc.RootElement, null, DateTimeOffset.UtcNow);
        Assert.Equal(count, result.Metrics.Count);
        Assert.Equal(percent, result.Metrics[0].UsedPercent);
        Assert.Null(result.Error);
    }
    [Fact]
    public void Unknown_response_is_not_zero_usage()
    {
        using var doc = JsonDocument.Parse("{}");
        var result = NativeUsageParser.Parse("codex", doc.RootElement, null, DateTimeOffset.UtcNow);
        Assert.Empty(result.Metrics);
        Assert.NotNull(result.Error);
    }
    [Fact]
    public async Task Reads_directly_and_never_calls_disabled_companion()
    {
        using var handler = new Handler(r => { Assert.Equal("chatgpt.com", r.RequestUri!.Host); Assert.Equal("fixture", r.Headers.Authorization!.Parameter); return Json(Codex); });
        var fallback = new Fallback();
        using var source = new NativeUsageSource(() => new() { IncludeWslCodex = false, NativeProviders = ["codex"] }, new Credentials(), handler, fallback);
        var result = await source.GetSnapshotAsync(default);
        Assert.Equal(2, result.Providers[0].Metrics.Count);
        Assert.Equal(0, fallback.Calls);
        await source.GetSnapshotAsync(default);
        Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task Missing_credentials_do_not_send_network_requests()
    {
        using var handler = new Handler(_ => throw new Exception("Must not send"));
        using var source = new NativeUsageSource(() => new() { NativeProviders = ["claude"] }, new Credentials { Value = null }, handler);
        var result = await source.GetSnapshotAsync(default);
        Assert.Contains("Sign in", result.Providers[0].Error);
        Assert.Equal(0, handler.Calls);
    }
    [Fact]
    public async Task Account_switch_does_not_reuse_previous_accounts_cached_reading()
    {
        var credentials = new Credentials();
        using var handler = new Handler(_ => Json(Codex));
        using var source = new NativeUsageSource(() => new() { IncludeWslCodex = false, NativeProviders = ["codex"] }, credentials, handler);
        await source.GetSnapshotAsync(default);
        credentials.Value = new() { AccessToken = "second-account" };
        await source.GetSnapshotAsync(default);
        Assert.Equal(2, handler.Calls);
    }
    [Fact]
    public async Task Rate_limit_honors_retry_after_and_retains_stale_readings()
    {
        var time = DateTimeOffset.UtcNow;
        var calls = 0;
        using var handler = new Handler(_ => { if (++calls == 1) return Json(Codex); var response = new HttpResponseMessage((HttpStatusCode)429); response.Headers.RetryAfter = new(TimeSpan.FromHours(1)); return response; });
        using var source = new NativeUsageSource(() => new() { IncludeWslCodex = false, NativeProviders = ["codex"] }, new Credentials(), handler, clock: () => time);
        await source.GetSnapshotAsync(default);
        time = time.AddMinutes(3);
        var stale = await source.GetSnapshotAsync(default);
        Assert.True(stale.Providers[0].IsStale);
        Assert.NotEmpty(stale.Providers[0].Metrics);
        time = time.AddMinutes(10);
        await source.GetSnapshotAsync(default);
        Assert.Equal(2, handler.Calls);
    }
    [Theory]
    [InlineData(401)] [InlineData(403)] [InlineData(302)] [InlineData(500)]
    public async Task Failures_are_actionable_without_exposing_response_or_token(int status)
    {
        using var handler = new Handler(_ => new((HttpStatusCode)status) { Content = new StringContent("SECRET_RESPONSE") });
        using var source = new NativeUsageSource(() => new() { IncludeWslCodex = false, NativeProviders = ["codex"] }, new Credentials(), handler);
        var error = (await source.GetSnapshotAsync(default)).Providers[0].Error!;
        Assert.DoesNotContain("SECRET", error);
        Assert.DoesNotContain("fixture", error);
        Assert.NotEmpty(error);
    }
    [Fact]
    public async Task Cancelled_request_is_not_cached_as_failure()
    {
        using var handler = new Handler(_ => throw new OperationCanceledException());
        using var source = new NativeUsageSource(() => new(), new Credentials(), handler);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.GetSnapshotAsync(cancel.Token));
    }
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    [Fact]
    public async Task Wsl_and_windows_accounts_have_separate_ids_and_caches()
    {
        using var handler = new Handler(_ => Json(Codex));
        var wsl = new WslCredentials();
        using var source = new NativeUsageSource(() => new() { NativeProviders = ["codex"] }, new Credentials(), handler, wsl: wsl);
        var result = await source.GetSnapshotAsync(default);
        Assert.Equal(new[] { "codex", "codex-wsl-fixture" }, result.Providers.Select(p => p.Id));
        Assert.Contains("WSL", result.Providers[1].DisplayName);
        Assert.Equal(2, handler.Calls);
        await source.GetSnapshotAsync(default);
        Assert.Equal(2, handler.Calls);
        wsl.Accounts = [];
        var stopped = await source.GetSnapshotAsync(default);
        Assert.True(stopped.Providers[1].IsStale);
        Assert.False(stopped.Providers[0].IsStale);
        Assert.Equal(2, handler.Calls);
    }
    [Fact]
    public async Task Wsl_can_be_disabled_without_reading_its_credentials()
    {
        using var handler = new Handler(_ => Json(Codex));
        var wsl = new WslCredentials();
        using var source = new NativeUsageSource(() => new() { NativeProviders = ["codex"], IncludeWslCodex = false }, new Credentials(), handler, wsl: wsl);
        Assert.Single((await source.GetSnapshotAsync(default)).Providers);
        Assert.Equal(0, wsl.Calls);
    }
    [Fact]
    public async Task Wsl_failure_does_not_break_windows_usage()
    {
        using var handler = new Handler(_ => Json(Codex));
        using var source = new NativeUsageSource(() => new() { NativeProviders = ["codex"] }, new Credentials(), handler, wsl: new WslCredentials { Fail = true });
        var result = await source.GetSnapshotAsync(default);
        Assert.Single(result.Providers);
        Assert.Null(result.Providers[0].Error);
    }
    private sealed class WslCredentials : IWslCodexCredentials
    {
        public int Calls;
        public bool Fail;
        public IReadOnlyList<WslCodexAccount> Accounts = [new("codex-wsl-fixture", "Codex · WSL (Fixture)", new() { AccessToken = "wsl-fixture" })];
        public Task<IReadOnlyList<WslCodexAccount>> ReadAsync(CancellationToken token)
        {
            Calls++;
            if (Fail) throw new IOException("WSL unavailable");
            return Task.FromResult(Accounts);
        }
    }
    [Fact]
    public async Task Fallback_is_called_only_when_explicitly_enabled()
    {
        var fallback = new Fallback();
        using var source = new NativeUsageSource(() => new() { NativeProviders = [], CompanionFallback = true }, fallback: fallback);
        await source.GetSnapshotAsync(default);
        Assert.Equal(1, fallback.Calls);
    }
    [Fact]
    public async Task Oversized_provider_response_is_rejected()
    {
        using var handler = new Handler(_ => Json(new string('x', 1024 * 1024 + 1)));
        using var source = new NativeUsageSource(() => new() { IncludeWslCodex = false, NativeProviders = ["codex"] }, new Credentials(), handler);
        var result = await source.GetSnapshotAsync(default);
        Assert.Empty(result.Providers[0].Metrics);
        Assert.NotNull(result.Providers[0].Error);
    }
    private sealed class Credentials : INativeUsageCredentials
    {
        public NativeUsageCredential? Value { get; set; } = new() { AccessToken = "fixture" };
        public Task<NativeUsageCredential?> ReadAsync(string provider, CancellationToken token) => Task.FromResult(Value);
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Calls++; return Task.FromResult(response(request)); }
    }
    private sealed class Fallback : IUsageProviderSource
    {
        public int Calls;
        public Task<UsageSnapshot> GetSnapshotAsync(CancellationToken token) { Calls++; return Task.FromResult(UsageSnapshot.Empty(DateTimeOffset.UtcNow)); }
    }
}
