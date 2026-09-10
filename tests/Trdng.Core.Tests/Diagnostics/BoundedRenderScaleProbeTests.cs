using Trdng.Core.Diagnostics;

namespace Trdng.Core.Tests.Diagnostics;

public sealed class BoundedRenderScaleProbeTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(24)]
    [InlineData(48)]
    [InlineData(100)]
    public void TierKeepsLatestStateAndRenderWorkBounded(int logicalBooks)
    {
        var result = BoundedRenderScaleProbe.Run(
            logicalBooks,
            producerUpdates: 10_000,
            updatesPerRenderPass: 10);

        Assert.True(result.Passed);
        Assert.Equal(logicalBooks, result.RetainedSnapshots);
        Assert.InRange(result.MaximumPendingRenderWork, 0, 1);
        Assert.InRange(result.VisibleRowsTotal, 1,
            BoundedBookScalePolicy.MaximumVisibleRowsGlobal);
        Assert.Equal((long)result.RenderPasses * result.VisibleRowsTotal,
            result.RenderedRowMutations);
        Assert.Equal(BoundedBookScalePolicy.MaximumRenderHertz,
            result.ConfiguredMaximumRenderHertz);
        Assert.Equal(0, result.ReconnectCount);
    }
}
