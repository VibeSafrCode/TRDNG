using System.Net;
using Trdng.Bybit.MarketData;
using Trdng.Core.MarketData;
using Trdng.Gate.MarketData;
using Trdng.Mexc.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class BoundedHttpContentReaderTests
{
    private static readonly Uri Endpoint = new("https://example.test/public");

    [Fact]
    public async Task AcceptsExactBoundaryAndFragmentedReads()
    {
        var stream = new CountingReadStream([1, 2, 3, 4], maximumChunkBytes: 1);
        using var client = Client(HttpStatusCode.OK, stream, contentLength: 4);

        var body = await BoundedHttpContentReader.GetBytesAsync(client, Endpoint, 4);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, body);
        Assert.Equal(4, stream.BytesRead);
    }

    [Fact]
    public async Task RejectsDeclaredOversizeBeforeReadingBody()
    {
        var stream = new CountingReadStream([42]);
        using var client = Client(HttpStatusCode.OK, stream, contentLength: 5);

        var error = await Assert.ThrowsAsync<BoundedHttpContentException>(() =>
            BoundedHttpContentReader.GetBytesAsync(client, Endpoint, 4));

        Assert.Equal(BoundedHttpContentFailure.ContentLengthExceeded, error.Failure);
        Assert.Equal("HTTP_CONTENT_LENGTH_EXCEEDED", error.Message);
        Assert.Equal(0, stream.BytesRead);
    }

    [Fact]
    public async Task RejectsChunkedOversizeAfterSingleDiscriminatorByte()
    {
        var stream = new CountingReadStream(Enumerable.Range(0, 32)
            .Select(value => (byte)value).ToArray());
        using var client = Client(HttpStatusCode.OK, stream, contentLength: null);

        var error = await Assert.ThrowsAsync<BoundedHttpContentException>(() =>
            BoundedHttpContentReader.GetBytesAsync(client, Endpoint, 4));

        Assert.Equal(BoundedHttpContentFailure.BodyTooLarge, error.Failure);
        Assert.Equal("HTTP_BODY_TOO_LARGE", error.Message);
        Assert.Equal(5, stream.BytesRead);
    }

    [Fact]
    public async Task AcceptsEmptyBody()
    {
        var stream = new CountingReadStream([]);
        using var client = Client(HttpStatusCode.OK, stream, contentLength: null);

        var body = await BoundedHttpContentReader.GetBytesAsync(client, Endpoint, 4);

        Assert.Empty(body);
    }

    [Fact]
    public async Task RejectsNonSuccessWithoutReadingBody()
    {
        var stream = new CountingReadStream([1, 2, 3]);
        using var client = Client(HttpStatusCode.BadGateway, stream, contentLength: 3);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            BoundedHttpContentReader.GetBytesAsync(client, Endpoint, 4));

        Assert.Equal(0, stream.BytesRead);
    }

    [Fact]
    public async Task JsonReaderRejectsUnexpectedMediaTypeBeforeReadingBody()
    {
        var stream = new CountingReadStream([1, 2, 3]);
        using var client = Client(
            HttpStatusCode.OK,
            stream,
            contentLength: 3,
            mediaType: "application/octet-stream");

        var error = await Assert.ThrowsAsync<BoundedHttpContentException>(() =>
            BoundedHttpContentReader.GetJsonBytesAsync(client, Endpoint, 4));

        Assert.Equal(BoundedHttpContentFailure.UnexpectedMediaType, error.Failure);
        Assert.Equal("HTTP_MEDIA_TYPE_REJECTED", error.Message);
        Assert.Equal(0, stream.BytesRead);
    }

    [Fact]
    public void ProductionPublicHandlerDisablesRedirectsAndCookies()
    {
        using var handler = PublicHttpTransport.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);

        using var client = PublicHttpTransport.CreateClient(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(5), client.Timeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(BoundedHttpContentReader.MaximumSupportedBytes + 1)]
    public async Task RejectsUnsafeLimits(int maximumResponseBytes)
    {
        using var client = Client(HttpStatusCode.OK, new CountingReadStream([]), 0);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            BoundedHttpContentReader.GetBytesAsync(
                client, Endpoint, maximumResponseBytes));
    }

    [Fact]
    public async Task PropagatesCallerCancellationBeforeRequest()
    {
        using var client = new HttpClient(new SingleResponseHandler(
            new HttpResponseMessage(HttpStatusCode.OK)));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BoundedHttpContentReader.GetBytesAsync(
                client, Endpoint, 4, cancellation.Token));
    }

    [Fact]
    public async Task PublicMetadataClientsRejectOversizedDeclaredResponses()
    {
        await AssertDeclaredOversize(client =>
            new BybitInstrumentMetadataClient(client).GetLinearTickSizeAsync("APTUSDT"),
            4 * 1024 * 1024 + 1);
        await AssertDeclaredOversize(client =>
            new GateInstrumentMetadataClient(client).GetUsdtPerpetualCatalogAsync(),
            8 * 1024 * 1024 + 1);
        await AssertDeclaredOversize(client =>
            new MexcInstrumentMetadataClient(client).GetSpotCatalogResultAsync(),
            8 * 1024 * 1024 + 1);
    }

    [Theory]
    [InlineData(1000, 2 * 1024 * 1024 + 1)]
    [InlineData(5000, 8 * 1024 * 1024 + 1)]
    public async Task MexcSnapshotUsesDepthSpecificLimit(int depth, long contentLength)
    {
        var stream = new CountingReadStream([1]);
        using var client = Client(HttpStatusCode.OK, stream, contentLength);

        var error = await Assert.ThrowsAsync<BoundedHttpContentException>(() =>
            MexcPublicOrderBookClient.FetchSnapshotBytesAsync(
                client, Endpoint, depth));

        Assert.Equal(BoundedHttpContentFailure.ContentLengthExceeded, error.Failure);
        Assert.Equal(0, stream.BytesRead);
    }

    private static async Task AssertDeclaredOversize(
        Func<HttpClient, Task> action,
        long contentLength)
    {
        var stream = new CountingReadStream([1]);
        using var client = Client(HttpStatusCode.OK, stream, contentLength);
        var error = await Assert.ThrowsAsync<BoundedHttpContentException>(() => action(client));
        Assert.Equal(BoundedHttpContentFailure.ContentLengthExceeded, error.Failure);
        Assert.Equal(0, stream.BytesRead);
    }

    private static HttpClient Client(
        HttpStatusCode status,
        Stream stream,
        long? contentLength,
        string mediaType = "application/json")
    {
        var content = new StreamContent(stream);
        content.Headers.ContentLength = contentLength;
        content.Headers.ContentType = new(mediaType);
        return new HttpClient(new SingleResponseHandler(
            new HttpResponseMessage(status) { Content = content }));
    }

    private sealed class SingleResponseHandler(HttpResponseMessage response)
        : HttpMessageHandler
    {
        private int _sent;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref _sent, 1) != 0)
                throw new InvalidOperationException("Unexpected retry.");
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(response);
        }
    }

    private sealed class CountingReadStream(byte[] bytes, int maximumChunkBytes = int.MaxValue)
        : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, Math.Min(count, maximumChunkBytes));
            BytesRead += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = base.Read(buffer[..Math.Min(buffer.Length, maximumChunkBytes)]);
            BytesRead += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(
                buffer[..Math.Min(buffer.Length, maximumChunkBytes)],
                cancellationToken);
            BytesRead += read;
            return read;
        }
    }
}
