namespace Trdng.Core.MarketData;

public sealed class VenueLiquidityTrackers
{
    public LiquidityTracker Bybit { get; } = new();
    public LiquidityTracker Gate { get; } = new();
    public LiquidityTracker Mexc { get; } = new();

    public void ResetAll()
    {
        Bybit.Reset();
        Gate.Reset();
        Mexc.Reset();
    }
}
