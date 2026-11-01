using System.Globalization;
using System.Text;
using System.Text.Json;
using Belac.Dock.Core.Models;

namespace Belac.Dock.Core.Usage;

public static class UsageSnapshotNormalizer
{
    public const int MaxBytes = 1_000_000;
    public const int MaxString = 240;
    public const int MaxProviders = 32;
    public const int MaxLines = 40;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    public static UsageSnapshot Parse(byte[] utf8, DateTimeOffset observedAt)
    {
        if (utf8.Length > MaxBytes)
            return UsageSnapshot.Failed(observedAt, "Usage response exceeded 1 MB.");

        try
        {
            using var document = JsonDocument.Parse(utf8);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return UsageSnapshot.Failed(observedAt, "Usage response was not a provider list.");

            var providers = new List<ProviderUsage>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (providers.Count >= MaxProviders)
                    break;

                if (ParseProvider(item, observedAt) is ProviderUsage provider)
                    providers.Add(provider);
            }

            return new UsageSnapshot(observedAt, providers, null);
        }
        catch (JsonException)
        {
            return UsageSnapshot.Failed(observedAt, "Usage response could not be read.");
        }
    }

    private static ProviderUsage? ParseProvider(JsonElement item, DateTimeOffset observedAt)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return null;

        var id = Clamp(ReadString(item, "providerId"));
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var displayName = Clamp(ReadString(item, "displayName"));
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = id;

        var plan = Clamp(ReadString(item, "plan"));
        var fetchedAt = ReadTime(item, "fetchedAt");
        var stale = fetchedAt is not null && observedAt - fetchedAt.Value > StaleAfter;
        var metrics = new List<UsageMetric>();
        if (item.TryGetProperty("lines", out var lines) && lines.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in lines.EnumerateArray())
            {
                if (metrics.Count >= MaxLines)
                    break;

                if (ParseLine(line) is UsageMetric metric)
                    metrics.Add(metric);
            }
        }

        return new ProviderUsage(id, displayName, plan, metrics, fetchedAt, stale, null);
    }

    private static UsageMetric? ParseLine(JsonElement line)
    {
        if (line.ValueKind != JsonValueKind.Object)
            return null;

        var type = ReadString(line, "type") ?? "";
        var label = Clamp(ReadString(line, "label")) ?? "Usage";
        var resetsAt = ReadTime(line, "resetsAt");
        var period = ReadPeriod(line);
        if (string.Equals(type, "progress", StringComparison.OrdinalIgnoreCase))
        {
            var used = ReadDecimal(line, "used");
            var limit = ReadDecimal(line, "limit");
            var format = ReadFormatKind(line);
            double? percent = null;
            if (string.Equals(format, "percent", StringComparison.OrdinalIgnoreCase) &&
                used is not null &&
                limit is > 0)
            {
                var ratio = (double)used.Value / (double)limit.Value * 100.0;
                if (!double.IsNaN(ratio) && !double.IsInfinity(ratio))
                    percent = ratio;
            }

            return new UsageMetric(
                Key: "progress:" + label,
                Label: label,
                UsedPercent: percent,
                Used: used,
                Limit: limit,
                Remaining: used is not null && limit is not null ? limit.Value - used.Value : null,
                Unit: format,
                ResetsAt: resetsAt,
                WindowLabel: label,
                Period: period,
                TextValue: null);
        }

        if (string.Equals(type, "text", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "badge", StringComparison.OrdinalIgnoreCase))
        {
            var value = Clamp(ReadString(line, "value"));
            return new UsageMetric(
                Key: type + ":" + label,
                Label: label,
                UsedPercent: null,
                Used: null,
                Limit: null,
                Remaining: null,
                Unit: null,
                ResetsAt: resetsAt,
                WindowLabel: label,
                Period: period,
                TextValue: value);
        }

        return null;
    }

    private static string? ReadFormatKind(JsonElement line)
    {
        if (!line.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.Object)
            return null;

        return ReadString(format, "kind");
    }

    private static TimeSpan? ReadPeriod(JsonElement line)
    {
        if (!line.TryGetProperty("periodDurationMs", out var value) || value.ValueKind != JsonValueKind.Number)
            return null;

        if (!value.TryGetDouble(out var ms) || ms <= 0 || ms > TimeSpan.FromDays(400).TotalMilliseconds)
            return null;

        return TimeSpan.FromMilliseconds(ms);
    }

    private static decimal? ReadDecimal(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
            return null;

        return value.TryGetDecimal(out var number) ? number : null;
    }

    private static DateTimeOffset? ReadTime(JsonElement element, string name)
    {
        var text = ReadString(element, name);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static string? Clamp(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var cleaned = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return cleaned.Length <= MaxString ? cleaned : cleaned[..MaxString];
    }
}

public static class CommandLineQuoting
{
    public static string Quote(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        if (value.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
            return value;

        var builder = new StringBuilder();
        builder.Append('"');
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                slashes++;
                continue;
            }

            if (character == '"')
            {
                builder.Append('\\', slashes * 2 + 1);
                builder.Append('"');
                slashes = 0;
                continue;
            }

            if (slashes > 0)
            {
                builder.Append('\\', slashes);
                slashes = 0;
            }

            builder.Append(character);
        }

        builder.Append('\\', slashes * 2);
        builder.Append('"');
        return builder.ToString();
    }
}
