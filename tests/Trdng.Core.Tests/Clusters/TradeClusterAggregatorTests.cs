using Trdng.Core.Clusters;
using Trdng.Core.MarketData;

namespace Trdng.Core.Tests.Clusters;

public sealed class TradeClusterAggregatorTests
{
    [Fact]
    public void AggregatesTakerSellIntoBidAndTakerBuyIntoAsk()
    {
        var aggregator = new TradeClusterAggregator(TimeSpan.FromSeconds(15), 0.5m);
        var time = DateTimeOffset.Parse("2026-07-29T12:00:01Z");

        aggregator.Ingest(Trade("sell", time, AggressorSide.Sell, 100.7m, 2m));
        aggregator.Ingest(Trade("buy", time, AggressorSide.Buy, 100.9m, 3m));

        var cluster = Assert.IsType<TradeCluster>(aggregator.CaptureCurrent());
        var level = Assert.Single(cluster.Levels);
        Assert.Equal(100.5m, level.Price);
        Assert.Equal(2m, level.BidVolume);
        Assert.Equal(3m, level.AskVolume);
        Assert.Equal(5m, level.TotalVolume);
        Assert.Equal(1m, level.Delta);
    }

    [Fact]
    public void NewIntervalCompletesPreviousCluster()
    {
        var aggregator = new TradeClusterAggregator(TimeSpan.FromSeconds(15), 0.5m);

        aggregator.Ingest(Trade(
            "first",
            DateTimeOffset.Parse("2026-07-29T12:00:14Z"),
            AggressorSide.Buy,
            100m,
            1m));
        aggregator.Ingest(Trade(
            "second",
            DateTimeOffset.Parse("2026-07-29T12:00:15Z"),
            AggressorSide.Sell,
            101m,
            2m));

        var completed = Assert.Single(aggregator.Completed);
        Assert.Equal(DateTimeOffset.Parse("2026-07-29T12:00:00Z"), completed.StartsAt);
        Assert.Equal(1m, completed.TotalVolume);
        Assert.Equal(DateTimeOffset.Parse("2026-07-29T12:00:15Z"),
            aggregator.CaptureCurrent()!.StartsAt);
    }

    [Fact]
    public void CompletedHistoryIsBounded()
    {
        var aggregator = new TradeClusterAggregator(
            TimeSpan.FromSeconds(1),
            0.5m,
            maxCompletedClusters: 2);
        var start = DateTimeOffset.Parse("2026-07-29T12:00:00Z");

        for (var index = 0; index < 4; index++)
        {
            aggregator.Ingest(Trade(
                index.ToString(),
                start.AddSeconds(index),
                AggressorSide.Buy,
                100m,
                1m));
        }

        Assert.Equal(2, aggregator.Completed.Count);
        Assert.DoesNotContain(
            aggregator.Completed,
            cluster => cluster.StartsAt == start);
    }

    [Fact]
    public void UniquePriceCapacityDropsOverflowedIntervalWithoutPartialCluster()
    {
        var aggregator = new TradeClusterAggregator(
            TimeSpan.FromSeconds(15),
            0.5m,
            maximumPriceLevelsPerCluster: 2,
            maximumTradesPerCluster: 10);
        var start = DateTimeOffset.Parse("2026-07-29T12:00:00Z");

        Assert.Equal(TradeClusterIngestResult.Accepted,
            aggregator.Ingest(Trade("1", start, AggressorSide.Buy, 100m, 1m)));
        Assert.Equal(TradeClusterIngestResult.Accepted,
            aggregator.Ingest(Trade("2", start, AggressorSide.Sell, 101m, 1m)));
        Assert.Equal(TradeClusterIngestResult.CapacityExceeded,
            aggregator.Ingest(Trade("3", start, AggressorSide.Buy, 102m, 1m)));
        Assert.Equal(TradeClusterIngestResult.IgnoredOverflowedInterval,
            aggregator.Ingest(Trade("4", start, AggressorSide.Buy, 100m, 1m)));

        Assert.Null(aggregator.CaptureCurrent());
        Assert.True(aggregator.Metrics.IsCurrentIntervalOverflowed);
        Assert.Equal(2, aggregator.Metrics.AcceptedTradeCount);
        Assert.Equal(2, aggregator.Metrics.RejectedTradeCount);
        Assert.Equal(1, aggregator.Metrics.OverflowedClusterCount);

        Assert.Equal(TradeClusterIngestResult.Accepted,
            aggregator.Ingest(Trade("5", start.AddSeconds(15), AggressorSide.Buy, 103m, 2m)));
        Assert.Empty(aggregator.Completed);
        Assert.NotNull(aggregator.CaptureCurrent());
        Assert.False(aggregator.Metrics.IsCurrentIntervalOverflowed);
    }

    [Fact]
    public void TradeCapacityIsBoundedEvenAtOnePrice()
    {
        var aggregator = new TradeClusterAggregator(
            TimeSpan.FromSeconds(15),
            0.5m,
            maximumPriceLevelsPerCluster: 10,
            maximumTradesPerCluster: 2);
        var start = DateTimeOffset.Parse("2026-07-29T12:00:00Z");

        aggregator.Ingest(Trade("1", start, AggressorSide.Buy, 100m, 1m));
        aggregator.Ingest(Trade("2", start, AggressorSide.Buy, 100m, 1m));
        var result = aggregator.Ingest(
            Trade("3", start, AggressorSide.Buy, 100m, 1m));

        Assert.Equal(TradeClusterIngestResult.CapacityExceeded, result);
        Assert.Null(aggregator.CaptureCurrent());
        Assert.Equal(2, aggregator.Metrics.CurrentTradeCount);
    }

    [Fact]
    public void RandomizedTradeStreamRemainsWithinBothCaps()
    {
        const int maxLevels = 16;
        const int maxTrades = 50;
        var aggregator = new TradeClusterAggregator(
            TimeSpan.FromSeconds(1),
            0.25m,
            maximumPriceLevelsPerCluster: maxLevels,
            maximumTradesPerCluster: maxTrades);
        var start = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
        var random = new Random(618);

        for (var index = 0; index < 5_000; index++)
        {
            var trade = Trade(
                index.ToString(),
                start.AddMilliseconds(index * 10L),
                index % 2 == 0 ? AggressorSide.Buy : AggressorSide.Sell,
                90m + (decimal)random.NextDouble() * 20m,
                1m);
            aggregator.Ingest(trade);

            Assert.InRange(aggregator.Metrics.CurrentPriceLevelCount, 0, maxLevels);
            Assert.InRange(aggregator.Metrics.CurrentTradeCount, 0, maxTrades);
            if (aggregator.Metrics.IsCurrentIntervalOverflowed)
            {
                Assert.Null(aggregator.CaptureCurrent());
            }
        }
    }

    private static PublicTrade Trade(
        string id,
        DateTimeOffset time,
        AggressorSide side,
        decimal price,
        decimal quantity) =>
        new(id, "BTCUSDT", time, side, price, quantity, 1);
}
