namespace Trdng.Core.MarketData;

public sealed record MarketDataFreshnessOptions
{
    public static MarketDataFreshnessOptions ScalpingDefault { get; } =
        new(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(2));

    public MarketDataFreshnessOptions(
        TimeSpan warningAfter,
        TimeSpan staleAfter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            warningAfter,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            staleAfter,
            warningAfter);

        WarningAfter = warningAfter;
        StaleAfter = staleAfter;
    }

    public TimeSpan WarningAfter { get; }

    public TimeSpan StaleAfter { get; }
}
