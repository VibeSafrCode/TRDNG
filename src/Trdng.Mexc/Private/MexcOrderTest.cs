using Trdng.Core.Instruments;
using Trdng.Core.Orders;

namespace Trdng.Mexc.Private;

public enum MexcOrderTestState
{
    TestReady, TestRejected, KeyRequired, KeychainDenied, TimeUnsynced,
    PermissionDenied, RateLimited, Unavailable, Error
}

public static class MexcOrderTestPresentation
{
    public static string Masked(MexcOrderTestState state) => state switch
    {
        MexcOrderTestState.TestReady => "MEXC ORDER TEST · TEST READY · NOT AN ORDER",
        MexcOrderTestState.TestRejected => "MEXC ORDER TEST · TEST REJECTED",
        MexcOrderTestState.TimeUnsynced => "MEXC ORDER TEST · TIME UNSYNCED",
        MexcOrderTestState.PermissionDenied => "MEXC ORDER TEST · PERMISSION DENIED",
        MexcOrderTestState.RateLimited => "MEXC ORDER TEST · RATE LIMITED",
        MexcOrderTestState.KeychainDenied => "MEXC ORDER TEST · KEYCHAIN DENIED",
        MexcOrderTestState.Unavailable => "MEXC ORDER TEST · UNAVAILABLE",
        MexcOrderTestState.Error => "MEXC ORDER TEST · ERROR",
        _ => "MEXC ORDER TEST · KEY REQUIRED"
    };
}

public sealed class MexcOrderTestAuthorization
{
    private int _consumed;
    private MexcOrderTestAuthorization(MarketOrderIntent intent,
        OrderValidationResult validation, RiskDecision riskDecision)
    { Intent = intent; Validation = validation; RiskDecision = riskDecision; }
    public MarketOrderIntent Intent { get; }
    public OrderValidationResult Validation { get; }
    public RiskDecision RiskDecision { get; }
    internal bool TryConsume() => Interlocked.Exchange(ref _consumed, 1) == 0;

    public static MexcOrderTestAuthorization? From(
        ConfirmationResult confirmation, OrderValidationResult validation)
    {
        var intent = confirmation.ConfirmedIntent;
        var risk = confirmation.ConfirmedRiskDecision;
        if (confirmation.Status != ConfirmationStatus.Confirmed || intent is null || risk is null ||
            !risk.IsAllowed || risk.ProfileMode != RiskProfileMode.Simulation ||
            validation.Status != OrderValidationStatus.Valid ||
            validation.ValidatedIntent != intent)
            return null;
        return new(intent, validation, risk);
    }
}

internal static class MexcOrderTestPolicy
{
    public static IReadOnlyDictionary<string, string>? Parameters(
        MexcOrderTestAuthorization authorization)
    {
        var intent = authorization.Intent;
        if (authorization.Validation.Status != OrderValidationStatus.Valid ||
            authorization.Validation.ValidatedIntent != intent ||
            !authorization.RiskDecision.IsAllowed ||
            authorization.RiskDecision.ProfileMode != RiskProfileMode.Simulation ||
            intent.Venue != TradingVenue.Mexc || intent.Instrument.Product != MarketProduct.Spot ||
            intent.Type != OrderType.Market || intent.Price is not null)
            return null;

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["symbol"] = intent.Instrument.VenueSymbol(TradingVenue.Mexc),
            ["side"] = intent.Side == OrderSide.Buy ? "BUY" : "SELL",
            ["type"] = "MARKET",
            ["newClientOrderId"] = intent.ClientOrderId
        };
        if (intent is { Side: OrderSide.Buy, SizingMode: OrderSizingMode.QuoteNotional })
            values["quoteOrderQty"] = MexcDecimalWire.Format(intent.SizingValue);
        else if (intent is { Side: OrderSide.Sell, SizingMode: OrderSizingMode.BaseQuantity })
            values["quantity"] = MexcDecimalWire.Format(intent.SizingValue);
        else return null;
        return values;
    }
}

internal static class CanonicalInstrumentExtensions
{
    public static string VenueSymbol(this CanonicalInstrument instrument, TradingVenue venue)
    {
        var capability = StarterInstrumentCatalog.Find(instrument, venue);
        if (capability is null || string.IsNullOrWhiteSpace(capability.VenueSymbol))
            throw new InvalidOperationException("MEXC symbol mapping unavailable.");
        return capability.VenueSymbol;
    }
}
