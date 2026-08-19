using System.Text;
using Trdng.Core.Credentials;
using Trdng.Core.Instruments;
using Trdng.Core.Orders;
using Trdng.Mexc.MarketData;
using Trdng.Mexc.Private;

namespace Trdng.Core.Tests.Mexc;

public sealed class MexcOrderTestPreflightTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-19T12:00:00Z");

    [Fact]
    public void SanitizedLivePayloadRemainsNeedsMetadataWithoutQuoteSupportProof()
    {
        var metadata = Parse(quoteSupport: null);
        var mapped = MexcOrderTestMetadataMapper.Map(metadata, OrderSide.Buy);
        Assert.Equal(MexcOrderTestMetadataState.NeedsMetadata, mapped.State);
        Assert.Equal("QUOTE_ORDER_QTY_SUPPORT_MISSING", mapped.Code);
        Assert.Equal(MexcOrderTestMetadataState.NeedsMetadata,
            MexcOrderTestMetadataMapper.Map(metadata, OrderSide.Sell).State);
    }

    [Fact]
    public void ProvenBuyMetadataMapsOfficialMarketNotionalWithoutInventingStep()
    {
        var mapped = MexcOrderTestMetadataMapper.Map(Parse(quoteSupport: true), OrderSide.Buy);
        Assert.Equal(MexcOrderTestMetadataState.Available, mapped.State);
        Assert.Equal(1m, mapped.Filters!.MinimumQuoteNotional);
        Assert.Equal(600000m, mapped.Filters.MaximumQuoteNotional);
        Assert.Null(mapped.Filters.QuoteNotionalStep);
        Assert.Null(mapped.Filters.BaseQuantityStep);
    }

    [Fact]
    public void DisabledUnsupportedAndIncoherentMetadataFailClosed()
    {
        Assert.Equal(MexcOrderTestMetadataState.Blocked,
            MexcOrderTestMetadataMapper.Map(Parse(true, status: "2"), OrderSide.Buy).State);
        Assert.Equal(MexcOrderTestMetadataState.Blocked,
            MexcOrderTestMetadataMapper.Map(Parse(false), OrderSide.Buy).State);
        var inverted = Parse(true) with
        { MinimumMarketQuoteAmount = 10, MaximumMarketQuoteAmount = 1 };
        Assert.Equal(MexcOrderTestMetadataState.NeedsMetadata,
            MexcOrderTestMetadataMapper.Map(inverted, OrderSide.Buy).State);
    }

    [Fact]
    public void PassivePreflightShowsFreshnessRiskStopAndSeparatedProfiles()
    {
        var intent = Intent();
        var profile = new RiskProfile("SIMULATION", RiskProfileMode.Simulation, true,
            intent.Venue, intent.Instrument, intent.Side, intent.SizingMode,
            10, 1, TimeSpan.FromSeconds(2));
        var missing = new MexcCredentialProfilePresence(
            CredentialVaultState.NotConfigured, CredentialVaultState.NotConfigured);
        var stored = new MexcCredentialProfilePresence(
            CredentialVaultState.Stored, CredentialVaultState.Stored);

        var stopped = MexcOrderTestPreflightEvaluator.Evaluate(intent, Parse(true), Now,
            Now, TimeSpan.FromMinutes(1), profile, null, true, stored, missing);
        Assert.Equal(MexcOrderTestPreflightState.StopEngaged, stopped.State);
        Assert.Equal("STORED", stopped.ReadOnlyProfile);
        Assert.Equal("MISSING", stopped.OrderTestProfile);
        Assert.Equal(10, stopped.RiskCap);
        Assert.False(stopped.IsActionEnabled);

        var noKey = MexcOrderTestPreflightEvaluator.Evaluate(intent, Parse(true), Now,
            Now, TimeSpan.FromMinutes(1), profile, null, false, stored, missing);
        Assert.Equal(MexcOrderTestPreflightState.OrderTestKeyRequired, noKey.State);
        var presentation = MexcOrderTestPreflightPresentation.Masked(noKey);
        Assert.Contains("APTUSDT · SPOT · BUY 5", presentation);
        Assert.Contains("READ STORED · TEST MISSING", presentation);
        var eligible = MexcOrderTestPreflightEvaluator.Evaluate(intent, Parse(true), Now,
            Now, TimeSpan.FromMinutes(1), profile, null, false, stored, stored);
        Assert.Equal(MexcOrderTestPreflightState.Eligible, eligible.State);
        Assert.False(eligible.IsActionEnabled); // passive until separate owner gate
    }

    [Fact]
    public void MissingAndStaleMetadataBlockBeforeKeys()
    {
        var intent = Intent();
        var profile = new RiskProfile("SIMULATION", RiskProfileMode.Simulation, true,
            intent.Venue, intent.Instrument, intent.Side, intent.SizingMode,
            10, 1, TimeSpan.FromSeconds(2));
        var missing = new MexcCredentialProfilePresence(
            CredentialVaultState.NotConfigured, CredentialVaultState.NotConfigured);
        Assert.Equal(MexcOrderTestPreflightState.NeedsMetadata,
            MexcOrderTestPreflightEvaluator.Evaluate(intent, null, null, Now,
                TimeSpan.FromMinutes(1), profile, null, true, missing, missing).State);
        Assert.Equal(MexcOrderTestPreflightState.MetadataStale,
            MexcOrderTestPreflightEvaluator.Evaluate(intent, Parse(true), Now.AddMinutes(-2),
                Now, TimeSpan.FromMinutes(1), profile, null, true, missing, missing).State);
        Assert.Equal(MexcOrderTestPreflightState.NeedsMetadata,
            MexcOrderTestPreflightEvaluator.Evaluate(intent,
                Parse(true) with { Symbol = "BTCUSDT", BaseAsset = "BTC" }, Now,
                Now, TimeSpan.FromMinutes(1), profile, null, true, missing, missing).State);
    }

    private static MarketOrderIntent Intent() => new(TradingVenue.Mexc,
        new CanonicalInstrument("APT", "USDT", MarketProduct.Spot), OrderSide.Buy,
        OrderType.Market, OrderSizingMode.QuoteNotional, 5, "preflight-1");

    private static MexcInstrumentMetadata Parse(bool? quoteSupport, string status = "1")
    {
        var quoteField = quoteSupport is null ? "" :
            $",\"quoteOrderQtyMarketAllowed\":{quoteSupport.Value.ToString().ToLowerInvariant()}";
        var json = $"{{\"symbols\":[{{\"symbol\":\"APTUSDT\",\"status\":\"{status}\",\"baseAsset\":\"APT\",\"quoteAsset\":\"USDT\",\"quotePrecision\":4,\"baseSizePrecision\":\"0.0001\",\"isSpotTradingAllowed\":true,\"orderTypes\":[\"LIMIT\",\"MARKET\"],\"quoteAmountPrecisionMarket\":\"1\",\"maxQuoteAmountMarket\":\"600000\",\"tradeSideType\":1{quoteField}}}]}}";
        return MexcInstrumentMetadataClient.Parse(Encoding.UTF8.GetBytes(json), "APTUSDT");
    }
}
