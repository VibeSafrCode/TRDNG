namespace Trdng.Mexc.MarketData;

public sealed record MexcInstrumentMetadata(
    string Symbol,
    string BaseAsset,
    string QuoteAsset,
    decimal TickSize,
    string Status,
    bool IsSpotTradingAllowed,
    IReadOnlyList<string> OrderTypes,
    bool? QuoteOrderQtyMarketAllowed,
    decimal? MinimumBaseQuantity,
    decimal? MinimumMarketQuoteAmount,
    decimal? MaximumMarketQuoteAmount,
    int? TradeSideType);
