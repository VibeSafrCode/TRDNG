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

    [Fact]
    public void SnapshotAcceptsExactCapacityBoundary()
    {
        var engine = Engine(maximumLevelsPerSide: 2, maximumLevelsPerUpdate: 4);

        engine.ApplySnapshot(Update(
            1,
            bids: [Level(100, 1), Level(99, 1)],
            asks: [Level(101, 1), Level(102, 1)]));

        var snapshot = engine.Capture(2);
        Assert.Equal(2, snapshot.Bids.Count);
        Assert.Equal(2, snapshot.Asks.Count);
    }

    [Fact]
    public void OversizedSnapshotDoesNotReplaceExistingBook()
    {
        var engine = Engine(maximumLevelsPerSide: 2, maximumLevelsPerUpdate: 4);
        engine.ApplySnapshot(Update(
            1,
            bids: [Level(100, 1)],
            asks: [Level(101, 1)]));

        var exception = Assert.Throws<OrderBookPolicyViolationException>(() =>
            engine.ApplySnapshot(Update(
                2,
                bids: [Level(100, 9), Level(99, 1), Level(98, 1)],
                asks: [Level(101, 9), Level(102, 1)])));

        Assert.Equal(OrderBookPolicyViolationCode.UpdateTooLarge, exception.Code);
        var unchanged = engine.Capture(2);
        Assert.Equal(1, unchanged.UpdateId);
        Assert.Equal([Level(100, 1)], unchanged.Bids);
        Assert.Equal([Level(101, 1)], unchanged.Asks);
        Assert.Equal(1, engine.RejectedUpdateCount);
    }

    [Fact]
    public void DeltaCapacityViolationHasNoPartialMutation()
    {
        var engine = Engine(maximumLevelsPerSide: 2, maximumLevelsPerUpdate: 4);
        engine.ApplySnapshot(Update(
            1,
            bids: [Level(100, 1), Level(99, 1)],
            asks: [Level(101, 1)]));

        var exception = Assert.Throws<OrderBookPolicyViolationException>(() =>
            engine.TryApplyDelta(Update(
                2,
                bids: [Level(100, 7), Level(98, 1)])));

        Assert.Equal(OrderBookPolicyViolationCode.SideCapacityExceeded, exception.Code);
        var unchanged = engine.Capture(2);
        Assert.Equal(1, unchanged.UpdateId);
        Assert.Equal([Level(100, 1), Level(99, 1)], unchanged.Bids);
    }

    [Fact]
    public void DuplicateAndCrossedUpdatesAreRejectedWithoutMutation()
    {
        var engine = Engine(maximumLevelsPerSide: 4, maximumLevelsPerUpdate: 8);
        engine.ApplySnapshot(Update(
            1,
            bids: [Level(100, 1)],
            asks: [Level(101, 1)]));

        var duplicate = Assert.Throws<OrderBookPolicyViolationException>(() =>
            engine.TryApplyDelta(Update(
                2,
                bids: [Level(99, 1), Level(99, 2)])));
        Assert.Equal(OrderBookPolicyViolationCode.DuplicatePrice, duplicate.Code);

        var crossed = Assert.Throws<OrderBookPolicyViolationException>(() =>
            engine.TryApplyDelta(Update(2, bids: [Level(101, 3)])));
        Assert.Equal(OrderBookPolicyViolationCode.CrossedBook, crossed.Code);

        var unchanged = engine.Capture(4);
        Assert.Equal(1, unchanged.UpdateId);
        Assert.Equal([Level(100, 1)], unchanged.Bids);
        Assert.Equal([Level(101, 1)], unchanged.Asks);
    }

    [Fact]
    public void DeltaProjectionHandlesDeletionOfBothPreviousBestLevels()
    {
        var engine = Engine(maximumLevelsPerSide: 4, maximumLevelsPerUpdate: 8);
        engine.ApplySnapshot(Update(
            1,
            bids: [Level(100, 1), Level(99, 1)],
            asks: [Level(101, 1), Level(102, 1)]));

        Assert.True(engine.TryApplyDelta(Update(
            2,
            bids: [Level(100, 0), Level(100.5m, 2)],
            asks: [Level(101, 0), Level(101.5m, 2)])));

        var result = engine.Capture(4);
        Assert.Equal(100.5m, result.BestBid);
        Assert.Equal(101.5m, result.BestAsk);
        Assert.Equal(1m, result.Spread);
    }

    [Fact]
    public void MaximumPriceAndCaptureDepthAreFailClosed()
    {
        var engine = new OrderBookEngine(new OrderBookCapacityPolicy(2, 4, 1_000m));

        var exception = Assert.Throws<OrderBookPolicyViolationException>(() =>
            engine.ApplySnapshot(Update(1, bids: [Level(1_001, 1)])));
        Assert.Equal(OrderBookPolicyViolationCode.PriceLimitExceeded, exception.Code);
        Assert.False(engine.HasSnapshot);

        engine.ApplySnapshot(Update(1, bids: [Level(100, 1)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Capture(3));
    }

    [Fact]
    public void RandomizedDeltasNeverExceedConfiguredCapacity()
    {
        const int maximumLevels = 32;
        var engine = Engine(maximumLevels, maximumLevelsPerUpdate: 8);
        engine.ApplySnapshot(Update(
            1,
            bids: [Level(90, 1)],
            asks: [Level(110, 1)]));
        var random = new Random(618);

        for (var iteration = 0; iteration < 5_000; iteration++)
        {
            var bid = random.Next(1, 100);
            var ask = random.Next(101, 201);
            var quantity = random.Next(0, 5);
            var update = Update(
                engine.LastUpdateId + 1,
                bids: [Level(bid, quantity)],
                asks: [Level(ask, quantity)]);

            try
            {
                engine.TryApplyDelta(update);
            }
            catch (OrderBookPolicyViolationException exception)
            {
                Assert.Equal(
                    OrderBookPolicyViolationCode.SideCapacityExceeded,
                    exception.Code);
            }

            var snapshot = engine.Capture(maximumLevels);
            Assert.InRange(snapshot.Bids.Count, 0, maximumLevels);
            Assert.InRange(snapshot.Asks.Count, 0, maximumLevels);
            if (snapshot.Bids.Count > 0 && snapshot.Asks.Count > 0)
            {
                Assert.True(snapshot.Bids[0].Price < snapshot.Asks[0].Price);
            }
        }
    }

    private static OrderBookEngine Engine(
        int maximumLevelsPerSide,
        int maximumLevelsPerUpdate) =>
        new(new OrderBookCapacityPolicy(
            maximumLevelsPerSide,
            maximumLevelsPerUpdate));

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
