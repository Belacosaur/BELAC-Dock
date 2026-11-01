using System.Globalization;
using System.Text.Json;
using Belac.Dock.Core.Models;

namespace Belac.Dock.Infrastructure;

// Protocol mappings adapted from OpenTokenUsage (MIT); see THIRD_PARTY_NOTICES.md.
public static class NativeUsageParser
{
    public static JsonElement Child(JsonElement root, string key) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var value) ? value : default;
    public static string? Text(JsonElement root, string key) => Child(root, key) is var v && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    public static double? Number(JsonElement root, string key)
    {
        var v = Child(root, key);
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && double.IsFinite(n)) return n;
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out n) && double.IsFinite(n)) return n;
        return null;
    }
    private static DateTimeOffset? Date(JsonElement root, string key, bool milliseconds = false)
    {
        var n = Number(root, key);
        if (n is not null)
        {
            try { return milliseconds ? DateTimeOffset.FromUnixTimeMilliseconds(checked((long)n.Value)) : DateTimeOffset.FromUnixTimeSeconds(checked((long)n.Value)); }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException) { return null; }
        }
        return DateTimeOffset.TryParse(Text(root, key), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;
    }
    public static ProviderUsage Parse(string id, JsonElement root, string? plan, DateTimeOffset now)
    {
        var metrics = new List<UsageMetric>();
        void Add(string key, string label, double? percent, DateTimeOffset? reset, double? seconds = null)
        {
            if (percent is null || percent < 0) return;
            metrics.Add(new(key, label, Math.Clamp(percent.Value, 0, 100), null, null, null, "%", reset, null,
                seconds is > 0 and < 31536000 ? TimeSpan.FromSeconds(seconds.Value) : null, null));
        }
        if (id == "codex")
        {
            plan = Text(root, "plan_type") ?? plan;
            foreach (var key in new[] { "primary_window", "secondary_window" })
            {
                var window = Child(Child(root, "rate_limit"), key);
                var seconds = Number(window, "limit_window_seconds");
                Add(key, seconds >= 604800 ? "Weekly" : key == "primary_window" ? "Current session" : "Additional limit", Number(window, "used_percent"), Date(window, "reset_at"), seconds);
            }
            var review = Child(Child(root, "code_review_rate_limit"), "primary_window");
            Add("code_review", "Code review", Number(review, "used_percent"), Date(review, "reset_at"), Number(review, "limit_window_seconds"));
        }
        else if (id == "claude")
        {
            foreach (var (key, label) in new[] { ("five_hour", "Current session"), ("seven_day", "All models · weekly"), ("seven_day_opus", "Opus · weekly"), ("seven_day_sonnet", "Sonnet · weekly"), ("seven_day_omelette", "Claude Design · weekly") })
            {
                var window = Child(root, key);
                Add(key, label, Number(window, "utilization"), Date(window, "resets_at"), key == "five_hour" ? 18000 : 604800);
            }
        }
        else if (id == "cursor")
        {
            var usage = Child(root, "planUsage");
            var reset = Date(root, "billingCycleEnd", true);
            var total = Number(usage, "totalPercentUsed");
            var limit = Number(usage, "limit");
            var remaining = Number(usage, "remaining");
            if (total is null && limit > 0 && remaining is not null) total = (limit - remaining) / limit * 100;
            Add("total", "Total usage", total, reset);
            Add("auto", "Auto usage", Number(usage, "autoPercentUsed"), reset);
            Add("api", "API usage", Number(usage, "apiPercentUsed"), reset);
        }
        return new(id, Name(id), plan, metrics, metrics.Count > 0 ? now : null, false,
            metrics.Count == 0 ? "The provider returned no supported usage limits for this account. Check your plan's usage dashboard." : null);
    }
    public static string Name(string id) => id switch { "codex" => "Codex", "claude" => "Claude", "cursor" => "Cursor", _ => id };
}
