using Trdng.Core.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class CrossVenueConsensusTests
{
    [Fact]
    public void LargeBidAbsorptionOutweighsSmallAskHolding()
    {
        var verdict = CrossVenueConsensus.Evaluate([
            new("BYBIT", LiquiditySide.Bid, LiquidityBehavior.Absorbing, 10),
            new("GATE", LiquiditySide.Ask, LiquidityBehavior.Holding, 1)
        ]);

        Assert.Equal(MarketConsensus.Bullish, verdict.Consensus);
        Assert.Equal(2, verdict.VenueCount);
    }

    [Fact]
    public void PullingAskIsBullish()
    {
        var verdict = CrossVenueConsensus.Evaluate([
            new("GATE", LiquiditySide.Ask, LiquidityBehavior.Pulling, 5)
        ]);

        Assert.Equal(MarketConsensus.Bullish, verdict.Consensus);
    }

    [Fact]
    public void BalancedConflictIsMixed()
    {
        var verdict = CrossVenueConsensus.Evaluate([
            new("BYBIT", LiquiditySide.Bid, LiquidityBehavior.Building, 5),
            new("GATE", LiquiditySide.Ask, LiquidityBehavior.Building, 5)
        ]);

        Assert.Equal(MarketConsensus.Mixed, verdict.Consensus);
    }
}
