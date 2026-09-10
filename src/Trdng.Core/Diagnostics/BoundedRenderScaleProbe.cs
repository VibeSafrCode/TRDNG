using System.Diagnostics;
using Trdng.Core.MarketData;

namespace Trdng.Core.Diagnostics;

public sealed record BoundedRenderScaleResult(
    int LogicalBooks,
    int VisibleBooks,
    int RowsPerSide,
    int VisibleRowsTotal,
    int ProducerUpdates,
    int RenderPasses,
    long RenderedRowMutations,
    int ConfiguredMaximumRenderHertz,
    int MaximumPendingRenderWork,
    int RetainedSnapshots,
    long AllocatedBytes,
    long ManagedHeapBytesAfterCollection,
    long WorkingSetBytes,
    TimeSpan Elapsed,
    TimeSpan CpuTime,
    int ReconnectCount,
    bool Passed);

public static class BoundedRenderScaleProbe
{
    public static readonly int[] DefaultTiers = [3, 6, 12, 24, 48, 100];

    public static BoundedRenderScaleResult Run(
        int logicalBooks,
        int producerUpdates = 100_000,
        int updatesPerRenderPass = 10)
    {
        if (logicalBooks is < 1 or > BoundedBookScalePolicy.MaximumLogicalBooks)
            throw new ArgumentOutOfRangeException(nameof(logicalBooks));
        if (producerUpdates < logicalBooks)
            throw new ArgumentOutOfRangeException(nameof(producerUpdates));
        if (updatesPerRenderPass < 1)
            throw new ArgumentOutOfRangeException(nameof(updatesPerRenderPass));

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var cpuBefore = process.TotalProcessorTime;
        var clock = Stopwatch.StartNew();
        var store = new BoundedLatestSnapshotStore<OrderBookSnapshot>(logicalBooks);
        var renderGate = new BoundedRenderUpdateGate();
        var visibleBooks = Math.Min(
            logicalBooks, BoundedBookScalePolicy.MaximumVisibleBooks);
        var rowsPerSide = BoundedBookScalePolicy.ResolveRowsPerSide(
            visibleBooks, BoundedBookScalePolicy.MaximumSnapshotLevelsPerSide);
        var renderPasses = 0;
        long renderedRowMutations = 0;
        var maximumPending = 0;
        var renderRows = Enumerable.Range(0, visibleBooks * rowsPerSide * 2)
            .Select(static _ => new ScaleRowState())
            .ToArray();

        for (var update = 0; update < producerUpdates; update++)
        {
            var bookIndex = update % logicalBooks;
            store.Publish(bookIndex, new OrderBookSnapshot(
                $"BOOK-{bookIndex}", update, update,
                [new(100m, update + 1m)],
                [new(101m, update + 1m)]));
            renderGate.Request();
            maximumPending = Math.Max(maximumPending, renderGate.PendingCount);
            if ((update + 1) % updatesPerRenderPass == 0 && renderGate.TryConsume())
            {
                for (var book = 0; book < visibleBooks; book++)
                {
                    var snapshot = store.ReadLatest(book);
                    var firstRow = book * rowsPerSide * 2;
                    var lastRow = firstRow + rowsPerSide * 2;
                    for (var row = firstRow; row < lastRow; row++)
                    {
                        renderRows[row].Apply(snapshot?.UpdateId ?? -1);
                        renderedRowMutations++;
                    }
                }
                renderPasses++;
            }
        }
        if (renderGate.TryConsume()) renderPasses++;

        clock.Stop();
        process.Refresh();
        var cpu = process.TotalProcessorTime - cpuBefore;
        var workingSet = process.WorkingSet64;
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        var heap = GC.GetTotalMemory(forceFullCollection: false);
        var visibleRows = checked(visibleBooks * rowsPerSide * 2);
        var allocatedPerUpdate = producerUpdates == 0 ? long.MaxValue :
            allocated / producerUpdates;
        var passed = maximumPending <= 1 &&
            store.RetainedCount == logicalBooks &&
            visibleRows <= BoundedBookScalePolicy.MaximumVisibleRowsGlobal &&
            renderedRowMutations == (long)renderPasses * visibleRows &&
            allocatedPerUpdate <= 4_096 &&
            workingSet < ProcessMemoryCircuitPolicy.WorkingTargetBytes;

        return new(
            logicalBooks,
            visibleBooks,
            rowsPerSide,
            visibleRows,
            producerUpdates,
            renderPasses,
            renderedRowMutations,
            BoundedBookScalePolicy.MaximumRenderHertz,
            maximumPending,
            store.RetainedCount,
            allocated,
            heap,
            workingSet,
            clock.Elapsed,
            cpu,
            ReconnectCount: 0,
            passed);
    }

    private sealed class ScaleRowState
    {
        public long Sequence { get; private set; }

        public void Apply(long sequence) => Sequence = sequence;
    }
}
