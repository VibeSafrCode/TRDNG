using Trdng.Core.MarketData;

namespace Trdng.Bybit.MarketData;

public enum BybitOrderBookMessageType
{
    Snapshot,
    Delta
}

public sealed record BybitOrderBookMessage(
    BybitOrderBookMessageType Type,
    OrderBookUpdate Update);
