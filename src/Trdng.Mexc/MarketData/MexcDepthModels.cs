using Trdng.Core.MarketData;

namespace Trdng.Mexc.MarketData;

public sealed record MexcDepthDelta(
    string Symbol,
    long FromVersion,
    long ToVersion,
    long SendTime,
    IReadOnlyList<OrderBookLevel> Bids,
    IReadOnlyList<OrderBookLevel> Asks);

public enum MexcOrderBookApplyResult
{
    Buffered,
    SnapshotApplied,
    DeltaApplied,
    IgnoredStale,
    ResyncRequired
}

public enum MexcOrderBookSessionState
{
    WaitingForSnapshot,
    Live,
    ResyncRequired
}
