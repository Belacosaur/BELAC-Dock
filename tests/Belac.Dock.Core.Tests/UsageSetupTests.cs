using Belac.Dock.Core.Models;
using Belac.Dock.Core.Usage;

namespace Belac.Dock.Core.Tests;

public class UsageSetupTests
{
    private static ProviderUsage Provider(bool stale = false, string? error = null, bool metrics = true) =>
        new("fixture", "Fixture", null, metrics ? [new UsageMetric("quota", "Quota", 0, 0, 100, 100, "%", null, null, null, null)] : [], DateTimeOffset.Now, stale, error);

    [Fact]
    public void Process_running_does_not_imply_connection() =>
        Assert.False(UsageSetupStatus.From(UsageSnapshot.Failed(DateTimeOffset.Now, "offline"), true, true).CanFinish);

    [Fact]
    public void Empty_successful_response_needs_provider_setup()
    {
        var state = UsageSetupStatus.From(UsageSnapshot.Empty(DateTimeOffset.Now), false, false);
        Assert.False(state.CanFinish);
        Assert.Contains("waiting", state.Title);
    }

    [Fact]
    public void Cached_data_during_connection_failure_cannot_finish() =>
        Assert.False(UsageSetupStatus.From(new(DateTimeOffset.Now, [Provider()], "offline"), true, true).CanFinish);

    [Fact]
    public void Stale_errored_or_empty_providers_cannot_finish() =>
        Assert.False(UsageSetupStatus.From(new(DateTimeOffset.Now, [Provider(stale: true), Provider(error: "login"), Provider(metrics: false)], null), true, true).CanFinish);

    [Fact]
    public void Zero_percent_and_partial_success_can_finish_without_install_detection() =>
        Assert.True(UsageSetupStatus.From(new(DateTimeOffset.Now, [Provider(), Provider(error: "login")], null), false, false).CanFinish);
}
