namespace Trdng.Core.MarketData;

public sealed record PublicTrade(
    string Id,
    string Symbol,
    DateTimeOffset MatchedAt,
    AggressorSide Aggressor,
    decimal Price,
    decimal Quantity,
    long CrossSequence);
