using Trdng.Bybit.MarketData;
using Trdng.Core.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class BybitOrderBookSessionTests
{
    [Fact]
    public void DeltaIsRejectedUntilSnapshotArrives()
    {
        var session = new BybitOrderBookSession(new OrderBookEngine());

        var result = session.Apply(Message(BybitOrderBookMessageType.Delta, updateId: 10));

        Assert.Equal(OrderBookApplyResult.WaitingForSnapshot, result);
        Assert.Equal(OrderBookSessionState.WaitingForSnapshot, session.State);
    }

    [Fact]
    public void ReconnectClearsBookAndRequiresFreshSnapshot()
    {
        var session = new BybitOrderBookSession(new OrderBookEngine());
        session.Apply(Message(
            BybitOrderBookMessageType.Snapshot,
            updateId: 10,
            bids: [new OrderBookLevel(100, 1)]));

        session.OnDisconnected();

        Assert.Equal(OrderBookSessionState.WaitingForSnapshot, session.State);
        Assert.False(session.Engine.HasSnapshot);
        Assert.Equal(
            OrderBookApplyResult.WaitingForSnapshot,
            session.Apply(Message(BybitOrderBookMessageType.Delta, updateId: 11)));
    }

    [Fact]
    public void FreshSnapshotAfterReconnectRestoresLiveState()
    {
        var session = new BybitOrderBookSession(new OrderBookEngine());
        session.Apply(Message(BybitOrderBookMessageType.Snapshot, updateId: 10));
        session.OnDisconnected();

        var result = session.Apply(Message(
            BybitOrderBookMessageType.Snapshot,
            updateId: 42,
            asks: [new OrderBookLevel(101, 2)]));

        Assert.Equal(OrderBookApplyResult.SnapshotApplied, result);
        Assert.Equal(OrderBookSessionState.Live, session.State);
        Assert.Equal(42, session.Engine.LastUpdateId);
    }

    [Fact]
    public void UpdateIdOneForcesFullResetAfterBybitServiceRestart()
    {
        var session = new BybitOrderBookSession(new OrderBookEngine());
        session.Apply(Message(
            BybitOrderBookMessageType.Snapshot,
            updateId: 100,
            bids: [new OrderBookLevel(100, 1)]));

        var result = session.Apply(Message(
            BybitOrderBookMessageType.Delta,
            updateId: 1,
            asks: [new OrderBookLevel(102, 3)]));

        var book = session.Engine.Capture(10);
        Assert.Equal(OrderBookApplyResult.SnapshotApplied, result);
        Assert.Empty(book.Bids);
        Assert.Equal([new OrderBookLevel(102, 3)], book.Asks);
        Assert.Equal(1, book.UpdateId);
    }

    [Fact]
    public void NonContiguousUpdateIdsAreAcceptedBecauseBybitDoesNotDefineThemAsGapFree()
    {
        var session = new BybitOrderBookSession(new OrderBookEngine());
        session.Apply(Message(BybitOrderBookMessageType.Snapshot, updateId: 10));

        var result = session.Apply(Message(
            BybitOrderBookMessageType.Delta,
            updateId: 12,
            bids: [new OrderBookLevel(100, 1)]));

        Assert.Equal(OrderBookApplyResult.DeltaApplied, result);
        Assert.Equal(12, session.Engine.LastUpdateId);
    }

    private static BybitOrderBookMessage Message(
        BybitOrderBookMessageType type,
        long updateId,
        IReadOnlyList<OrderBookLevel>? bids = null,
        IReadOnlyList<OrderBookLevel>? asks = null) =>
        new(
            type,
            new OrderBookUpdate(
                "BTCUSDT",
                updateId,
                updateId * 10,
                bids ?? [],
                asks ?? []));
}
