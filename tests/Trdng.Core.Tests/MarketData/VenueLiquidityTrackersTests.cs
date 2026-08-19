using Trdng.Core.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class VenueLiquidityTrackersTests
{
    [Fact]
    public void ResetAllClearsMexcBehaviorAcrossSelection()
    {
        var trackers = new VenueLiquidityTrackers();
        var at = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var first = Book(10);
        trackers.Mexc.Observe(first, at);
        var building = trackers.Mexc.Observe(Book(20), at.AddMilliseconds(10));
        Assert.Equal(LiquidityBehavior.Building, building[(LiquiditySide.Bid, 1m)].Behavior);

        trackers.ResetAll();

        var afterSwitch = trackers.Mexc.Observe(Book(20), at.AddMilliseconds(20));
        Assert.Equal(LiquidityBehavior.Normal, afterSwitch[(LiquiditySide.Bid, 1m)].Behavior);
    }

    private static OrderBookSnapshot Book(decimal quantity) =>
        new("APTUSDT", 1, 1, [new(1m, quantity)], [new(2m, quantity)]);
}
