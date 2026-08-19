using Trdng.Core.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class LiquidityTrackerTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IncreasingQuantityIsBuilding()
    {
        var tracker = new LiquidityTracker();
        tracker.Observe(Book(bids: [Level(100, 10)]), Start);

        var states = tracker.Observe(
            Book(bids: [Level(100, 13)]),
            Start.AddMilliseconds(100));

        Assert.Equal(
            LiquidityBehavior.Building,
            states[(LiquiditySide.Bid, 100m)].Behavior);
    }

    [Fact]
    public void DecreaseWithoutExecutionsIsPulling()
    {
        var tracker = new LiquidityTracker();
        tracker.Observe(Book(asks: [Level(101, 10)]), Start);

        var states = tracker.Observe(
            Book(asks: [Level(101, 5)]),
            Start.AddMilliseconds(100));

        Assert.Equal(
            LiquidityBehavior.Pulling,
            states[(LiquiditySide.Ask, 101m)].Behavior);
    }

    [Fact]
    public void ExecutionsWhileQuantityRemainsAreAbsorbing()
    {
        var tracker = new LiquidityTracker();
        tracker.Observe(Book(asks: [Level(101, 10)]), Start);
        tracker.ObserveTrades([
            new PublicTrade(
                "trade-1",
                "BTCUSDT",
                Start.AddMilliseconds(50),
                AggressorSide.Buy,
                101,
                3,
                1)
        ]);

        var states = tracker.Observe(
            Book(asks: [Level(101, 9)]),
            Start.AddMilliseconds(100));

        Assert.Equal(
            LiquidityBehavior.Absorbing,
            states[(LiquiditySide.Ask, 101m)].Behavior);
    }

    [Fact]
    public void StableLevelBecomesHolding()
    {
        var tracker = new LiquidityTracker();
        tracker.Observe(Book(bids: [Level(100, 10)]), Start);

        var states = tracker.Observe(
            Book(bids: [Level(100, 10)]),
            Start.AddSeconds(3));

        Assert.Equal(
            LiquidityBehavior.Holding,
            states[(LiquiditySide.Bid, 100m)].Behavior);
    }

    private static OrderBookSnapshot Book(
        IReadOnlyList<OrderBookLevel>? bids = null,
        IReadOnlyList<OrderBookLevel>? asks = null) =>
        new("BTCUSDT", 1, 1, bids ?? [], asks ?? []);

    private static OrderBookLevel Level(decimal price, decimal quantity) =>
        new(price, quantity);
}
