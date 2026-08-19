namespace Trdng.Core.MarketData;

public enum LiquiditySide
{
    Bid,
    Ask
}

public enum LiquidityBehavior
{
    Normal,
    Building,
    Holding,
    Pulling,
    Absorbing
}

public readonly record struct LiquidityLevelState(
    LiquiditySide Side,
    decimal Price,
    decimal Quantity,
    TimeSpan VisibleFor,
    decimal ExecutedVolume,
    LiquidityBehavior Behavior);
