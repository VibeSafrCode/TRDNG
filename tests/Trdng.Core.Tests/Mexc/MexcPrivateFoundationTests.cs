using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Trdng.Core.Credentials;
using Trdng.Mexc.Private;

namespace Trdng.Core.Tests.Mexc;

public sealed class MexcPrivateFoundationTests
{
    [Fact]
    public void HmacMatchesIndependentComputationForContradictoryDocumentationVector()
    {
        const string query = "symbol=BTCUSDT&side=BUY&type=LIMIT&quantity=1&price=11&recvWindow=5000&timestamp=1644489390087";
        var secret = Encoding.UTF8.GetBytes("45d0b3c26f2644f19bfb98b07741b2f5");
        var signature = MexcHmacSigner.Sign(
            secret, query);
        var independent = ManualHmacSha256(secret, Encoding.UTF8.GetBytes(query));
        // The official page checked 2026-08-19 prints two contradictory signatures
        // for this input. Cryptographic recomputation agrees with fd3e..., not 323c....
        Assert.Equal("fd3e4e8543c5188531eb7279d68ae7d26a573d0fc5ab0d18eb692451654d837a",
            signature);
        Assert.Equal(independent, signature);
    }

    private static string ManualHmacSha256(byte[] key, byte[] message)
    {
        const int blockSize = 64;
        var normalized = key.Length > blockSize ? SHA256.HashData(key) : key.ToArray();
        Array.Resize(ref normalized, blockSize);
        var innerPad = new byte[blockSize];
        var outerPad = new byte[blockSize];
        for (var index = 0; index < blockSize; index++)
        {
            innerPad[index] = (byte)(normalized[index] ^ 0x36);
            outerPad[index] = (byte)(normalized[index] ^ 0x5c);
        }
        var innerInput = innerPad.Concat(message).ToArray();
        var innerHash = SHA256.HashData(innerInput);
        var outerInput = outerPad.Concat(innerHash).ToArray();
        try { return Convert.ToHexStringLower(SHA256.HashData(outerInput)); }
        finally
        {
            CryptographicOperations.ZeroMemory(normalized);
            CryptographicOperations.ZeroMemory(innerPad);
            CryptographicOperations.ZeroMemory(outerPad);
            CryptographicOperations.ZeroMemory(innerInput);
            CryptographicOperations.ZeroMemory(innerHash);
            CryptographicOperations.ZeroMemory(outerInput);
        }
    }

    [Fact]
    public void BuilderUsesStableEncodingOrderAndHeaderOnlyApiKey()
    {
        var query = MexcSignedRequestBuilder.BuildCanonicalQuery("/api/v3/openOrders",
            new Dictionary<string, string?> { ["symbol"] = "APT USDT" },
            1_700_000_000_000, 5000);
        Assert.Equal("recvWindow=5000&symbol=APT%20USDT&timestamp=1700000000000", query);
        using var signed = MexcSignedRequestBuilder.BuildGet("/api/v3/openOrders",
            new Dictionary<string, string?> { ["symbol"] = "APTUSDT" },
            1_700_000_000_000, 5000, Encoding.UTF8.GetBytes("synthetic-key"),
            Encoding.UTF8.GetBytes("synthetic-secret"));
        Assert.Equal("MEXC SIGNED REQUEST · REDACTED", signed.ToString());
        Assert.Throws<InvalidOperationException>(() => MexcSignedRequestBuilder.BuildGet(
            "/api/v3/order", new Dictionary<string, string?>(), 1, 5000, [1], [2]));
        foreach (var invalid in new byte[][]
                 { [], [65, 13, 66], [0xff], new byte[MexcSignedRequestBuilder.MaxApiKeyBytes + 1] })
            Assert.Throws<InvalidOperationException>(() => MexcSignedRequestBuilder.BuildGet(
                "/api/v3/account", new Dictionary<string, string?>(), 1, 5000,
                invalid, [2]));
    }

