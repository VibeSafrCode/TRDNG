namespace Trdng.Core.MarketData;

public sealed record OrderBookSnapshot(
    string Symbol,
    long UpdateId,
    long CrossSequence,
    IReadOnlyList<OrderBookLevel> Bids,
    IReadOnlyList<OrderBookLevel> Asks)
{
    public decimal? BestBid => Bids.Count == 0 ? null : Bids[0].Price;

    public decimal? BestAsk => Asks.Count == 0 ? null : Asks[0].Price;

    public decimal? Spread => BestBid is { } bid && BestAsk is { } ask
        ? ask - bid
        : null;
}
