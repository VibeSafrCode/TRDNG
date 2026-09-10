using System.Diagnostics;
using Trdng.Core.Diagnostics;

namespace Trdng.Desktop.Diagnostics;

internal sealed class RuntimeProcessMemoryGuard : IDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(10);
    private readonly ProcessMemoryCircuitPolicy _policy = new();
    private readonly Timer _timer;
    private int _started;
    private int _terminalTripped;

    public RuntimeProcessMemoryGuard()
    {
        _timer = new Timer(Sample, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event Action<ProcessMemoryCircuitAction>? Tripped;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("Memory guard already started.");
        _timer.Change(SampleInterval, SampleInterval);
    }

    public void Dispose() => _timer.Dispose();

    private void Sample(object? state)
    {
        if (Volatile.Read(ref _terminalTripped) != 0) return;
        using var process = Process.GetCurrentProcess();
        var action = _policy.Observe(new(
            process.WorkingSet64,
            GC.GetGCMemoryInfo().HeapSizeBytes));
        if (action == ProcessMemoryCircuitAction.Continue) return;
        if (action != ProcessMemoryCircuitAction.Warning &&
            Interlocked.Exchange(ref _terminalTripped, 1) != 0) return;
        if (action != ProcessMemoryCircuitAction.Warning)
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Tripped?.Invoke(action);
    }
}
