using System.Net;
using Belac.Dock.Core.Usage;
using Belac.Dock.Infrastructure;

namespace Belac.Dock.Core.Tests;

public class UsageTransportTests
{
    [Fact]
    public async Task Unknown_length_payload_is_stopped_at_size_limit()
    {
        var stream = new CountingStream(new byte[UsageSnapshotNormalizer.MaxBytes * 3]);
        using var handler = new ResponseHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnknownLengthContent(stream) });
        using var source = new OpenTokenUsageSource(handler);
        var result = await source.GetSnapshotAsync(CancellationToken.None);
        Assert.Contains("exceeded", result.SourceError);
        Assert.InRange(stream.BytesRead, UsageSnapshotNormalizer.MaxBytes + 1, UsageSnapshotNormalizer.MaxBytes + 8192);
    }

    [Fact]
    public async Task Redirect_response_is_reported_as_failure()
    {
        using var handler = new ResponseHandler(new HttpResponseMessage(HttpStatusCode.Redirect));
        using var source = new OpenTokenUsageSource(handler);
        var result = await source.GetSnapshotAsync(CancellationToken.None);
        Assert.Contains("302", result.SourceError);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://127.0.0.1:8080")]
    public void Remote_or_wrong_port_endpoints_are_rejected(string endpoint) =>
        Assert.Throws<ArgumentException>(() => new OpenTokenUsageSource(baseAddress: new Uri(endpoint)));

    private sealed class ResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response);
    }

    private sealed class CountingStream(byte[] buffer) : MemoryStream(buffer)
    {
        public int BytesRead { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += count;
            return count;
        }
    }

    private sealed class UnknownLengthContent(Stream stream) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream target, TransportContext? context) => stream.CopyToAsync(target);
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(stream);
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) => Task.FromResult(stream);
    }
}
