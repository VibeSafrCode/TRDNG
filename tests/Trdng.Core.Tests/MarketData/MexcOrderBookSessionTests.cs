using Trdng.Core.MarketData;
using Trdng.Mexc.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class MexcOrderBookSessionTests
{
    [Fact]
    public void BuffersDeltaAndBridgesSnapshotVersion()
    {
        var session = NewSession();
        Assert.Equal(MexcOrderBookApplyResult.Buffered, session.BufferOrApply(Delta(10, 12, [new(100, 2)])));
        var result = session.ApplySnapshot(Snapshot(11, [new(100, 1)]));
        Assert.Equal(MexcOrderBookApplyResult.SnapshotApplied, result);
        Assert.Equal(MexcOrderBookSessionState.Live, session.State);
        Assert.Equal(12, session.LastVersion);
        Assert.Equal(2m, session.Engine.Capture(5).Bids[0].Quantity);
    }

    [Fact]
    public void GapAfterLiveRequiresResnapshotAndClearsBook()
    {
        var session = LiveSession();
        Assert.Equal(MexcOrderBookApplyResult.ResyncRequired, session.BufferOrApply(Delta(14, 14)));
        Assert.Equal(MexcOrderBookSessionState.ResyncRequired, session.State);
        Assert.False(session.Engine.HasSnapshot);
    }

    [Fact]
    public void StaleDeltaIsIgnored()
    {
        var session = LiveSession();
        Assert.Equal(MexcOrderBookApplyResult.IgnoredStale, session.BufferOrApply(Delta(11, 12)));
    }

    [Fact]
    public void ReconnectRequiresNewSnapshot()
    {
        var session = LiveSession();
        session.OnDisconnected();
        Assert.Equal(MexcOrderBookSessionState.WaitingForSnapshot, session.State);
        Assert.False(session.Engine.HasSnapshot);
        Assert.Equal(MexcOrderBookApplyResult.Buffered, session.BufferOrApply(Delta(20, 20)));
    }

    [Fact]
    public void MissingSnapshotBridgeRequiresResync()
    {
        var session = NewSession();
        session.BufferOrApply(Delta(12, 13));
        Assert.Equal(MexcOrderBookApplyResult.ResyncRequired, session.ApplySnapshot(Snapshot(10)));
        Assert.StartsWith("initial-gap:", session.LastDecision);
    }

    [Fact]
    public void SnapshotNewerThanEarlyDeltasWaitsForBridge()
    {
        var session = NewSession();
        session.BufferOrApply(Delta(10, 11));

        Assert.Equal(MexcOrderBookApplyResult.Buffered, session.ApplySnapshot(Snapshot(13)));
        Assert.Equal(MexcOrderBookApplyResult.SnapshotApplied, session.BufferOrApply(Delta(12, 14)));
        Assert.Equal(14, session.LastVersion);
        Assert.Equal(MexcOrderBookSessionState.Live, session.State);
        Assert.StartsWith("bridge-success:", session.LastDecision);
    }

    [Fact]
    public void DeltaStartingAtSnapshotPlusOneIsAValidInitialBridge()
    {
        var session = NewSession();
        session.BufferOrApply(Delta(11, 13, [new(100, 2)]));

        Assert.Equal(MexcOrderBookApplyResult.SnapshotApplied, session.ApplySnapshot(Snapshot(10)));
        Assert.Equal(13, session.LastVersion);
        Assert.Equal(MexcOrderBookSessionState.Live, session.State);
    }

    [Fact]
    public void DeltaStartingAtSnapshotPlusTwoIsATrueInitialGap()
    {
        var session = NewSession();
        session.BufferOrApply(Delta(12, 13));

        Assert.Equal(MexcOrderBookApplyResult.ResyncRequired, session.ApplySnapshot(Snapshot(10)));
        Assert.StartsWith("initial-gap:", session.LastDecision);
    }

    private static MexcOrderBookSession LiveSession()
    {
        var session = NewSession();
        session.BufferOrApply(Delta(10, 12));
        session.ApplySnapshot(Snapshot(11));
        return session;
    }
    [Fact]
    public void PreSnapshotBufferIsBounded()
    {
        var session = new MexcOrderBookSession(new OrderBookEngine(), "APTUSDT", 2);
        session.BufferOrApply(Delta(1, 1));
        session.BufferOrApply(Delta(2, 2));
        Assert.Equal(MexcOrderBookApplyResult.ResyncRequired, session.BufferOrApply(Delta(3, 3)));
    }

    [Fact]
    public void RejectsAnotherSymbolBeforeBuffering()
    {
        var session = NewSession();
        var foreign = new MexcDepthDelta("BTCUSDT", 1, 1, 1, [], []);
        Assert.Throws<InvalidDataException>(() => session.BufferOrApply(foreign));
    }

    private static MexcOrderBookSession NewSession() => new(new OrderBookEngine(), "APTUSDT");
    private static OrderBookUpdate Snapshot(long version, IReadOnlyList<OrderBookLevel>? bids = null) =>
        new("APTUSDT", version, version, bids ?? [], []);
    private static MexcDepthDelta Delta(long from, long to, IReadOnlyList<OrderBookLevel>? bids = null) =>
        new("APTUSDT", from, to, 1, bids ?? [], []);
}
