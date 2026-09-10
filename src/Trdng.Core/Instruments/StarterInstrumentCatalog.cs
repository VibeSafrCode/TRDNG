namespace Trdng.Core.Instruments;

public static class StarterInstrumentCatalog
{
    private static readonly string[] BaseAssets = ["APT", "BTC"];

    private static readonly IReadOnlyList<VenueInstrumentCapability> Entries =
        BuildEntries();

    public static IReadOnlyList<CanonicalInstrument> Instruments { get; } =
        Entries
            .Select(static entry => entry.Instrument)
            .Distinct()
            .OrderBy(static instrument => instrument.BaseAsset)
            .ThenBy(static instrument => instrument.Product)
            .ToArray();

    public static VenueInstrumentCapability? Find(
        CanonicalInstrument instrument,
        TradingVenue venue) =>
        Entries.SingleOrDefault(entry =>
            entry.Instrument == instrument && entry.Venue == venue);

    private static IReadOnlyList<VenueInstrumentCapability> BuildEntries()
    {
        var result = new List<VenueInstrumentCapability>();
        foreach (var baseAsset in BaseAssets)
        {
            var spot = new CanonicalInstrument(
                baseAsset,
                "USDT",
                MarketProduct.Spot);
            var perpetual = new CanonicalInstrument(
                baseAsset,
                "USDT",
                MarketProduct.Perpetual);

            result.AddRange(
            [
                new(spot, TradingVenue.Mexc, $"{baseAsset}USDT",
                    CapabilityAvailability.Available,
                    CapabilityAvailability.NotImplemented),
                new(perpetual, TradingVenue.Mexc, $"{baseAsset}_USDT",
                    CapabilityAvailability.Available,
                    CapabilityAvailability.Blocked),

                // Existing application adapters currently stream perpetual books.
                new(perpetual, TradingVenue.Gate, $"{baseAsset}_USDT",
                    CapabilityAvailability.Available,
                    CapabilityAvailability.NotImplemented),
                new(perpetual, TradingVenue.Bybit, $"{baseAsset}USDT",
                    CapabilityAvailability.Available,
                    CapabilityAvailability.NotImplemented)
            ]);
        }

        return result;
    }
}
