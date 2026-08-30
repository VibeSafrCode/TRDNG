using System.Diagnostics;

namespace Trdng.Core.Diagnostics;

public readonly record struct RuntimeMemorySample(
    DateTimeOffset CapturedAt,
    long ManagedHeapBytes,
    long LargeObjectHeapBytes,
    long TotalAllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    long WorkingSetBytes,
    long? PrivateMemoryBytes,
    int ThreadCount);

public sealed class RuntimeMemoryRecorder
{
    private readonly object _gate = new();
    private readonly Queue<RuntimeMemorySample> _samples = [];
    private readonly int _capacity;
    private DateTimeOffset? _lastCapturedAt;

    public RuntimeMemoryRecorder(int capacity)
    {
        if (capacity is <= 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public int Capacity => _capacity;

    public IReadOnlyList<RuntimeMemorySample> Samples
    {
        get { lock (_gate) return _samples.ToArray(); }
    }

    public RuntimeMemorySample Capture()
    {
        var gc = GC.GetGCMemoryInfo();
        var generations = gc.GenerationInfo;
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var sample = new RuntimeMemorySample(
            DateTimeOffset.UtcNow,
            GC.GetTotalMemory(forceFullCollection: false),
            generations.Length > 3 ? generations[3].SizeAfterBytes : 0,
            GC.GetTotalAllocatedBytes(precise: false),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            process.WorkingSet64,
            process.PrivateMemorySize64 > 0 ? process.PrivateMemorySize64 : null,
            process.Threads.Count);
        Record(sample);
        return sample;
    }

    public void Record(RuntimeMemorySample sample)
    {
        Validate(sample);
        lock (_gate)
        {
            if (_lastCapturedAt is { } previous && sample.CapturedAt < previous)
                throw new ArgumentException("Memory samples must be chronological.", nameof(sample));
            while (_samples.Count >= _capacity) _samples.Dequeue();
            _samples.Enqueue(sample);
            _lastCapturedAt = sample.CapturedAt;
        }
    }

    private static void Validate(RuntimeMemorySample sample)
    {
        if (sample.CapturedAt == default ||
            sample.ManagedHeapBytes < 0 || sample.LargeObjectHeapBytes < 0 ||
            sample.TotalAllocatedBytes < 0 || sample.Gen0Collections < 0 ||
            sample.Gen1Collections < 0 || sample.Gen2Collections < 0 ||
            sample.WorkingSetBytes < 0 || sample.PrivateMemoryBytes < 0 ||
            sample.ThreadCount < 0)
            throw new ArgumentOutOfRangeException(nameof(sample));
    }
}

public sealed record MemorySoakBudget
{
    public MemorySoakBudget(
        long maximumManagedHeapBytes,
        long maximumWorkingSetBytes,
        long maximumPrivateMemoryBytes,
        long maximumRetainedManagedGrowthBytes,
        long maximumAllocatedBytesPerOperation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumManagedHeapBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumWorkingSetBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPrivateMemoryBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRetainedManagedGrowthBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumAllocatedBytesPerOperation);
        MaximumManagedHeapBytes = maximumManagedHeapBytes;
        MaximumWorkingSetBytes = maximumWorkingSetBytes;
        MaximumPrivateMemoryBytes = maximumPrivateMemoryBytes;
        MaximumRetainedManagedGrowthBytes = maximumRetainedManagedGrowthBytes;
        MaximumAllocatedBytesPerOperation = maximumAllocatedBytesPerOperation;
    }

    public long MaximumManagedHeapBytes { get; }
    public long MaximumWorkingSetBytes { get; }
    public long MaximumPrivateMemoryBytes { get; }
    public long MaximumRetainedManagedGrowthBytes { get; }
    public long MaximumAllocatedBytesPerOperation { get; }

    public MemorySoakBudgetResult Evaluate(
        IReadOnlyList<RuntimeMemorySample> samples,
        long operations)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(operations);
        if (samples.Count < 2)
            throw new ArgumentException("At least two memory samples are required.", nameof(samples));

        var failures = new List<string>(5);
        var peakManaged = samples.Max(static sample => sample.ManagedHeapBytes);
        var peakWorking = samples.Max(static sample => sample.WorkingSetBytes);
        var privateSamples = samples
            .Where(static sample => sample.PrivateMemoryBytes.HasValue)
            .Select(static sample => sample.PrivateMemoryBytes!.Value)
            .ToArray();
        var peakPrivate = privateSamples.Length == 0
            ? (long?)null
            : privateSamples.Max();
        var retainedGrowth = Math.Max(0,
            samples[^1].ManagedHeapBytes - samples[0].ManagedHeapBytes);
        var allocated = Math.Max(0,
            samples[^1].TotalAllocatedBytes - samples[0].TotalAllocatedBytes);
        var allocatedPerOperation = allocated / operations;

        if (peakManaged > MaximumManagedHeapBytes) failures.Add("MANAGED_HEAP_LIMIT");
        if (peakWorking > MaximumWorkingSetBytes) failures.Add("WORKING_SET_LIMIT");
        if (peakPrivate > MaximumPrivateMemoryBytes) failures.Add("PRIVATE_MEMORY_LIMIT");
        if (retainedGrowth > MaximumRetainedManagedGrowthBytes)
            failures.Add("RETAINED_MANAGED_GROWTH");
        if (allocatedPerOperation > MaximumAllocatedBytesPerOperation)
            failures.Add("ALLOCATION_RATE_LIMIT");

        return new(
            failures.Count == 0,
            failures,
            peakManaged,
            peakWorking,
            peakPrivate,
            retainedGrowth,
            allocatedPerOperation);
    }
}

public sealed record MemorySoakBudgetResult(
    bool Passed,
    IReadOnlyList<string> FailureCodes,
    long PeakManagedHeapBytes,
    long PeakWorkingSetBytes,
    long? PeakPrivateMemoryBytes,
    long RetainedManagedGrowthBytes,
    long AllocatedBytesPerOperation);
