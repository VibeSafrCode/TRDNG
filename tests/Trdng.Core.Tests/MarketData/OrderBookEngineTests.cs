using Trdng.Core.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class OrderBookEngineTests
{
    [Fact]
    public void SnapshotSortsBidsDescendingAndAsksAscending()
    {
        var engine = new OrderBookEngine();

        engine.ApplySnapshot(Update(
            updateId: 10,
            bids: [Level(100, 2), Level(101, 1)],
            asks: [Level(103, 4), Level(102, 3)]));

        var book = engine.Capture(10);

        Assert.Equal([101m, 100m], book.Bids.Select(static level => level.Price));
        Assert.Equal([102m, 103m], book.Asks.Select(static level => level.Price));
        Assert.Equal(1m, book.Spread);
    }

    [Fact]
    public void DeltaUpdatesAddsAndDeletesLevels()
    {
        var engine = new OrderBookEngine();
        engine.ApplySnapshot(Update(
            updateId: 10,
            bids: [Level(100, 2), Level(99, 5)],
            asks: [Level(101, 3), Level(102, 4)]));

        var applied = engine.TryApplyDelta(Update(
            updateId: 11,
            bids: [Level(100, 7), Level(99, 0), Level(98, 1)],
            asks: [Level(101, 0), Level(103, 6)]));

        var book = engine.Capture(10);
        Assert.True(applied);
        Assert.Equal([Level(100, 7), Level(98, 1)], book.Bids);
        Assert.Equal([Level(102, 4), Level(103, 6)], book.Asks);
    }

    [Fact]
    public void DeltaBeforeSnapshotIsRejected()
    {
        var engine = new OrderBookEngine();

        Assert.False(engine.TryApplyDelta(Update(updateId: 1)));
    }

    [Fact]
    public void StaleDeltaIsIgnored()
    {
        var engine = new OrderBookEngine();
        engine.ApplySnapshot(Update(updateId: 10, bids: [Level(100, 1)]));

        Assert.False(engine.TryApplyDelta(Update(updateId: 10, bids: [Level(100, 9)])));
        Assert.Equal(1m, engine.Capture(1).Bids[0].Quantity);
    }

    [Fact]
    public void DeltaWithOlderCrossSequenceIsIgnored()
    {
        var engine = new OrderBookEngine();
        engine.ApplySnapshot(new OrderBookUpdate(
            "BTCUSDT", 10, 100, [Level(100, 1)], []));

        var applied = engine.TryApplyDelta(new OrderBookUpdate(
            "BTCUSDT", 11, 99, [Level(100, 9)], []));

        Assert.False(applied);
        Assert.Equal(1m, engine.Capture(1).Bids[0].Quantity);
    }

    private static OrderBookUpdate Update(
        long updateId,
        IReadOnlyList<OrderBookLevel>? bids = null,
        IReadOnlyList<OrderBookLevel>? asks = null) =>
        new(
            "BTCUSDT",
            updateId,
            updateId * 10,
            bids ?? [],
            asks ?? []);

    private static OrderBookLevel Level(decimal price, decimal quantity) =>
        new(price, quantity);
}
