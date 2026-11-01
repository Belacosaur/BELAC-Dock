using Belac.Dock.Core.Abstractions;
using Belac.Dock.Core.Models;

namespace Belac.Dock.Infrastructure;

public sealed class UsagePollingCoordinator : IDisposable
{
    private readonly IUsageProviderSource _source;
    private readonly Func<int> _intervalSeconds;
    private readonly TimeSpan? _delayOverride;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private int _refreshing;
    private UsageSnapshot _current = UsageSnapshot.Empty(DateTimeOffset.Now);

    public UsagePollingCoordinator(IUsageProviderSource source, Func<int>? intervalSeconds = null, TimeSpan? delayOverride = null)
    {
        _source = source;
        _intervalSeconds = intervalSeconds ?? (() => 90);
        _delayOverride = delayOverride;
    }

    public UsageSnapshot Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    public event EventHandler? Updated;

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => LoopAsync(token), token);
    }

    public void Stop()
    {
        if (_cts is null)
            return;

        _cts.Cancel();
        _cts.Dispose();
        _cts = null;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
            return;

        try
        {
            UsageSnapshot next;
            try
            {
                next = await _source.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                next = UsageSnapshot.Failed(DateTimeOffset.Now, "Usage refresh failed.");
            }

            lock (_gate)
            {
                _current = next.SourceError is not null && _current.Providers.Count > 0
                    ? _current.MarkStale(next.SourceError, next.ObservedAt)
                    : next;
            }

            Updated?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(token).ConfigureAwait(false);
                var delay = _delayOverride ?? TimeSpan.FromSeconds(Math.Clamp(_intervalSeconds(), 30, 900));
                await Task.Delay(delay, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public void Dispose() => Stop();
}
