using Belac.Dock.Core.Models;

namespace Belac.Dock.Core.Usage;

public sealed record UsageSetupStatus(string Title, string Detail, bool CanFinish)
{
    public static bool HasReading(ProviderUsage provider) => !provider.IsStale && string.IsNullOrWhiteSpace(provider.Error) && provider.Metrics.Count > 0;

    public static UsageSetupStatus From(UsageSnapshot snapshot, bool running, bool installed)
    {
        if (snapshot.SourceError is not null)
            return new("No connection yet", running
                ? "OpenTokenUsage is running, but its local connection is unavailable. Restart OpenTokenUsage, wait for its panel to load, then check again. If this continues, open the setup guide."
                : installed ? "Open OpenTokenUsage, leave it running in the tray, then check again."
                : "Install OpenTokenUsage using the download button, open it, then check again.", false);
        var count = snapshot.Providers.Count(HasReading);
        if (count == 0)
            return new("Connected — waiting for readings", "The local connection works. Enable your providers in OpenTokenUsage and follow any sign-in prompts there. Refresh its panel until usage appears, then check again here.", false);
        return new($"Readings received from {count} provider{(count == 1 ? "" : "s")}",
            "You can show these providers on the dock now. Missing one? Enable it and resolve its sign-in or refresh error in OpenTokenUsage. Readings are cached; this check does not sign in or force a provider refresh.", true);
    }
}
