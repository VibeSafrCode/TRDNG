using Trdng.Core.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class SelectionGenerationTests
{
    [Fact]
    public void OldCallbacksAndResetPostsAreRejectedAfterSwitch()
    {
        var generation = new SelectionGeneration();
        var old = generation.Next();
        Assert.True(generation.IsCurrent(old));

        var current = generation.Next();

        Assert.False(generation.IsCurrent(old));
        Assert.True(generation.IsCurrent(current));
    }
}
