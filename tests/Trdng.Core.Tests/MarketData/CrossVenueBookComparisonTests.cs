using Trdng.Core.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class CrossVenueBookComparisonTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ComputesMidPriceDivergenceWithoutMergingBooks()
    {
        var bybit = Snapshot(5.800m, 5.802m);
        var gate = Snapshot(5.806m, 5.808m);

        var result = CrossVenueBookComparison.Evaluate(
            Observation("BYBIT", bybit),
            Observation("GATE", gate),
            Now,
            MarketDataFreshnessOptions.ScalpingDefault);

        Assert.Equal(CrossVenueComparisonStatus.Ready, result.Status);
        Assert.Equal(5.801m, result.FirstMidPrice);
        Assert.Equal(5.807m, result.SecondMidPrice);
        Assert.Equal("GATE", result.HigherVenue);
        Assert.Equal(10.3376981392m, result.DivergenceBasisPoints!.Value, 10);
        Assert.NotSame(bybit.Bids, gate.Bids);
    }

    [Fact]
    public void MarksComparisonStaleWhenEitherBookStopsUpdating()
    {
        var snapshot = Snapshot(5.800m, 5.802m);
        var result = CrossVenueBookComparison.Evaluate(
            Observation("BYBIT", snapshot, Now - TimeSpan.FromSeconds(3)),
            Observation("GATE", snapshot),
            Now,
            MarketDataFreshnessOptions.ScalpingDefault);

        Assert.Equal(CrossVenueComparisonStatus.Stale, result.Status);
        Assert.Null(result.DivergenceBasisPoints);
    }

    [Theory]
    [InlineData(MarketDataConnectionState.Disconnected)]
    [InlineData(MarketDataConnectionState.Reconnecting)]
    [InlineData(MarketDataConnectionState.WaitingForSnapshot)]
    public void DoesNotCompareDisconnectedOrUnsynchronizedVenue(
        MarketDataConnectionState state)
    {
        var snapshot = Snapshot(5.800m, 5.802m);
        var result = CrossVenueBookComparison.Evaluate(
            new VenueBookObservation("BYBIT", state, snapshot, Now),
            Observation("GATE", snapshot),
            Now,
            MarketDataFreshnessOptions.ScalpingDefault);

        Assert.Equal(CrossVenueComparisonStatus.NotLive, result.Status);
        Assert.Null(result.DivergenceBasisPoints);
    }

    [Fact]
    public void WarnsAfterConfiguredThresholdButKeepsComparisonVisible()
    {
        var snapshot = Snapshot(5.800m, 5.802m);
        var result = CrossVenueBookComparison.Evaluate(
            Observation(
                "BYBIT",
                snapshot,
                Now - TimeSpan.FromMilliseconds(501)),
            Observation("GATE", snapshot),
            Now,
            MarketDataFreshnessOptions.ScalpingDefault);

        Assert.Equal(CrossVenueComparisonStatus.Warning, result.Status);
        Assert.Equal(0m, result.DivergenceBasisPoints);
    }

    [Fact]
    public void FreshnessThresholdsMustBeOrdered()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MarketDataFreshnessOptions(
                TimeSpan.FromSeconds(2),
                TimeSpan.FromMilliseconds(500)));
    }

    private static VenueBookObservation Observation(
        string venue,
        OrderBookSnapshot snapshot,
        DateTimeOffset? receivedAt = null) =>
        new(
            venue,
            MarketDataConnectionState.Live,
            snapshot,
            receivedAt ?? Now);

    private static OrderBookSnapshot Snapshot(decimal bid, decimal ask) =>
        new(
            "APTUSDT",
            1,
            1,
            [new OrderBookLevel(bid, 10)],
            [new OrderBookLevel(ask, 12)]);
}
