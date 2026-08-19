namespace Trdng.Core.MarketData;

public enum CrossVenueComparisonStatus
{
    Ready,
    MissingBook,
    NotLive,
    Warning,
    Stale
}

public readonly record struct VenueBookObservation(
    string Venue,
    MarketDataConnectionState ConnectionState,
    OrderBookSnapshot? Snapshot,
    DateTimeOffset ReceivedAt);

public readonly record struct CrossVenueBookComparison(
    CrossVenueComparisonStatus Status,
    decimal? FirstMidPrice,
    decimal? SecondMidPrice,
    decimal? DivergenceBasisPoints,
    string? HigherVenue)
{
    public static CrossVenueBookComparison Evaluate(
        VenueBookObservation first,
        VenueBookObservation second,
        DateTimeOffset now,
        MarketDataFreshnessOptions freshness)
    {
        ArgumentNullException.ThrowIfNull(freshness);

        if (first.ConnectionState != MarketDataConnectionState.Live ||
            second.ConnectionState != MarketDataConnectionState.Live)
        {
            return new(CrossVenueComparisonStatus.NotLive, null, null, null, null);
        }

        if (first.Snapshot is null || second.Snapshot is null ||
            first.Snapshot.BestBid is null || first.Snapshot.BestAsk is null ||
            second.Snapshot.BestBid is null || second.Snapshot.BestAsk is null)
        {
            return new(CrossVenueComparisonStatus.MissingBook, null, null, null, null);
        }

        var oldestAge = TimeSpan.FromTicks(Math.Max(
            (now - first.ReceivedAt).Ticks,
            (now - second.ReceivedAt).Ticks));
        if (oldestAge > freshness.StaleAfter)
        {
            return new(CrossVenueComparisonStatus.Stale, null, null, null, null);
        }

        var firstMid = (first.Snapshot.BestBid.Value +
                        first.Snapshot.BestAsk.Value) / 2;
        var secondMid = (second.Snapshot.BestBid.Value +
                         second.Snapshot.BestAsk.Value) / 2;
        var reference = (firstMid + secondMid) / 2;
        var divergence = reference == 0
            ? 0
            : decimal.Abs(firstMid - secondMid) / reference * 10_000;
        var higherVenue = firstMid == secondMid
            ? null
            : firstMid > secondMid ? first.Venue : second.Venue;

        return new(
            oldestAge > freshness.WarningAfter
                ? CrossVenueComparisonStatus.Warning
                : CrossVenueComparisonStatus.Ready,
            firstMid,
            secondMid,
            divergence,
            higherVenue);
    }
}