    [Fact]
    public void TimeOffsetHonorsRttAgeAndOverflowFailClosed()
    {
        var sync = new MexcTimeSynchronizer(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1));
        Assert.False(sync.TryTimestamp(1_000, out _));
        Assert.False(sync.Record(5_000, 1_000, 2_001));
        Assert.True(sync.Record(5_000, 1_000, 1_200));
        Assert.True(sync.TryTimestamp(1_300, out var timestamp));
        Assert.Equal(5_200, timestamp);
        Assert.False(sync.TryTimestamp(11_201, out _));
        Assert.Throws<InvalidDataException>(() => MexcTimeSynchronizer.ParseServerTime(
            Encoding.UTF8.GetBytes("{\"serverTime\":0}")));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MexcTimeSynchronizer(TimeSpan.Zero, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MexcTimeSynchronizer(TimeSpan.FromSeconds(1), TimeSpan.Zero));
        Assert.False(sync.Record(long.MaxValue, 1, long.MaxValue));
        Assert.False(sync.TryTimestamp(long.MaxValue, out _));
    }

    [Fact]
    public async Task CredentialProviderReadsTypedLeasesAndFailsClosed()
    {
        var vault = new FakeVault();
        var provider = new MexcCredentialProvider(vault);
        Assert.Equal(MexcPrivateState.NotConfigured, (await provider.ReadAsync()).State);
        vault.Set(MexcCredentialProvider.ApiKeyIdentity, "key");
        Assert.Equal(MexcPrivateState.NotConfigured, (await provider.ReadAsync()).State);
        vault.Set(MexcCredentialProvider.SecretIdentity, "secret");
        using var lease = (await provider.ReadAsync()).Lease!;
        Assert.Equal("key", Encoding.UTF8.GetString(lease.ApiKey));
        Assert.Equal("secret", Encoding.UTF8.GetString(lease.Secret));
    }

    [Fact]
    public async Task ReadOnlyClientParsesAccountAndOpenOrdersWithoutRetry()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, "{\"canTrade\":true,\"accountType\":\"SPOT\",\"balances\":[{\"asset\":\"USDT\",\"free\":\"12.5\",\"locked\":\"1\"}]}"),
            Json(HttpStatusCode.OK, "[{\"symbol\":\"APTUSDT\",\"orderId\":\"o1\",\"clientOrderId\":\"c1\",\"price\":\"5\",\"origQty\":\"2\",\"executedQty\":\"1\",\"status\":\"NEW\",\"side\":\"BUY\",\"type\":\"LIMIT\",\"time\":1,\"updateTime\":2}]")
        );
        var client = Client(handler);
        var account = await client.GetAccountAsync();
        var orders = await client.GetOpenOrdersAsync("aptusdt");
        Assert.Equal(MexcPrivateState.Ready, account.State);
        Assert.Equal(12.5m, account.Value!.Balances.Single().Free);
        Assert.Equal("o1", orders.Value!.Single().OrderId);
        Assert.Equal(2, handler.Count);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        Assert.True(handler.AllHadApiKeyHeader);
        Assert.True(handler.AllHadSignature);
    }

    [Fact]
    public async Task ReadOnlyCallsNeverReadOrderTestIdentities()
    {
        var vault = new FakeVault();
        vault.Set(MexcCredentialProvider.ApiKeyIdentity, "readonly-key");
        vault.Set(MexcCredentialProvider.SecretIdentity, "readonly-secret");
        vault.Set(MexcOrderTestCredentialProvider.ApiKeyIdentity, "test-key");
        vault.Set(MexcOrderTestCredentialProvider.SecretIdentity, "test-secret");
        var time = new MexcTimeSynchronizer(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
        time.Record(10_000, 9_900, 10_000);
        using var client = new MexcPrivateClient(new QueueHandler(Json(HttpStatusCode.OK,
            "{\"canTrade\":true,\"accountType\":\"SPOT\",\"balances\":[]}")),
            new MexcCredentialProvider(vault), new MexcOrderTestCredentialProvider(vault),
            time, () => 10_100, TimeSpan.FromSeconds(1), 5000, new MexcPrivateAudit(8));
        Assert.Equal(MexcPrivateState.Ready, (await client.GetAccountAsync()).State);
        Assert.Contains(MexcCredentialProvider.ApiKeyIdentity, vault.ReadIdentities);
        Assert.Contains(MexcCredentialProvider.SecretIdentity, vault.ReadIdentities);
        Assert.DoesNotContain(MexcOrderTestCredentialProvider.ApiKeyIdentity, vault.ReadIdentities);
        Assert.DoesNotContain(MexcOrderTestCredentialProvider.SecretIdentity, vault.ReadIdentities);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, 700007, MexcPrivateState.PermissionDenied)]
    [InlineData(HttpStatusCode.TooManyRequests, 429, MexcPrivateState.RateLimited)]
    [InlineData(HttpStatusCode.BadRequest, 700003, MexcPrivateState.TimeUnsynced)]
    [InlineData(HttpStatusCode.InternalServerError, 500, MexcPrivateState.Unavailable)]
    public async Task ErrorsAreTypedAndNeverRetried(HttpStatusCode status, int code,
        MexcPrivateState expected)
    {
        var handler = new QueueHandler(Json(status, $"{{\"code\":{code},\"msg\":\"sentinel-secret\"}}"));
        var audit = new MexcPrivateAudit(4, () => DateTimeOffset.UnixEpoch);
        var client = Client(handler, audit);
        var result = await client.GetAccountAsync();
        Assert.Equal(expected, result.State);
        Assert.Equal(1, handler.Count);
        Assert.DoesNotContain("sentinel", string.Join('|', audit.Events));
    }

    [Fact]
    public async Task MalformedPayloadIsErrorAndCallerCancellationPropagates()
    {
        var malformed = new QueueHandler(Json(HttpStatusCode.OK, "{}"));
        Assert.Equal(MexcPrivateState.Error, (await Client(malformed).GetAccountAsync()).State);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Client(new QueueHandler(Json(HttpStatusCode.OK, "{}"))).GetAccountAsync(cancelled.Token));
    }

    [Fact]
    public async Task StaleTimeBlocksBeforeCredentialsAndTransport()
    {
        var handler = new QueueHandler(Json(HttpStatusCode.OK, "{}"));
        var vault = new FakeVault();
        var client = new MexcPrivateClient(handler,
            new MexcCredentialProvider(vault),
            new MexcOrderTestCredentialProvider(vault),
            new MexcTimeSynchronizer(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)),
            () => 10_000, TimeSpan.FromSeconds(1), 5000, new MexcPrivateAudit(4));
        Assert.Equal(MexcPrivateState.TimeUnsynced, (await client.GetAccountAsync()).State);
        Assert.Equal(0, handler.Count);
        Assert.Equal(0, vault.ReadCount);
    }

    [Fact]
    public async Task PublicServerTimeSyncUsesOnlyGetAndBecomesReady()
    {
        var handler = new QueueHandler(Json(HttpStatusCode.OK, "{\"serverTime\":10050}"));
        var ticks = new Queue<long>([10_000, 10_100]);
        var client = new MexcPrivateClient(handler,
            new MexcCredentialProvider(new FakeVault()),
            new MexcOrderTestCredentialProvider(new FakeVault()),
            new MexcTimeSynchronizer(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1)),
            () => ticks.Dequeue(), TimeSpan.FromSeconds(1), 5000, new MexcPrivateAudit(4));
        Assert.Equal(MexcPrivateState.Ready, await client.SynchronizeTimeAsync());
        Assert.Single(handler.Requests);
        Assert.Equal((HttpMethod.Get, "/api/v3/time"), handler.Requests.Single());
    }

    [Fact]
    public async Task ChunkedOversizeStopsAtMaxPlusOneWithoutReadingRemainder()
    {
        var stream = new CountingStream(2_000_000);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StreamContent(stream) };
        response.Content.Headers.ContentLength = null;
        var result = await Client(new QueueHandler(response)).GetAccountAsync();
        Assert.Equal(MexcPrivateState.Error, result.State);
        Assert.Equal(1_048_577, stream.BytesRead);
    }

    [Fact]
    public async Task RedirectIsNeverFollowedAndProductionHandlerDisablesIt()
    {
        using var production = MexcPrivateClient.CreateProductionHandler();
        Assert.False(production.AllowAutoRedirect);
        var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
        redirect.Headers.Location = new Uri("https://example.invalid/steal");
        var handler = new QueueHandler(redirect, Json(HttpStatusCode.OK, "{}"));
        var result = await Client(handler).GetAccountAsync();
        Assert.Equal(MexcPrivateState.Error, result.State);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task SigningFailureIsTypedAndDoesNotReachTransport()
    {
        var handler = new QueueHandler(Json(HttpStatusCode.OK, "{}"));
        var vault = new FakeVault();
        vault.SetBytes(MexcCredentialProvider.ApiKeyIdentity, [65, 13, 66]);
        vault.Set(MexcCredentialProvider.SecretIdentity, "secret");
        var time = new MexcTimeSynchronizer(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
        time.Record(10_000, 9_900, 10_000);
        using var client = new MexcPrivateClient(handler, new MexcCredentialProvider(vault),
            new MexcOrderTestCredentialProvider(vault),
            time, () => 10_100, TimeSpan.FromSeconds(1), 5000, new MexcPrivateAudit(4));
        Assert.Equal(MexcPrivateState.Error, (await client.GetAccountAsync()).State);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public void TimeAndAuditRemainBoundedUnderConcurrency()
    {
        var sync = new MexcTimeSynchronizer(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
        var audit = new MexcPrivateAudit(16, () => DateTimeOffset.UnixEpoch);
        Parallel.For(0, 1_000, index =>
        {
            var sent = 10_000L + index;
            Assert.True(sync.Record(20_000L + index, sent, sent + 10));
            audit.Add(MexcPrivateAuditAction.Account, MexcPrivateState.Ready);
            _ = audit.Events;
        });
        Parallel.For(0, 1_000, _ =>
        {
            Assert.True(sync.TryTimestamp(12_000, out var timestamp));
            Assert.True(timestamp > 0);
        });
        Assert.Equal(16, audit.Events.Count);
    }

    private static MexcPrivateClient Client(QueueHandler handler, MexcPrivateAudit? audit = null)
    {
        var vault = new FakeVault();
        vault.Set(MexcCredentialProvider.ApiKeyIdentity, "synthetic-key");
        vault.Set(MexcCredentialProvider.SecretIdentity, "synthetic-secret");
        var time = new MexcTimeSynchronizer(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
        time.Record(10_000, 9_900, 10_000);
        return new MexcPrivateClient(handler, new MexcCredentialProvider(vault),
            new MexcOrderTestCredentialProvider(vault),
            time, () => 10_100, TimeSpan.FromSeconds(1), 5000,
            audit ?? new MexcPrivateAudit(8));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public int Count { get; private set; }
        public List<(HttpMethod Method, string Path)> Requests { get; } = [];
        public bool AllHadApiKeyHeader { get; private set; } = true;
        public bool AllHadSignature { get; private set; } = true;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Count++;
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            AllHadApiKeyHeader &= request.Headers.Contains("X-MEXC-APIKEY");
            AllHadSignature &= request.RequestUri.Query.Contains("signature=", StringComparison.Ordinal);
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class FakeVault : ICredentialVault
    {
        private readonly Dictionary<CredentialIdentity, byte[]> _values = [];
        public int ReadCount { get; private set; }
        public List<CredentialIdentity> ReadIdentities { get; } = [];
        public void Set(CredentialIdentity identity, string value) =>
            _values[identity] = Encoding.UTF8.GetBytes(value);
        public void SetBytes(CredentialIdentity identity, byte[] value) =>
            _values[identity] = value.ToArray();
        public ValueTask<CredentialReadResult> ReadAsync(CredentialIdentity identity,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            ReadIdentities.Add(identity);
            return ValueTask.FromResult(_values.TryGetValue(identity, out var value)
                ? new CredentialReadResult(CredentialVaultState.Stored,
                    new SecretLease(value.ToArray()), "STORED")
                : new CredentialReadResult(CredentialVaultState.NotConfigured, null, "NONE"));
        }
        public ValueTask<CredentialVaultResult> StoreAsync(CredentialIdentity identity,
            ReadOnlyMemory<byte> secret, bool overwrite, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<CredentialVaultResult> RevokeAsync(CredentialIdentity identity,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<CredentialVaultResult> GetStatusAsync(CredentialIdentity identity,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CountingStream(long length) : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = length - BytesRead;
            if (remaining <= 0) return 0;
            var take = (int)Math.Min(count, remaining);
            Array.Clear(buffer, offset, take);
            BytesRead += take;
            return take;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = length - BytesRead;
            if (remaining <= 0) return ValueTask.FromResult(0);
            var take = (int)Math.Min(buffer.Length, remaining);
            buffer.Span[..take].Clear();
            BytesRead += take;
            return ValueTask.FromResult(take);
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
