namespace Trdng.Core.Instruments;

public sealed record VenueInstrumentCapability(
    CanonicalInstrument Instrument,
    TradingVenue Venue,
    string VenueSymbol,
    CapabilityAvailability MarketData,
    CapabilityAvailability Trading)
{
    public bool CanStreamMarketData =>
        MarketData == CapabilityAvailability.Available;

    public bool CanTrade => Trading == CapabilityAvailability.Available;
}
