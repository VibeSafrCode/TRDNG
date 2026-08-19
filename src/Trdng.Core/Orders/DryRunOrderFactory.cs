using Trdng.Core.Instruments;

namespace Trdng.Core.Orders;

public sealed record DryRunOrderResult(
    MarketOrderIntent? Intent,
    OrderValidationResult Validation)
{
    public bool Created => Intent is not null;
}

public sealed class DryRunOrderFactory
{
    private readonly ClientOrderIdGenerator _idGenerator;
    private readonly Func<CanonicalInstrument, TradingVenue, VenueInstrumentCapability?> _resolver;

    public DryRunOrderFactory(
        ClientOrderIdGenerator idGenerator,
        Func<CanonicalInstrument, TradingVenue, VenueInstrumentCapability?>? resolver = null)
    {
        _idGenerator = idGenerator;
        _resolver = resolver ?? StarterInstrumentCatalog.Find;
    }

    public DryRunOrderResult Create(
        TradingVenue venue,
        CanonicalInstrument instrument,
        OrderSide side,
        OrderSizingMode sizingMode,
        decimal sizingValue,
        OrderFilterSet? filters)
    {
        var capability = _resolver(instrument, venue);
        if (DryRunCapabilityPolicy.Evaluate(capability) == DryRunCapabilityMode.Denied)
            return Blocked("VENUE / PRODUCT IS BLOCKED OR UNSUPPORTED");

        var intent = new MarketOrderIntent(
            venue, instrument, side, OrderType.Market, sizingMode, sizingValue,
            _idGenerator.Next());
        return new(intent, MarketOrderValidator.Validate(intent, filters));
    }

    private static DryRunOrderResult Blocked(string message) =>
        new(null, new(OrderValidationStatus.Blocked, message));
}
