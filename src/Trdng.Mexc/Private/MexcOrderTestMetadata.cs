using Trdng.Core.Orders;
using Trdng.Mexc.MarketData;

namespace Trdng.Mexc.Private;

public enum MexcOrderTestMetadataState { Available, NeedsMetadata, Blocked }

public sealed record MexcOrderTestMetadataResult(
    MexcOrderTestMetadataState State, OrderFilterSet? Filters, string Code);

public static class MexcOrderTestMetadataMapper
{
    public static MexcOrderTestMetadataResult Map(
        MexcInstrumentMetadata metadata, OrderSide side)
    {
        if (metadata.Status != "1" || !metadata.IsSpotTradingAllowed ||
            !metadata.OrderTypes.Contains("MARKET", StringComparer.Ordinal))
            return Blocked("MARKET_DISABLED");
        if (metadata.TradeSideType is not 1 &&
            !(side == OrderSide.Buy && metadata.TradeSideType == 2) &&
            !(side == OrderSide.Sell && metadata.TradeSideType == 3))
            return metadata.TradeSideType is null
                ? Needs("TRADE_SIDE_MISSING") : Blocked("SIDE_DISABLED");

        if (side == OrderSide.Buy)
        {
            if (metadata.QuoteOrderQtyMarketAllowed is not true)
                return metadata.QuoteOrderQtyMarketAllowed is false
                    ? Blocked("QUOTE_ORDER_QTY_DISABLED")
                    : Needs("QUOTE_ORDER_QTY_SUPPORT_MISSING");
            if (metadata.MinimumMarketQuoteAmount is not > 0 ||
                metadata.MaximumMarketQuoteAmount is not > 0 ||
                metadata.MinimumMarketQuoteAmount > metadata.MaximumMarketQuoteAmount)
                return Needs("MARKET_QUOTE_LIMITS_MISSING");
            return new(MexcOrderTestMetadataState.Available,
                new OrderFilterSet(
                    MinimumQuoteNotional: metadata.MinimumMarketQuoteAmount,
                    MaximumQuoteNotional: metadata.MaximumMarketQuoteAmount), "AVAILABLE");
        }

        // Official exchangeInfo documents baseSizePrecision as minimum quantity,
        // not as step size, and exposes no documented MARKET maximum base quantity.
        // OrderFilterSet requires all three, so SELL remains fail-closed.
        return metadata.MinimumBaseQuantity is > 0
            ? Needs("BASE_MAX_AND_STEP_MISSING") : Needs("BASE_LIMITS_MISSING");
    }

    private static MexcOrderTestMetadataResult Needs(string code) =>
        new(MexcOrderTestMetadataState.NeedsMetadata, null, code);
    private static MexcOrderTestMetadataResult Blocked(string code) =>
        new(MexcOrderTestMetadataState.Blocked, null, code);
}
