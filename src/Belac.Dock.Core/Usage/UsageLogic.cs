using Belac.Dock.Core.Models;

namespace Belac.Dock.Core.Usage;

public static class RingMath
{
    public static double? NormalizePercent(double? value)
    {
        if (value is null)
            return null;

        var number = value.Value;
        if (double.IsNaN(number) || double.IsInfinity(number))
            return null;

        if (number < 0)
            return 0;

        return number > 100 ? 100 : number;
    }

    public static RingBand Band(double? rawPercent, double cautionThreshold, double criticalThreshold)
    {
        if (rawPercent is null || double.IsNaN(rawPercent.Value) || double.IsInfinity(rawPercent.Value))
            return RingBand.Unknown;

        var percent = Math.Max(0, rawPercent.Value);
        if (percent >= criticalThreshold)
            return RingBand.Critical;

        return percent >= cautionThreshold ? RingBand.Caution : RingBand.Normal;
    }
}

public static class RingLayout
{
    public static (UsageMetric? Outer, UsageMetric? Inner, int Extra) Select(IReadOnlyList<UsageMetric> metrics)
    {
        var ordered = metrics
            .Select((metric, index) => (metric, index))
            .Where(item => item.metric.UsedPercent is not null)
            .OrderBy(item => item.metric.Period is null ? 1 : 0)
            .ThenBy(item => item.metric.Period ?? TimeSpan.MaxValue)
            .ThenBy(item => item.index)
            .Select(item => item.metric)
            .ToList();

        if (ordered.Count == 0)
            return (null, null, 0);

        var inner = ordered.Count > 1 ? ordered[1] : null;
        return (ordered[0], inner, Math.Max(0, ordered.Count - (inner is null ? 1 : 2)));
    }
}

public static class ProviderOrdering
{
    public static IReadOnlyList<ProviderUsage> Order(IReadOnlyList<ProviderUsage> providers, UsageSettings settings)
    {
        var hidden = new HashSet<string>(settings.HiddenProviderIds, StringComparer.OrdinalIgnoreCase);
        return providers
            .Where(provider => !hidden.Contains(provider.Id))
            .Select((provider, index) => (provider, index))
            .OrderBy(item => UserRank(settings, item.provider.Id) == int.MaxValue ? 1 : 0)
            .ThenBy(item => UserRank(settings, item.provider.Id) == int.MaxValue
                ? DefaultRank(item.provider.Id)
                : UserRank(settings, item.provider.Id))
            .ThenBy(item => item.index)
            .Select(item => item.provider)
            .ToList();
    }

    private static int UserRank(UsageSettings settings, string id)
    {
        var index = settings.ProviderOrder.FindIndex(item => string.Equals(item, id, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : int.MaxValue;
    }

    private static int DefaultRank(string id)
    {
        var key = id.ToLowerInvariant();
        if (key.StartsWith("cursor", StringComparison.Ordinal)) return 0;
        if (key.StartsWith("codex", StringComparison.Ordinal)) return 1;
        if (key.StartsWith("claude", StringComparison.Ordinal)) return 2;
        if (key.StartsWith("grok", StringComparison.Ordinal)) return 3;
        return 100;
    }
}

public static class ResetClock
{
    public static string Relative(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is null)
            return "No reset time";

        var delta = resetsAt.Value - now;
        if (delta <= TimeSpan.Zero)
            return "due now";

        if (delta.TotalDays >= 2)
            return $"{(int)delta.TotalDays} days";

        if (delta.TotalHours >= 1)
            return $"{(int)delta.TotalHours}h {delta.Minutes}m";

        return $"{Math.Max(1, (int)Math.Ceiling(delta.TotalMinutes))}m";
    }

    public static string Describe(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is null)
            return "No reset time";

        return $"{resetsAt.Value.ToLocalTime():g} · {Relative(resetsAt, now)}";
    }
}
