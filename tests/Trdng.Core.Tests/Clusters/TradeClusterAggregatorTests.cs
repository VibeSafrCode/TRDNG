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

    private static PublicTrade Trade(
        string id,
        DateTimeOffset time,
        AggressorSide side,
        decimal price,
        decimal quantity) =>
        new(id, "BTCUSDT", time, side, price, quantity, 1);
}
