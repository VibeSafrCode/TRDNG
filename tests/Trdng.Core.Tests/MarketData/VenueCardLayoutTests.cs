using Trdng.Core.Instruments;
using Trdng.Core.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class VenueCardLayoutTests
{
    [Theory]
    [InlineData(MarketProduct.Spot)]
    [InlineData(MarketProduct.Perpetual)]
    public void KeepsStableMexcGateBybitOrder(MarketProduct product)
    {
        var cards = VenueCardLayout.Build(new CanonicalInstrument("APT", "USDT", product));
        Assert.Equal([TradingVenue.Mexc, TradingVenue.Gate, TradingVenue.Bybit],
            cards.Select(card => card.Venue));
    }

    [Fact]
    public void SpotIncludesOnlyMexcAndDoesNotClaimOtherProductsAreMissing()
    {
        var cards = VenueCardLayout.Build(new CanonicalInstrument("BTC", "USDT", MarketProduct.Spot));
        Assert.True(cards.Single(card => card.Venue == TradingVenue.Mexc).MarketDataAvailable);
        Assert.All(cards.Where(card => card.Venue != TradingVenue.Mexc), card =>
        {
            Assert.False(card.MarketDataAvailable);
            Assert.Contains("АДАПТЕР", card.EmptyState);
        });
    }

    [Fact]
    public void PerpetualIncludesGateAndBybitButNotMexc()
    {
        var cards = VenueCardLayout.Build(new CanonicalInstrument("APT", "USDT", MarketProduct.Perpetual));
        Assert.False(cards[0].MarketDataAvailable);
        Assert.True(cards[1].MarketDataAvailable);
        Assert.True(cards[2].MarketDataAvailable);
    }
}
