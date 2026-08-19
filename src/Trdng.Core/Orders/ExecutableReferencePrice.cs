using Trdng.Core.MarketData;

namespace Trdng.Core.Orders;

public static class ExecutableReferencePrice
{
    public static ReferencePrice? Select(
        OrderBookSnapshot? snapshot,
        OrderSide side,
        DateTimeOffset observedAt)
    {
        if (snapshot is null || observedAt == default) return null;
        var price = side == OrderSide.Buy ? snapshot.BestAsk : snapshot.BestBid;
        return price is > 0 ? new(price.Value, observedAt) : null;
    }
}
