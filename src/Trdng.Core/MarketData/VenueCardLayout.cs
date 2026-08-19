using Trdng.Core.Instruments;

namespace Trdng.Core.MarketData;

public sealed record VenueCardDefinition(
    TradingVenue Venue,
    string Symbol,
    bool MarketDataAvailable,
    string EmptyState);

public static class VenueCardLayout
{
    private static readonly TradingVenue[] StableOrder =
        [TradingVenue.Mexc, TradingVenue.Gate, TradingVenue.Bybit];

    public static IReadOnlyList<VenueCardDefinition> Build(CanonicalInstrument instrument) =>
        StableOrder.Select(venue =>
        {
            var capability = StarterInstrumentCatalog.Find(instrument, venue);
            return new VenueCardDefinition(
                venue,
                capability?.VenueSymbol ?? $"{instrument.BaseAsset}/{instrument.QuoteAsset}",
                capability?.CanStreamMarketData == true,
                capability?.CanStreamMarketData == true
                    ? string.Empty
                    : instrument.Product == MarketProduct.Spot
                        ? "SPOT-АДАПТЕР ЕЩЁ НЕ РЕАЛИЗОВАН"
                        : "PUBLIC PERPETUAL НЕ РЕАЛИЗОВАН");
        }).ToArray();
}
