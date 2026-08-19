using Trdng.Core.MarketData;

namespace Trdng.Bybit.MarketData;

public enum OrderBookSessionState
{
    WaitingForSnapshot,
    Live
}

public enum OrderBookApplyResult
{
    SnapshotApplied,
    DeltaApplied,
    IgnoredStaleDelta,
    WaitingForSnapshot
}

public sealed class BybitOrderBookSession
{
    public BybitOrderBookSession(OrderBookEngine engine)
    {
        Engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public OrderBookEngine Engine { get; }

    public OrderBookSessionState State { get; private set; } =
        OrderBookSessionState.WaitingForSnapshot;

    public OrderBookApplyResult Apply(BybitOrderBookMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Bybit documents u=1 as snapshot data emitted after a service restart.
        // Treat it as a full reset even if a malformed/changed envelope labels it delta.
        if (message.Type == BybitOrderBookMessageType.Snapshot ||
            message.Update.UpdateId == 1)
        {
            Engine.ApplySnapshot(message.Update);
            State = OrderBookSessionState.Live;
            return OrderBookApplyResult.SnapshotApplied;
        }

        if (State != OrderBookSessionState.Live)
        {
            return OrderBookApplyResult.WaitingForSnapshot;
        }

        return Engine.TryApplyDelta(message.Update)
            ? OrderBookApplyResult.DeltaApplied
            : OrderBookApplyResult.IgnoredStaleDelta;
    }

    public void OnDisconnected()
    {
        Engine.Reset();
        State = OrderBookSessionState.WaitingForSnapshot;
    }
}
