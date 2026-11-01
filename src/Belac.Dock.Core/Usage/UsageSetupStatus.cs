using Belac.Dock.Core.Models;

namespace Belac.Dock.Core.Usage;

public sealed record UsageSetupStatus(string Title, string Detail, bool CanFinish)
{
    public static bool HasReading(ProviderUsage provider) => !provider.IsStale && string.IsNullOrWhiteSpace(provider.Error) && provider.Metrics.Count > 0;

    public static UsageSetupStatus From(UsageSnapshot snapshot, bool running, bool installed)
    {
        if (snapshot.SourceError is not null)
            return new("No connection yet", "Could not read usage. Check your connection and provider sign-in, then try again.", false);
        var count = snapshot.Providers.Count(HasReading);
        if (count == 0)
            return new("Connected — waiting for readings", "Select a provider and sign in to its Windows app. Check the provider messages below, then try again.", false);
        return new($"Readings received from {count} provider{(count == 1 ? "" : "s")}",
            "These providers are ready. You can finish even if another provider needs attention. Readings refresh automatically; repeated checks respect provider rate limits.", true);
    }
}
