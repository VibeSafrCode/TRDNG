using Trdng.Core.Diagnostics;

namespace Trdng.Core.Tests.Diagnostics;

public sealed class MemoryObservabilityTests
{
    [Fact]
    public void RecorderRetainsOnlyConfiguredNumberOfChronologicalSamples()
    {
        var recorder = new RuntimeMemoryRecorder(2);
        recorder.Record(Sample(1, 10, 20));
        recorder.Record(Sample(2, 20, 30));
        recorder.Record(Sample(3, 30, 40));

        Assert.Equal(2, recorder.Samples.Count);
        Assert.Equal(20, recorder.Samples[0].ManagedHeapBytes);
        Assert.Equal(30, recorder.Samples[1].ManagedHeapBytes);
        Assert.Throws<ArgumentException>(() => recorder.Record(Sample(1, 1, 1)));
    }

    [Fact]
    public void BudgetReportsOnlyStableAllowlistedFailureCodes()
    {
        var budget = new MemorySoakBudget(100, 200, 300, 10, 5);
        var result = budget.Evaluate([
            Sample(1, 50, 100, working: 100, privateBytes: 100),
            Sample(2, 120, 200, working: 250, privateBytes: 350)], 10);

        Assert.False(result.Passed);
        Assert.Equal([
            "MANAGED_HEAP_LIMIT",
            "WORKING_SET_LIMIT",
            "PRIVATE_MEMORY_LIMIT",
            "RETAINED_MANAGED_GROWTH",
            "ALLOCATION_RATE_LIMIT"], result.FailureCodes);
    }

    [Fact]
    public async Task DeterministicReplayKeepsBooksClustersSamplesAndClientsBounded()
    {
        var recorder = new RuntimeMemoryRecorder(16);
        var result = await DeterministicMarketDataReplay.RunAsync(
            new(500, 25, 25, 64), recorder);

        Assert.Equal(500, result.Cycles);
        Assert.Equal(result.CreatedClients, result.DisposedClients);
        Assert.InRange(result.MaximumActiveClients, 1, 2);
        Assert.InRange(result.MaximumObservedLevelsPerSide, 1, 64);
        Assert.InRange(result.MaximumCompletedClusters, 0, 16);
        Assert.InRange(result.MemorySamples, 2, recorder.Capacity);
    }

    [Fact]
    public async Task DeterministicReplayHonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DeterministicMarketDataReplay.RunAsync(
                new(100, 10, 10, 16),
                new RuntimeMemoryRecorder(8),
                cancellation.Token));
    }

    private static RuntimeMemorySample Sample(
        int seconds,
        long managed,
        long allocated,
        long working = 0,
        long? privateBytes = null) => new(
        DateTimeOffset.UnixEpoch.AddSeconds(seconds),
        managed,
        0,
        allocated,
        0,
        0,
        0,
        working,
        privateBytes,
        1);
}
