using Trdng.Core.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class SharedPriceScaleTests
{
    [Fact]
    public void BuildsOnePriceAxisForTwoIndependentBooks()
    {
        var bybit = Snapshot(
            [Level(5.825m), Level(5.824m)],
            [Level(5.826m), Level(5.827m)]);
        var gate = Snapshot(
            [Level(5.826m), Level(5.825m)],
            [Level(5.827m), Level(5.828m)]);

        var scale = SharedPriceScale.Build(bybit, gate, 3);

        Assert.NotNull(scale);
        Assert.Equal(0.001m, scale.TickSize);
        Assert.Equal([5.826m, 5.827m, 5.828m], scale.Asks);
        Assert.Equal([5.825m, 5.824m, 5.823m], scale.Bids);
        Assert.Equal(5.826m, scale.ReferencePrice);
    }

    [Fact]
    public void CanTemporarilyUseOneBookWhileOtherVenueIsUnavailable()
    {
        var bybit = Snapshot(
            [Level(5.825m), Level(5.824m)],
            [Level(5.826m), Level(5.827m)]);

        var scale = SharedPriceScale.Build(bybit, null, 2);

        Assert.NotNull(scale);
        Assert.Equal([5.826m, 5.827m], scale.Asks);
        Assert.Equal([5.825m, 5.824m], scale.Bids);
    }

    [Fact]
    public void UsesValidatedOfficialTickInsteadOfSparseVisibleDifference()
    {
        var sparse = Snapshot(
            [Level(5.824m), Level(5.822m)],
            [Level(5.828m), Level(5.830m)]);

        var scale = SharedPriceScale.Build(
            sparse,
            null,
            2,
            preferredTickSize: 0.001m);

        Assert.Equal(0.001m, scale!.TickSize);
    }

    [Fact]
    public void FallsBackWhenPreferredTickDoesNotAlignWithPrices()
    {
        var book = Snapshot(
            [Level(5.825m), Level(5.824m)],
            [Level(5.826m), Level(5.827m)]);

        var scale = SharedPriceScale.Build(
            book,
            null,
            2,
            preferredTickSize: 0.003m);

        Assert.Equal(0.001m, scale!.TickSize);
    }

    private static OrderBookSnapshot Snapshot(
        IReadOnlyList<OrderBookLevel> bids,
        IReadOnlyList<OrderBookLevel> asks) =>
        new("APTUSDT", 1, 1, bids, asks);

    private static OrderBookLevel Level(decimal price) => new(price, 1);
}
