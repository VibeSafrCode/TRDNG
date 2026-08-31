using Trdng.Core.Diagnostics;

namespace Trdng.Core.Tests.Diagnostics;

public sealed class BoundedBookScalePolicyTests
{
    public static TheoryData<int> ScaleTiers => new()
    {
        3, 6, 12, 24, 48, 100
    };

    [Theory]
    [MemberData(nameof(ScaleTiers))]
    public void LatestWinsRetainsAtMostOneSnapshotPerLogicalBook(int logicalBooks)
    {
        var store = new BoundedLatestSnapshotStore<Marker>(logicalBooks);
        var gate = new BoundedRenderUpdateGate();

        Parallel.For(0, 100_000, update =>
        {
            var book = update % logicalBooks;
            store.Publish(book, new Marker(update));
            gate.Request();
        });

        Assert.Equal(100_000, store.PublishCount);
        Assert.Equal(logicalBooks, store.RetainedCount);
        Assert.Equal(1, gate.PendingCount);
        Assert.True(gate.TryConsume());
        Assert.InRange(store.RetainedCount, 1,
            BoundedBookScalePolicy.MaximumLogicalBooks);
    }

    [Fact]
    public void VisibleRowBudgetDoesNotGrowWithHiddenLogicalBooks()
    {
        Assert.Equal(200,
            BoundedBookScalePolicy.ResolveRowsPerSide(3, 200));
        Assert.Equal(100,
            BoundedBookScalePolicy.ResolveRowsPerSide(6, 200));
        Assert.Equal(50,
            BoundedBookScalePolicy.ResolveRowsPerSide(12, 200));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BoundedBookScalePolicy.ResolveRowsPerSide(24, 200));
    }

    private sealed record Marker(int UpdateId);
}
