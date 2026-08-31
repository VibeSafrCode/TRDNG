namespace Trdng.Core.Diagnostics;

/// <summary>
/// Coalesces any number of producer notifications into one pending UI refresh.
/// A UI-owned bounded timer consumes the pending bit, so producers never enqueue
/// closures or retain market snapshots in an unbounded dispatcher queue.
/// </summary>
public sealed class BoundedRenderUpdateGate
{
    private int _pending;
    private long _requestCount;
    private long _consumeCount;

    public long RequestCount => Interlocked.Read(ref _requestCount);

    public long ConsumeCount => Interlocked.Read(ref _consumeCount);

    public int PendingCount => Volatile.Read(ref _pending);

    public void Request()
    {
        Interlocked.Increment(ref _requestCount);
        Interlocked.Exchange(ref _pending, 1);
    }

    public bool TryConsume()
    {
        if (Interlocked.Exchange(ref _pending, 0) == 0)
        {
            return false;
        }

        Interlocked.Increment(ref _consumeCount);
        return true;
    }
}
