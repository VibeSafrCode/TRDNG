namespace Trdng.Core.MarketData;

public sealed record OrderBookUpdate(
    string Symbol,
    long UpdateId,
    long CrossSequence,
    IReadOnlyList<OrderBookLevel> Bids,
    IReadOnlyList<OrderBookLevel> Asks);
