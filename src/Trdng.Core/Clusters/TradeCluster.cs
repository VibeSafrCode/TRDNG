namespace Trdng.Core.Clusters;

public sealed record TradeCluster(
    DateTimeOffset StartsAt,
    TimeSpan Interval,
    IReadOnlyList<ClusterLevel> Levels)
{
    public decimal TotalVolume => Levels.Sum(static level => level.TotalVolume);

    public decimal Delta => Levels.Sum(static level => level.Delta);
}
