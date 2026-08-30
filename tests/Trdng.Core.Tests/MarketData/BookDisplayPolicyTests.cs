using Trdng.Core.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class BookDisplayPolicyTests
{
    [Fact]
    public void AutoDepthFillsAvailableHalfAndManualDepthIsExact()
    {
        var automatic = BookDisplayPolicy.Resolve(true, 24, 200, 500, 720);
        Assert.Equal(40, automatic.Depth);
        Assert.Equal(18, automatic.RowHeight);
        Assert.Equal(480, automatic.BarWidth);

        var manual = BookDisplayPolicy.Resolve(false, 25, 200, 500, 720);
        Assert.Equal(25, manual.Depth);
        Assert.Equal(28.8, manual.RowHeight, 3);
    }

    [Fact]
    public void TrackpadAdjustmentIsFineGrainedAndBounded()
    {
        Assert.Equal(23, BookDisplayPolicy.AdjustDepth(24, -1, 1, 50));
        Assert.Equal(26, BookDisplayPolicy.AdjustDepth(24, 1, 2, 50));
        Assert.Equal(8, BookDisplayPolicy.AdjustDepth(8, -1, 2, 50));
        Assert.Equal(50, BookDisplayPolicy.AdjustDepth(50, 1, 2, 50));
    }

    [Fact]
    public void VisibleMaximumDrivesAutomaticWidthWhileManualReferenceCaps()
    {
        var largest = VisibleBookVolumeScale.Largest([2m, 10m, 5m]);
        Assert.Equal(10m, largest);
        Assert.Equal(1, VisibleBookVolumeScale.Ratio(10,
            VisibleBookVolumeScale.Reference(largest, true, 1)));
        Assert.Equal(0.5, VisibleBookVolumeScale.Ratio(5,
            VisibleBookVolumeScale.Reference(largest, true, 1)));
        Assert.Equal(1, VisibleBookVolumeScale.Ratio(10,
            VisibleBookVolumeScale.Reference(largest, false, 4)));
    }

    [Fact]
    public void AutomaticVolumeReferencesAreIndependentForAskAndBidSides()
    {
        var scale = VisibleBookVolumeScale.ResolveSides(
            [2m, 8m, 4m], [3m, 12m, 6m], automatic: true, manual: 1m);

        Assert.Equal(8m, scale.AskLargest);
        Assert.Equal(12m, scale.BidLargest);
        Assert.Equal(1, VisibleBookVolumeScale.Ratio(8m, scale.AskReference));
        Assert.Equal(1, VisibleBookVolumeScale.Ratio(12m, scale.BidReference));
        Assert.Equal(0.5, VisibleBookVolumeScale.Ratio(4m, scale.AskReference));
        Assert.Equal(0.5, VisibleBookVolumeScale.Ratio(6m, scale.BidReference));
    }

    [Fact]
    public void PaletteUsesOwnerDefaultsAndRejectsInvalidBrushes()
    {
        Assert.Equal("#B3FFD60A", BookBarPalette.OwnerDefault.Ask);
        Assert.True(BookBarPalette.TryCreate(
            "#112233", "#AA445566", "#abcdef", "#778899", out var palette));
        Assert.Equal("#ABCDEF", palette.Bid);
        Assert.False(BookBarPalette.TryCreate(
            "yellow", "#AA445566", "#ABCDEF", "#778899", out _));
    }
}
