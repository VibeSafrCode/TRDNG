using System.Net;
using System.Text;
using Trdng.Core.Credentials;
using Trdng.Core.Instruments;
using Trdng.Core.Orders;
using Trdng.Mexc.MarketData;
using Trdng.Mexc.Private;

namespace Trdng.Core.Tests.Mexc;

public sealed class MexcOrderTestProbeTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(10_100);
    private static readonly CanonicalInstrument Apt = new("APT", "USDT", MarketProduct.Spot);

    [Fact]
    public void BuyUsesDocumentedMinimumAndRetainsMissingSupportCaveat()
    {
        var result = Derive(OrderSide.Buy, Metadata(quoteSupport: null), cap: 10);
        Assert.Equal(MexcProbeCandidateState.Prepared, result.State);
        Assert.Equal("UNPROVEN_UNTIL_ORDER_TEST", result.Code);
        Assert.Equal(1, result.Candidate!.Value);
        Assert.Equal(OrderSizingMode.QuoteNotional, result.Candidate.SizingMode);
        Assert.Equal(MexcProbeCaveat.QuoteOrderQtySupportUnproven,
            result.Candidate.Caveats);
        Assert.Equal(Now.AddMinutes(1), result.Candidate.MetadataValidUntil);
        Assert.True(MexcOrderTestProbePolicy.HasValidFingerprint(result.Candidate));
    }

    [Fact]
    public void BuyExplicitValueMustStayInsideDocumentedLimitsAndTinyCap()
    {
        Assert.Equal(5, Derive(OrderSide.Buy, Metadata(null), 10, ownerValue: 5)
            .Candidate!.Value);
        Assert.Equal(MexcProbeCandidateState.Rejected,
            Derive(OrderSide.Buy, Metadata(null), 4, ownerValue: 5).State);
        Assert.Equal(MexcProbeCandidateState.Rejected,
            Derive(OrderSide.Buy, Metadata(null) with
            { MinimumMarketQuoteAmount = 10, MaximumMarketQuoteAmount = 1 }, 10).State);
        Assert.Equal(MexcProbeCandidateState.Rejected,
            Derive(OrderSide.Buy, Metadata(false), 10).State);
    }

    [Fact]
    public void SellUsesMinimumAndRequiresFreshExecutablePriceAndCap()
    {
        var price = new ReferencePrice(5, Now);
        var result = Derive(OrderSide.Sell, Metadata(null), cap: 1,
            price: price, priceMaxAge: TimeSpan.FromSeconds(1));
        Assert.Equal(MexcProbeCandidateState.Prepared, result.State);
        Assert.Equal(0.0001m, result.Candidate!.Value);
        Assert.Equal(0.0005m, result.Candidate.EstimatedQuoteExposure);
        Assert.Equal(MexcProbeCaveat.BaseMaximumUnproven |
            MexcProbeCaveat.BaseStepUnproven, result.Candidate.Caveats);
        Assert.Equal(Now, result.Candidate.ReferencePriceObservedAt);
        Assert.Equal(Now.AddSeconds(1), result.Candidate.ReferencePriceValidUntil);
        Assert.Equal(MexcProbeCandidateState.Rejected,
            Derive(OrderSide.Sell, Metadata(null), 1).State);
        Assert.Equal(MexcProbeCandidateState.Rejected,
            Derive(OrderSide.Sell, Metadata(null), 0.0001m,
                price: price, priceMaxAge: TimeSpan.FromSeconds(1)).State);
    }

    [Fact]
    public void StaleWrongSideMarketAndMissingMinimumFailClosed()
    {
        Assert.Equal(MexcProbeCandidateState.Rejected,
            MexcOrderTestProbePolicy.Derive(Apt, OrderSide.Buy, Metadata(null),
                Now.AddMinutes(-2), Now, TimeSpan.FromMinutes(1), 10, "probe-1").State);
        Assert.Equal(MexcProbeCandidateState.Rejected,
            Derive(OrderSide.Buy, Metadata(null) with { TradeSideType = 3 }, 10).State);
        Assert.Equal(MexcProbeCandidateState.Rejected,
            Derive(OrderSide.Buy, Metadata(null) with { OrderTypes = ["LIMIT"] }, 10).State);
        Assert.Equal(MexcProbeCandidateState.Rejected,
            Derive(OrderSide.Sell, Metadata(null) with { MinimumBaseQuantity = null }, 10,
                price: new ReferencePrice(5, Now),
                priceMaxAge: TimeSpan.FromSeconds(1)).State);
    }

    [Fact]
    public void OwnerTokenIsExactExpiringAndSingleUse()
    {
        var now = Now;
        var candidate = Derive(OrderSide.Buy, Metadata(null), 10).Candidate!;
        var stop = new MexcProbeKillSwitch();
        var controller = new MexcProbeAuthorizationController(TimeSpan.FromSeconds(2), stop,
            () => now, () => "owner-token-0001");
        Assert.False(controller.Prepare(candidate).Prepared);
        stop.Disengage();
        var prepared = controller.Prepare(candidate).Value!;
        Assert.Null(controller.Confirm("wrong", candidate, true, true));
        prepared = controller.Prepare(candidate).Value!;
        Assert.Null(controller.Confirm(prepared.Token,
            candidate with { Value = 2 }, true, true));
        prepared = controller.Prepare(candidate).Value!;
        stop.Engage();
        Assert.Null(controller.Confirm(prepared.Token, candidate, true, true));
        stop.Disengage();
        prepared = controller.Prepare(candidate).Value!;
        Assert.Null(controller.Confirm(prepared.Token, candidate, false, true));
        prepared = controller.Prepare(candidate).Value!;
        now = now.AddSeconds(3);
        Assert.Null(controller.Confirm(prepared.Token, candidate, true, true));
    }

    [Fact]
    public async Task SuccessCreatesExactEvidenceOnlyAndReplayDoesNotCallAgain()
    {
        var candidate = Derive(OrderSide.Buy, Metadata(null), 10).Candidate!;
        var stop = new MexcProbeKillSwitch();
        stop.Disengage();
        var controller = new MexcProbeAuthorizationController(TimeSpan.FromSeconds(2), stop,
            () => Now, () => "owner-token-0001");
        var prepared = controller.Prepare(candidate).Value!;
        var authorization = controller.Confirm(prepared.Token, candidate, true, true)!;
        var handler = new CaptureHandler(new(HttpStatusCode.OK)
        { Content = new StringContent("{}") });
        using var client = Client(handler, stop);
        var result = await client.TestProbeAsync(authorization);
        Assert.Equal(MexcOrderTestState.TestReady, result.State);
        Assert.Equal(candidate.Fingerprint, result.Evidence!.CandidateFingerprint);
        Assert.Matches("^[a-f0-9]{64}$", result.Evidence.WireRequestFingerprint);
        Assert.Equal(candidate.MetadataObservedAt, result.Evidence.MetadataObservedAt);
        Assert.Equal(candidate.Caveats, result.Evidence.Caveats);
        Assert.Equal(1, handler.Count);
        Assert.Equal(MexcOrderTestState.TestRejected,
            (await client.TestProbeAsync(authorization)).State);
        Assert.Equal(1, handler.Count);
        Assert.False(typeof(MexcOrderTestAuthorization).IsAssignableFrom(
            typeof(MexcProbeAuthorization)));
        Assert.Null(typeof(OrderTestValidatedEvidence).GetProperty("Filters"));
    }

    [Fact]
    public async Task StopBlocksBeforeCredentialsAndNetwork()
    {
        var candidate = Derive(OrderSide.Buy, Metadata(null), 10).Candidate!;
        var stop = new MexcProbeKillSwitch();
        stop.Disengage();
        var controller = new MexcProbeAuthorizationController(TimeSpan.FromSeconds(2), stop,
            () => Now, () => "owner-token-0001");
        var prepared = controller.Prepare(candidate).Value!;
        var authorization = controller.Confirm(prepared.Token, candidate, true, true)!;
        stop.Engage();
        var handler = new CaptureHandler(new(HttpStatusCode.OK));
        using var client = Client(handler, stop);
        Assert.Equal(MexcOrderTestState.TestRejected,
            (await client.TestProbeAsync(authorization)).State);
        stop.Disengage();
        Assert.Equal(MexcOrderTestState.TestRejected,
            (await client.TestProbeAsync(authorization)).State);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task ExpiredMetadataBlocksBeforeCredentialsAndNetwork()
    {
        var candidate = Derive(OrderSide.Buy, Metadata(null), 10).Candidate!;
        var stop = new MexcProbeKillSwitch();
        stop.Disengage();
        var controller = new MexcProbeAuthorizationController(TimeSpan.FromSeconds(2), stop,
            () => Now, () => "owner-token-0001");
        var prepared = controller.Prepare(candidate).Value!;
        var authorization = controller.Confirm(prepared.Token, candidate, true, true)!;
        var handler = new CaptureHandler(new(HttpStatusCode.OK));
        using var client = Client(handler, stop, Now.AddMinutes(2).ToUnixTimeMilliseconds());
        Assert.Equal(MexcOrderTestState.TestRejected,
            (await client.TestProbeAsync(authorization)).State);
        Assert.Equal(0, handler.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"code\":0}")]
    [InlineData("<html></html>")]
    public async Task SuccessRequiresExactEmptyJsonObject(string body)
    {
        var (authorization, stop) = Authorization();
        var handler = new CaptureHandler(new(HttpStatusCode.OK)
        { Content = new StringContent(body) });
        using var client = Client(handler, stop);
        var result = await client.TestProbeAsync(authorization);
        Assert.Equal(MexcOrderTestState.Error, result.State);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task ConcurrentConfirmProducesAtMostOneAuthorization()
    {
        var candidate = Derive(OrderSide.Buy, Metadata(null), 10).Candidate!;
        var stop = new MexcProbeKillSwitch();
        stop.Disengage();
        var controller = new MexcProbeAuthorizationController(TimeSpan.FromSeconds(2), stop,
            () => Now, () => "owner-token-0001");
        var prepared = controller.Prepare(candidate).Value!;
        var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            start.Wait();
            return controller.Confirm(prepared.Token, candidate, true, true);
        })).ToArray();
        start.Set();
        var results = await Task.WhenAll(tasks);
        Assert.Single(results, result => result is not null);
    }

    [Fact]
    public void InvalidGeneratedTokenFailsClosed()
    {
        var stop = new MexcProbeKillSwitch();
        stop.Disengage();
        var controller = new MexcProbeAuthorizationController(TimeSpan.FromSeconds(2), stop,
            () => Now, () => "short");
        Assert.Equal("TOKEN_INVALID",
            controller.Prepare(Derive(OrderSide.Buy, Metadata(null), 10).Candidate!).Code);
    }

    [Fact]
    public void ExecutionBoundaryHasNoCallerSuppliedStopBoolean()
    {
        var method = typeof(MexcPrivateClient).GetMethod(nameof(MexcPrivateClient.TestProbeAsync))!;
        Assert.DoesNotContain(method.GetParameters(), parameter => parameter.ParameterType == typeof(bool));
    }

    [Fact]
    public async Task AuthorizationFromDifferentStopSourceIsRejectedAndBurned()
    {
        var (authorization, authorizationStop) = Authorization();
        var clientStop = new MexcProbeKillSwitch();
        clientStop.Disengage();
        var handler = new CaptureHandler(new(HttpStatusCode.OK)
        { Content = new StringContent("{}") });
        using var client = Client(handler, clientStop);
        Assert.Equal(MexcOrderTestState.TestRejected,
            (await client.TestProbeAsync(authorization)).State);
        authorizationStop.Disengage();
        Assert.Equal(MexcOrderTestState.TestRejected,
            (await client.TestProbeAsync(authorization)).State);
        Assert.Equal(0, handler.Count);
    }

    private static (MexcProbeAuthorization Authorization, MexcProbeKillSwitch Stop) Authorization()
    {
        var candidate = Derive(OrderSide.Buy, Metadata(null), 10).Candidate!;
        var stop = new MexcProbeKillSwitch();
        stop.Disengage();
        var controller = new MexcProbeAuthorizationController(TimeSpan.FromSeconds(2), stop,
            () => Now, () => "owner-token-0001");
        var prepared = controller.Prepare(candidate).Value!;
        return (controller.Confirm(prepared.Token, candidate, true, true)!, stop);
    }

    private static MexcProbeCandidateResult Derive(OrderSide side,
        MexcInstrumentMetadata metadata, decimal cap, decimal? ownerValue = null,
        ReferencePrice? price = null, TimeSpan? priceMaxAge = null) =>
        MexcOrderTestProbePolicy.Derive(Apt, side, metadata, Now, Now,
            TimeSpan.FromMinutes(1), cap, "probe-1", ownerValue, price, priceMaxAge);

    private static MexcInstrumentMetadata Metadata(bool? quoteSupport) => new(
        "APTUSDT", "APT", "USDT", 0.0001m, "1", true,
        ["LIMIT", "MARKET"], quoteSupport, 0.0001m, 1m, 600000m, 1);

    private static MexcPrivateClient Client(CaptureHandler handler,
        MexcProbeKillSwitch stop, long clock = 10_100)
    {
        var vault = new FakeVault();
        vault.Set(MexcOrderTestCredentialProvider.ApiKeyIdentity, "synthetic-key");
        vault.Set(MexcOrderTestCredentialProvider.SecretIdentity, "synthetic-secret");
        var time = new MexcTimeSynchronizer(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
        time.Record(10_000, 9_900, 10_000);
        return new MexcPrivateClient(handler, new MexcCredentialProvider(vault),
            new MexcOrderTestCredentialProvider(vault), time, () => clock,
            TimeSpan.FromSeconds(1), 5000, new MexcPrivateAudit(8), stop);
    }

    private sealed class CaptureHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Count++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/v3/order/test", request.RequestUri!.AbsolutePath);
            return Task.FromResult(response);
        }
    }

    private sealed class FakeVault : ICredentialVault
    {
        private readonly Dictionary<CredentialIdentity, byte[]> _values = [];
        public void Set(CredentialIdentity identity, string value) =>
            _values[identity] = Encoding.UTF8.GetBytes(value);
        public ValueTask<CredentialReadResult> ReadAsync(CredentialIdentity identity,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
            _values.TryGetValue(identity, out var value)
                ? new CredentialReadResult(CredentialVaultState.Stored,
                    new SecretLease(value.ToArray()), "STORED")
                : new CredentialReadResult(CredentialVaultState.NotConfigured, null, "NONE"));
        public ValueTask<CredentialVaultResult> StoreAsync(CredentialIdentity identity,
            ReadOnlyMemory<byte> secret, bool overwrite,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<CredentialVaultResult> RevokeAsync(CredentialIdentity identity,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<CredentialVaultResult> GetStatusAsync(CredentialIdentity identity,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
