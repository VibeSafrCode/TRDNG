namespace Trdng.Core.Clusters;

public sealed record ClusterLevel(
    decimal Price,
    decimal BidVolume,
    decimal AskVolume)
{
    public decimal TotalVolume => BidVolume + AskVolume;

    public decimal Delta => AskVolume - BidVolume;
}
