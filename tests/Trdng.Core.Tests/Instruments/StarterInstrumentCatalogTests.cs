using Trdng.Core.Instruments;

namespace Trdng.Core.Tests.Instruments;

public sealed class StarterInstrumentCatalogTests
{
    [Fact]
    public void CanonicalIdentityNormalizesAssetCodes()
    {
        var instrument = new CanonicalInstrument(
            " btc ",
            "usdt",
            MarketProduct.Spot);

        Assert.Equal("BTC/USDT", instrument.PairId);
        Assert.Equal("BTC/USDT:SPOT", instrument.Id);
    }

    [Fact]
    public void SpotAndPerpetualAreDifferentInstruments()
    {
        var spot = new CanonicalInstrument("APT", "USDT", MarketProduct.Spot);
        var perpetual = new CanonicalInstrument(
            "APT",
            "USDT",
            MarketProduct.Perpetual);

        Assert.NotEqual(spot, perpetual);
        Assert.NotEqual(spot.Id, perpetual.Id);
    }

    [Theory]
    [InlineData(TradingVenue.Mexc, MarketProduct.Spot, "BTCUSDT")]
    [InlineData(TradingVenue.Mexc, MarketProduct.Perpetual, "BTC_USDT")]
    [InlineData(TradingVenue.Gate, MarketProduct.Perpetual, "BTC_USDT")]
    [InlineData(TradingVenue.Bybit, MarketProduct.Perpetual, "BTCUSDT")]
    public void MapsCanonicalPairToVenueSymbol(
        TradingVenue venue,
        MarketProduct product,
        string expectedSymbol)
    {
        var instrument = new CanonicalInstrument("BTC", "USDT", product);

        var capability = StarterInstrumentCatalog.Find(instrument, venue);

        Assert.NotNull(capability);
        Assert.Equal(expectedSymbol, capability.VenueSymbol);
    }

    [Fact]
    public void UnsupportedCombinationIsNotInvented()
    {
        var unknown = new CanonicalInstrument(
            "ETH",
            "USDT",
            MarketProduct.Perpetual);

        Assert.Null(StarterInstrumentCatalog.Find(unknown, TradingVenue.Bybit));
    }

    [Fact]
    public void ExistingPublicBooksRemainPerpetualOnly()
    {
        var perpetual = new CanonicalInstrument(
            "APT",
            "USDT",
            MarketProduct.Perpetual);
        var spot = new CanonicalInstrument("APT", "USDT", MarketProduct.Spot);

        Assert.True(StarterInstrumentCatalog.Find(perpetual, TradingVenue.Gate)!
            .CanStreamMarketData);
        Assert.True(StarterInstrumentCatalog.Find(perpetual, TradingVenue.Bybit)!
            .CanStreamMarketData);
        Assert.Null(StarterInstrumentCatalog.Find(spot, TradingVenue.Gate));
        Assert.Null(StarterInstrumentCatalog.Find(spot, TradingVenue.Bybit));
    }

    [Fact]
    public void MexcFuturesPrivateTradingIsExplicitlyBlocked()
    {
        var perpetual = new CanonicalInstrument(
            "BTC",
            "USDT",
            MarketProduct.Perpetual);

        var capability = StarterInstrumentCatalog.Find(
            perpetual,
            TradingVenue.Mexc);

        Assert.NotNull(capability);
        Assert.Equal(CapabilityAvailability.Blocked, capability.Trading);
        Assert.False(capability.CanTrade);
    }

    [Fact]
    public void NoStarterCapabilityClaimsTradingIsAvailable()
    {
        foreach (var instrument in StarterInstrumentCatalog.Instruments)
        foreach (var venue in Enum.GetValues<TradingVenue>())
        {
            Assert.False(
                StarterInstrumentCatalog.Find(instrument, venue)?.CanTrade ?? false);
        }
    }

    [Theory]
    [InlineData("APT")]
    [InlineData("BTC")]
    public void MexcSpotPublicMarketDataIsAvailableAfterSuccessfulAcceptanceSmoke(string baseAsset)
    {
        var capability = StarterInstrumentCatalog.Find(
            new CanonicalInstrument(baseAsset, "USDT", MarketProduct.Spot),
            TradingVenue.Mexc);

        Assert.NotNull(capability);
        Assert.Equal(CapabilityAvailability.Available, capability.MarketData);
        Assert.Equal(CapabilityAvailability.NotImplemented, capability.Trading);
    }
}
