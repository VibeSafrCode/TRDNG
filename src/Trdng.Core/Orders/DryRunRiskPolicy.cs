using Trdng.Core.Instruments;

namespace Trdng.Core.Orders;

public enum RiskProfileMode { Simulation, Production }
public enum RiskDecisionStatus { Allowed, Blocked, NeedsConfiguration }

public sealed record RiskProfile(
    string Name,
    RiskProfileMode Mode,
    bool IsConfigured,
    TradingVenue Venue,
    CanonicalInstrument Instrument,
    OrderSide Side,
    OrderSizingMode SizingMode,
    decimal? MaximumQuoteNotional,
    decimal? MaximumBaseQuantity,
    TimeSpan ReferencePriceMaxAge)
{
    public static RiskProfile ProductionUnconfigured(
        TradingVenue venue, CanonicalInstrument instrument,
        OrderSide side, OrderSizingMode sizingMode) =>
        new("PRODUCTION · UNCONFIGURED", RiskProfileMode.Production, false,
            venue, instrument, side, sizingMode, null, null, TimeSpan.Zero);
}

public sealed record ReferencePrice(decimal Price, DateTimeOffset ObservedAt);

public sealed record RiskDecision(
    RiskDecisionStatus Status,
    string Reason,
    RiskProfileMode? ProfileMode = null,
    decimal? EstimatedNotional = null)
{
    public bool IsAllowed => Status == RiskDecisionStatus.Allowed;
}

public static class DryRunRiskPolicy
{
    public static RiskDecision Evaluate(
        MarketOrderIntent intent,
        OrderValidationResult officialFilterValidation,
        RiskProfile? profile,
        ReferencePrice? referencePrice,
        DateTimeOffset now)
    {
        if (profile is null || !profile.IsConfigured ||
            profile.Mode != RiskProfileMode.Simulation)
            return Needs("RISK LIMITS ARE NOT CONFIGURED");
        if (officialFilterValidation.Status != OrderValidationStatus.Valid ||
            officialFilterValidation.ValidatedIntent != intent)
            return Needs("OFFICIAL FILTER VALIDATION REQUIRED");
        if (profile.Venue != intent.Venue || profile.Instrument != intent.Instrument ||
            profile.Side != intent.Side || profile.SizingMode != intent.SizingMode)
            return Block("INTENT DOES NOT MATCH THE EXACT RISK PROFILE");
        if (profile.MaximumQuoteNotional is not > 0 ||
            profile.MaximumBaseQuantity is not > 0)
            return Needs("HARD LIMITS ARE NOT CONFIGURED");

        if (intent.SizingMode == OrderSizingMode.QuoteNotional)
            return intent.SizingValue <= profile.MaximumQuoteNotional
                ? Allow(intent.SizingValue)
                : Block("QUOTE NOTIONAL CAP EXCEEDED");

        if (intent.SizingValue > profile.MaximumBaseQuantity)
            return Block("BASE QUANTITY CAP EXCEEDED");
        if (profile.ReferencePriceMaxAge <= TimeSpan.Zero)
            return Needs("REFERENCE PRICE AGE LIMIT IS NOT CONFIGURED");
        if (referencePrice is null || referencePrice.Price <= 0 ||
            referencePrice.ObservedAt == default ||
            now - referencePrice.ObservedAt > profile.ReferencePriceMaxAge ||
            referencePrice.ObservedAt > now)
            return Needs("FRESH REFERENCE PRICE REQUIRED");
        decimal estimated;
        try { estimated = intent.SizingValue * referencePrice.Price; }
        catch (OverflowException) { return Block("ESTIMATED NOTIONAL OVERFLOW"); }
        return estimated <= profile.MaximumQuoteNotional
            ? Allow(estimated)
            : Block("ESTIMATED NOTIONAL CAP EXCEEDED");
    }

    private static RiskDecision Allow(decimal estimated) =>
        new(RiskDecisionStatus.Allowed, "SIMULATION RISK CHECK PASSED",
            RiskProfileMode.Simulation, estimated);
    private static RiskDecision Block(string reason) =>
        new(RiskDecisionStatus.Blocked, reason);
    private static RiskDecision Needs(string reason) =>
        new(RiskDecisionStatus.NeedsConfiguration, reason);
}
