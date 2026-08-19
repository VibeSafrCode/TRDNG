namespace Trdng.Core.MarketData;

public sealed class SelectionGeneration
{
    private long _current;
    public long Current => Volatile.Read(ref _current);
    public long Next() => Interlocked.Increment(ref _current);
    public bool IsCurrent(long expected) => expected == Current;
}
