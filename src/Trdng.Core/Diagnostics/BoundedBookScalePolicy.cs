namespace Trdng.Core.Diagnostics;

public static class BoundedBookScalePolicy
{
    public const int MaximumLogicalBooks = 100;
    public const int MaximumVisibleBooks = 12;
    public const int MaximumSnapshotLevelsPerSide = 200;
    public const int MaximumVisibleRowsGlobal = 1_200;
    public const int MaximumRenderHertz = 10;

    public static int ResolveRowsPerSide(int visibleBooks, int desiredRowsPerSide)
    {
        if (visibleBooks is < 1 or > MaximumVisibleBooks)
            throw new ArgumentOutOfRangeException(nameof(visibleBooks));
        if (desiredRowsPerSide < 1)
            throw new ArgumentOutOfRangeException(nameof(desiredRowsPerSide));

        var globalShare = MaximumVisibleRowsGlobal / checked(visibleBooks * 2);
        return Math.Min(
            Math.Min(desiredRowsPerSide, MaximumSnapshotLevelsPerSide),
            globalShare);
    }
}

public sealed class BoundedLatestSnapshotStore<T> where T : class
{
    private readonly T?[] _slots;
    private long _publishCount;

    public BoundedLatestSnapshotStore(int logicalBooks)
    {
        if (logicalBooks is < 1 or > BoundedBookScalePolicy.MaximumLogicalBooks)
            throw new ArgumentOutOfRangeException(nameof(logicalBooks));
        _slots = new T[logicalBooks];
    }

    public int Capacity => _slots.Length;

    public long PublishCount => Interlocked.Read(ref _publishCount);

    public int RetainedCount => _slots.Count(static value => value is not null);

    public void Publish(int bookIndex, T snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if ((uint)bookIndex >= (uint)_slots.Length)
            throw new ArgumentOutOfRangeException(nameof(bookIndex));
        Interlocked.Exchange(ref _slots[bookIndex], snapshot);
        Interlocked.Increment(ref _publishCount);
    }

    public T? ReadLatest(int bookIndex)
    {
        if ((uint)bookIndex >= (uint)_slots.Length)
            throw new ArgumentOutOfRangeException(nameof(bookIndex));
        return Volatile.Read(ref _slots[bookIndex]);
    }
}
