using System.Net;
using System.Text;
using Trdng.Core.Credentials;
using Trdng.Core.Instruments;
using Trdng.Core.Orders;
using Trdng.Mexc.Private;

namespace Trdng.Core.Tests.Mexc;

public sealed class MexcOrderTestTests
{
    [Fact]
    public void ExactConfirmedBuyAndSellMapToOfficialMarketFields()
    {
        var buy = Authorized(Intent(OrderSide.Buy, OrderSizingMode.QuoteNotional, 10));
        var sell = Authorized(Intent(OrderSide.Sell, OrderSizingMode.BaseQuantity, 2));
        var buyValues = MexcOrderTestPolicy.Parameters(buy)!;
        var sellValues = MexcOrderTestPolicy.Parameters(sell)!;
        Assert.Equal("10", buyValues["quoteOrderQty"]);
        Assert.False(buyValues.ContainsKey("quantity"));
        Assert.Equal("2", sellValues["quantity"]);
        Assert.False(sellValues.ContainsKey("quoteOrderQty"));
        Assert.Equal("APTUSDT", buyValues["symbol"]);
    }

    [Fact]
    public void UnsupportedSizingProductAndMismatchedEvidenceFailClosed()
    {
        Assert.Null(MexcOrderTestPolicy.Parameters(Authorized(
            Intent(OrderSide.Buy, OrderSizingMode.BaseQuantity, 1))));
        var perpetual = Intent(OrderSide.Sell, OrderSizingMode.BaseQuantity, 1) with
        { Instrument = new("APT", "USDT", MarketProduct.Perpetual) };
        Assert.Null(MexcOrderTestPolicy.Parameters(Authorized(perpetual)));
        var intent = Intent(OrderSide.Buy, OrderSizingMode.QuoteNotional, 10);
        var confirmation = Confirm(intent);
        var otherValidation = MarketOrderValidator.Validate(intent with { SizingValue = 11 }, Filters());
        Assert.Null(MexcOrderTestAuthorization.From(confirmation, otherValidation));
        Assert.Null(MexcOrderTestAuthorization.From(
            new ConfirmationResult(ConfirmationStatus.Confirmed, "forged"),
            MarketOrderValidator.Validate(intent, Filters())));
    }

