using Trdng.Core.MarketData;

namespace Trdng.Gate.MarketData;

public sealed record GateOrderBookMessage(
    bool IsSnapshot,
    long FirstUpdateId,
    OrderBookUpdate Update);
