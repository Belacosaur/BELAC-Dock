namespace Belac.Dock.Core.Models;

public enum RingBand
{
    Unknown,
    Normal,
    Caution,
    Critical
}

public sealed record UsageMetric(
    string Key,
    string Label,
    double? UsedPercent,
    decimal? Used,
    decimal? Limit,
    decimal? Remaining,
    string? Unit,
    DateTimeOffset? ResetsAt,
    string? WindowLabel,
    TimeSpan? Period,
    string? TextValue);

public sealed record ProviderUsage(
    string Id,
    string DisplayName,
    string? Plan,
    IReadOnlyList<UsageMetric> Metrics,
    DateTimeOffset? FetchedAt,
    bool IsStale,
    string? Error)
{
    public ProviderUsage AsStale() => this with { IsStale = true };
}

public sealed record UsageSnapshot(
    DateTimeOffset ObservedAt,
    IReadOnlyList<ProviderUsage> Providers,
    string? SourceError)
{
    public static UsageSnapshot Empty(DateTimeOffset observedAt) => new(observedAt, [], null);

    public static UsageSnapshot Failed(DateTimeOffset observedAt, string error) => new(observedAt, [], error);

    public UsageSnapshot MarkStale(string error, DateTimeOffset observedAt) =>
        new(observedAt, Providers.Select(provider => provider.AsStale()).ToArray(), error);
}
