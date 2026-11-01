using System.Net;
using Belac.Dock.Core.Abstractions;
using Belac.Dock.Core.Models;
using Belac.Dock.Core.Usage;

namespace Belac.Dock.Infrastructure;

public sealed class OpenTokenUsageSource : IUsageProviderSource, IDisposable
{
    public static readonly Uri DefaultBase = new("http://127.0.0.1:6736");
    private readonly HttpClient _http;

    public OpenTokenUsageSource(HttpMessageHandler? handler = null, Uri? baseAddress = null)
    {
        var uri = baseAddress ?? DefaultBase;
        if (!IsAllowedBase(uri))
            throw new ArgumentException("The usage API must be http://127.0.0.1:6736.", nameof(baseAddress));

        if (handler is null)
        {
            var sockets = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(2), AllowAutoRedirect = false, UseProxy = false };
            _http = new HttpClient(sockets) { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(5) };
        }
        else
        {
            _http = new HttpClient(handler, disposeHandler: false) { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(5) };
        }
    }

    public static bool IsAllowedBase(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp &&
        string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
        uri.Port == 6736;

    public async Task<UsageSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var observedAt = DateTimeOffset.Now;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var response = await _http.GetAsync("/v1/usage", HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return UsageSnapshot.Failed(observedAt, "This OpenTokenUsage build has no /v1/usage route.");

            if (!response.IsSuccessStatusCode)
                return UsageSnapshot.Failed(observedAt, $"Usage API returned HTTP {(int)response.StatusCode}.");

            if (response.Content.Headers.ContentLength > UsageSnapshotNormalizer.MaxBytes)
                return UsageSnapshot.Failed(observedAt, "Usage response exceeded 1 MB.");

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var payload = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
            {
                if (payload.Length + count > UsageSnapshotNormalizer.MaxBytes)
                    return UsageSnapshot.Failed(observedAt, "Usage response exceeded 1 MB.");
                payload.Write(buffer, 0, count);
            }

            return UsageSnapshotNormalizer.Parse(payload.ToArray(), observedAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return UsageSnapshot.Failed(observedAt, "OpenTokenUsage is not responding on 127.0.0.1:6736.");
        }
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