    [Fact]
    public async Task DedicatedBuilderUsesPostExactPathBodyAndSignature()
    {
        var values = MexcOrderTestPolicy.Parameters(Authorized(
            Intent(OrderSide.Buy, OrderSizingMode.QuoteNotional, 10)))!;
        var body = MexcSignedRequestBuilder.BuildOrderTestCanonicalBody(values, 1000, 5000);
        Assert.Equal("newClientOrderId=test-1&quoteOrderQty=10&recvWindow=5000&side=BUY&symbol=APTUSDT&timestamp=1000&type=MARKET", body);
        using var signed = MexcSignedRequestBuilder.BuildOrderTestPost(values, 1000, 5000,
            Encoding.ASCII.GetBytes("synthetic-key"), Encoding.ASCII.GetBytes("synthetic-secret"));
        Assert.Equal(HttpMethod.Post, signed.Request.Method);
        Assert.Equal("/api/v3/order/test", signed.Request.RequestUri!.AbsolutePath);
        Assert.Empty(signed.Request.RequestUri.Query);
        Assert.Equal("MEXC SIGNED REQUEST · REDACTED", signed.ToString());
        var wire = await signed.Request.Content!.ReadAsStringAsync();
        Assert.StartsWith(body + "&signature=", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic", signed.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DedicatedBuilderRejectsWrongSideFieldAndMalformedValues()
    {
        static Dictionary<string, string> Values(string side, string field, string value,
            string symbol = "APTUSDT") => new(StringComparer.Ordinal)
        {
            ["symbol"] = symbol, ["side"] = side, ["type"] = "MARKET",
            ["newClientOrderId"] = "test-1", [field] = value
        };
        foreach (var invalid in new[]
        {
            Values("BUY", "quantity", "1"),
            Values("SELL", "quoteOrderQty", "1"),
            Values("BUY", "quoteOrderQty", "-1"),
            Values("BUY", "quoteOrderQty", "+1"),
            Values("BUY", "quoteOrderQty", "01"),
            Values("BUY", "quoteOrderQty", ".1"),
            Values("BUY", "quoteOrderQty", "1."),
            Values("BUY", "quoteOrderQty", "1e2"),
            Values("BUY", "quoteOrderQty", "1,0"),
            Values("BUY", "quoteOrderQty", " 1"),
            Values("BUY", "quoteOrderQty", "0"),
            Values("BUY", "quoteOrderQty", "79228162514264337593543950336"),
            Values("BUY", "quoteOrderQty", new string('1', 65)),
            Values("BUY", "quoteOrderQty", "1", "aptusdt"),
            Values("BUY", "quoteOrderQty", "1", "APT/USDT")
        })
            Assert.Throws<InvalidOperationException>(() =>
                MexcSignedRequestBuilder.BuildOrderTestCanonicalBody(invalid, 1, 5000));

        var trailingZero = Values("BUY", "quoteOrderQty", "1.0");
        Assert.Contains("quoteOrderQty=1.0",
            MexcSignedRequestBuilder.BuildOrderTestCanonicalBody(trailingZero, 1, 5000));
        Assert.Equal("1", MexcDecimalWire.Format(1.0m));
        Assert.Equal("0.1", MexcDecimalWire.Format(0.100m));
    }

    [Fact]
    public async Task OrderTestReadsOnlyTradePermissionIdentities()
    {
        var vault = new FakeVault();
        vault.Set(MexcCredentialProvider.ApiKeyIdentity, "readonly-key");
        vault.Set(MexcCredentialProvider.SecretIdentity, "readonly-secret");
        vault.Set(MexcOrderTestCredentialProvider.ApiKeyIdentity, "test-key");
        vault.Set(MexcOrderTestCredentialProvider.SecretIdentity, "test-secret");
        var time = new MexcTimeSynchronizer(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
        time.Record(10_000, 9_900, 10_000);
        using var client = new MexcPrivateClient(
            new CaptureHandler(new(HttpStatusCode.OK) { Content = new StringContent("{}") }),
            new MexcCredentialProvider(vault), new MexcOrderTestCredentialProvider(vault),
            time, () => 10_100, TimeSpan.FromSeconds(1), 5000, new MexcPrivateAudit(8));
        Assert.Equal(MexcOrderTestState.TestReady, await client.TestOrderAsync(Authorized(
            Intent(OrderSide.Buy, OrderSizingMode.QuoteNotional, 10))));
        Assert.Contains(MexcOrderTestCredentialProvider.ApiKeyIdentity, vault.Reads);
        Assert.Contains(MexcOrderTestCredentialProvider.SecretIdentity, vault.Reads);
        Assert.DoesNotContain(MexcCredentialProvider.ApiKeyIdentity, vault.Reads);
        Assert.DoesNotContain(MexcCredentialProvider.SecretIdentity, vault.Reads);
    }

    [Fact]
    public async Task SuccessIsTestReadyAndNeverExecutionState()
    {
        var handler = new CaptureHandler(new(HttpStatusCode.OK)
        { Content = new StringContent("{}") });
        using var client = Client(handler);
        var authorization = Authorized(Intent(OrderSide.Buy, OrderSizingMode.QuoteNotional, 10));
        var state = await client.TestOrderAsync(authorization);
        Assert.Equal(MexcOrderTestState.TestReady, state);
        Assert.Equal(1, handler.Count);
        Assert.Equal((HttpMethod.Post, "/api/v3/order/test"), handler.Requests.Single());
        Assert.True(handler.HadApiKey);
        Assert.Contains("signature=", handler.Body);
        Assert.Equal(MexcOrderTestState.TestRejected,
            await client.TestOrderAsync(authorization));
        Assert.Equal(1, handler.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, 700007, MexcOrderTestState.PermissionDenied)]
    [InlineData(HttpStatusCode.BadRequest, 700003, MexcOrderTestState.TimeUnsynced)]
    [InlineData(HttpStatusCode.TooManyRequests, 429, MexcOrderTestState.RateLimited)]
    public async Task ErrorsAreMaskedAndNeverRetried(HttpStatusCode status, int code,
        MexcOrderTestState expected)
    {
        var handler = new CaptureHandler(new(status)
        { Content = new StringContent($"{{\"code\":{code},\"msg\":\"sentinel-secret\"}}") });
        var audit = new MexcPrivateAudit(4);
        using var client = Client(handler, audit);
        Assert.Equal(expected, await client.TestOrderAsync(Authorized(
            Intent(OrderSide.Sell, OrderSizingMode.BaseQuantity, 1))));
        Assert.Equal(1, handler.Count);
        Assert.DoesNotContain("sentinel", string.Join('|', audit.Events), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sentinel", MexcOrderTestPresentation.Masked(expected), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingTimeMetadataAndOversizeBlockWithoutRetry()
    {
        var handler = new CaptureHandler(new(HttpStatusCode.OK) { Content = new StringContent("{}") });
        using var stale = Client(handler, synchronized: false);
        Assert.Equal(MexcOrderTestState.TimeUnsynced,
            await stale.TestOrderAsync(Authorized(Intent(OrderSide.Buy, OrderSizingMode.QuoteNotional, 10))));
        Assert.Equal(0, handler.Count);

        var raw = new byte[1_048_577];
        var oversizeHandler = new CaptureHandler(new(HttpStatusCode.OK)
        { Content = new ByteArrayContent(raw) });
        using var oversized = Client(oversizeHandler);
        Assert.Equal(MexcOrderTestState.Error,
            await oversized.TestOrderAsync(Authorized(Intent(OrderSide.Buy, OrderSizingMode.QuoteNotional, 10))));
        Assert.Equal(1, oversizeHandler.Count);
    }

    [Fact]
    public async Task RejectedAuthorizationNeverTouchesCredentialsOrNetwork()
    {
        var handler = new CaptureHandler(new(HttpStatusCode.OK));
        using var client = Client(handler);
        Assert.Equal(MexcOrderTestState.TestRejected, await client.TestOrderAsync(null));
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task RedirectAndCallerCancellationAreFailClosedWithoutRetry()
    {
        var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
        redirect.Headers.Location = new Uri("https://example.invalid/steal");
        var handler = new CaptureHandler(redirect);
        using var client = Client(handler);
        Assert.Equal(MexcOrderTestState.Error, await client.TestOrderAsync(Authorized(
            Intent(OrderSide.Buy, OrderSizingMode.QuoteNotional, 10))));
        Assert.Equal(1, handler.Count);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(
            new CaptureHandler(new(HttpStatusCode.OK))).TestOrderAsync(Authorized(
                Intent(OrderSide.Sell, OrderSizingMode.BaseQuantity, 1)), cancelled.Token));
    }

    [Fact]
    public void ProductionOrderPathCannotBeBuilt()
    {
        var forbidden = "/api/v3/" + "order";
        Assert.Throws<InvalidOperationException>(() => MexcSignedRequestBuilder.BuildGet(
            forbidden, new Dictionary<string, string?>(), 1, 5000, [65], [66]));
        Assert.DoesNotContain(forbidden, typeof(MexcSignedRequestBuilder).GetMethods()
            .Select(method => method.Name));
    }

    private static MarketOrderIntent Intent(OrderSide side, OrderSizingMode mode, decimal value) =>
        new(TradingVenue.Mexc, new("APT", "USDT", MarketProduct.Spot), side,
            OrderType.Market, mode, value, "test-1");

    private static OrderFilterSet Filters() => new(0.1m, 100m, 0.1m, 1m, 100m, 0.01m);

    private static ConfirmationResult Confirm(MarketOrderIntent intent)
    {
        var audit = new DryRunAuditTrail(8);
        var controller = new DryRunConfirmationController(TimeSpan.FromSeconds(5), audit,
            () => DateTimeOffset.UnixEpoch, () => "token");
        var profile = new RiskProfile("SIMULATION", RiskProfileMode.Simulation, true,
            intent.Venue, intent.Instrument, intent.Side, intent.SizingMode,
            100m, 100m, TimeSpan.FromSeconds(1));
        Assert.True(controller.DisengageForSimulation(profile));
        var validation = MarketOrderValidator.Validate(intent, Filters());
        var prepared = controller.Prepare(intent, validation, profile,
            new ReferencePrice(5m, DateTimeOffset.UnixEpoch)).Candidate!;
        return controller.Confirm(prepared.Token, intent);
    }

    private static MexcOrderTestAuthorization Authorized(MarketOrderIntent intent)
    {
        var validation = MarketOrderValidator.Validate(intent, Filters());
        return MexcOrderTestAuthorization.From(Confirm(intent), validation)!;
    }

    private static MexcPrivateClient Client(CaptureHandler handler,
        MexcPrivateAudit? audit = null, bool synchronized = true)
    {
        var vault = new FakeVault();
        vault.Set(MexcOrderTestCredentialProvider.ApiKeyIdentity, "synthetic-key");
        vault.Set(MexcOrderTestCredentialProvider.SecretIdentity, "synthetic-secret");
        var time = new MexcTimeSynchronizer(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
        if (synchronized) time.Record(10_000, 9_900, 10_000);
        return new MexcPrivateClient(handler, new MexcCredentialProvider(vault),
            new MexcOrderTestCredentialProvider(vault), time,
            () => 10_100, TimeSpan.FromSeconds(1), 5000, audit ?? new MexcPrivateAudit(8));
    }

    private sealed class CaptureHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int Count { get; private set; }
        public List<(HttpMethod Method, string Path)> Requests { get; } = [];
        public bool HadApiKey { get; private set; }
        public string Body { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Count++;
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            HadApiKey = request.Headers.Contains("X-MEXC-APIKEY");
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }

    private sealed class FakeVault : ICredentialVault
    {
        private readonly Dictionary<CredentialIdentity, byte[]> _values = [];
        public List<CredentialIdentity> Reads { get; } = [];
        public void Set(CredentialIdentity identity, string value) =>
            _values[identity] = Encoding.UTF8.GetBytes(value);
        public ValueTask<CredentialReadResult> ReadAsync(CredentialIdentity identity,
            CancellationToken cancellationToken = default)
        {
            Reads.Add(identity);
            return ValueTask.FromResult(_values.TryGetValue(identity, out var value)
                ? new CredentialReadResult(CredentialVaultState.Stored,
                    new SecretLease(value.ToArray()), "STORED")
                : new CredentialReadResult(CredentialVaultState.NotConfigured, null, "NONE"));
        }
        public ValueTask<CredentialVaultResult> StoreAsync(CredentialIdentity identity,
            ReadOnlyMemory<byte> secret, bool overwrite,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<CredentialVaultResult> RevokeAsync(CredentialIdentity identity,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<CredentialVaultResult> GetStatusAsync(CredentialIdentity identity,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
