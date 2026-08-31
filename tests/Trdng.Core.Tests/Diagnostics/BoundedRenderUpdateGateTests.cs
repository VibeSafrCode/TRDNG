using Trdng.Core.Diagnostics;

namespace Trdng.Core.Tests.Diagnostics;

public sealed class BoundedRenderUpdateGateTests
{
    [Fact]
    public void MillionProducerRequestsRetainExactlyOnePendingRefresh()
    {
        var gate = new BoundedRenderUpdateGate();

        Parallel.For(0, 1_000_000, _ => gate.Request());

        Assert.Equal(1_000_000, gate.RequestCount);
        Assert.Equal(1, gate.PendingCount);
        Assert.True(gate.TryConsume());
        Assert.False(gate.TryConsume());
        Assert.Equal(1, gate.ConsumeCount);
        Assert.Equal(0, gate.PendingCount);
    }

    [Fact]
    public void RequestsArrivingAfterConsumeCreateOnlyTheNextPendingRefresh()
    {
        var gate = new BoundedRenderUpdateGate();
        gate.Request();
        Assert.True(gate.TryConsume());

        for (var index = 0; index < 100_000; index++)
        {
            gate.Request();
        }

        Assert.Equal(1, gate.PendingCount);
        Assert.True(gate.TryConsume());
        Assert.False(gate.TryConsume());
        Assert.Equal(2, gate.ConsumeCount);
    }
}
