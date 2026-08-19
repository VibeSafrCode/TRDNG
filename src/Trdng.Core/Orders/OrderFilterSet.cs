namespace Trdng.Core.Orders;

public sealed record OrderFilterSet(
    decimal? MinimumBaseQuantity = null,
    decimal? MaximumBaseQuantity = null,
    decimal? BaseQuantityStep = null,
    decimal? MinimumQuoteNotional = null,
    decimal? MaximumQuoteNotional = null,
    decimal? QuoteNotionalStep = null);

public enum OrderValidationStatus { Valid, Invalid, NeedsMetadata, Blocked }

public sealed record OrderValidationResult(OrderValidationStatus Status, string Message)
{
    public bool IsValid => Status == OrderValidationStatus.Valid;
    public MarketOrderIntent? ValidatedIntent { get; internal init; }
}

public static class MarketOrderValidator
{
    public static OrderValidationResult Validate(
        MarketOrderIntent intent,
        OrderFilterSet? filters)
    {
        if (intent.Type != OrderType.Market || intent.Price is not null)
            return Invalid("MARKET intent must not contain a price.");
        if (!ClientOrderIdPolicy.IsValid(intent.ClientOrderId))
            return Invalid("Client order id is invalid.");
        if (intent.SizingValue <= 0)
            return Invalid("Sizing value must be positive.");
        if (filters is null)
            return new(OrderValidationStatus.NeedsMetadata, "OFFICIAL FILTERS REQUIRED");
        var coherence = ValidateCoherence(filters);
        if (coherence is not null) return coherence;

        var result = intent.SizingMode switch
        {
            OrderSizingMode.BaseQuantity => ValidateBase(intent.SizingValue, filters),
            OrderSizingMode.QuoteNotional => ValidateQuote(intent.SizingValue, filters),
            _ => Invalid("Unknown sizing mode.")
        };
        return result.Status == OrderValidationStatus.Valid
            ? result with { ValidatedIntent = intent }
            : result;
    }

    private static OrderValidationResult ValidateBase(decimal value, OrderFilterSet filters)
    {
        if (filters.MinimumBaseQuantity is null || filters.MaximumBaseQuantity is null ||
            filters.BaseQuantityStep is null)
            return new(OrderValidationStatus.NeedsMetadata, "BASE QUANTITY FILTERS REQUIRED");
        if (value < filters.MinimumBaseQuantity || value > filters.MaximumBaseQuantity)
            return Invalid("Base quantity is outside official limits.");
        if (filters.BaseQuantityStep <= 0 || value % filters.BaseQuantityStep != 0)
            return Invalid("Base quantity does not match official step size.");
        return new(OrderValidationStatus.Valid, "VALIDATED");
    }

    private static OrderValidationResult? ValidateCoherence(OrderFilterSet filters)
    {
        if (filters.MinimumBaseQuantity is <= 0 ||
            filters.MaximumBaseQuantity is <= 0 ||
            filters.BaseQuantityStep is <= 0 ||
            filters.MinimumQuoteNotional is <= 0 ||
            filters.MaximumQuoteNotional is <= 0 ||
            filters.QuoteNotionalStep is <= 0 ||
            filters.MinimumBaseQuantity is { } baseMin &&
            filters.MaximumBaseQuantity is { } baseMax && baseMin > baseMax ||
            filters.MinimumQuoteNotional is { } quoteMin &&
            filters.MaximumQuoteNotional is { } quoteMax && quoteMin > quoteMax)
            return new(OrderValidationStatus.NeedsMetadata,
                "OFFICIAL FILTER METADATA IS INVALID");
        return null;
    }

    private static OrderValidationResult ValidateQuote(decimal value, OrderFilterSet filters)
    {
        if (filters.MinimumQuoteNotional is null || filters.MaximumQuoteNotional is null)
            return new(OrderValidationStatus.NeedsMetadata, "QUOTE NOTIONAL FILTERS REQUIRED");
        if (value < filters.MinimumQuoteNotional || value > filters.MaximumQuoteNotional)
            return Invalid("Quote notional is outside official limits.");
        if (filters.QuoteNotionalStep is { } step &&
            (step <= 0 || value % step != 0))
            return Invalid("Quote notional does not match official step size.");
        return new(OrderValidationStatus.Valid, "VALIDATED");
    }

    private static OrderValidationResult Invalid(string message) =>
        new(OrderValidationStatus.Invalid, message);
}
