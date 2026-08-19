namespace Trdng.Core.MarketData;

public static class VenueCardStatus
{
    public static string Resolve(
        MarketDataConnectionState state,
        DateTimeOffset? lastSnapshotAt,
        DateTimeOffset now,
        MarketDataFreshnessOptions freshness)
    {
        if (state == MarketDataConnectionState.Live &&
            lastSnapshotAt is { } observed && now - observed >= freshness.StaleAfter)
            return "STALE";

        return state switch
        {
            MarketDataConnectionState.Live => "LIVE",
            MarketDataConnectionState.Connecting => "CONNECTING",
            MarketDataConnectionState.WaitingForSnapshot => "CONNECTING",
            MarketDataConnectionState.Reconnecting => "CONNECTING",
            _ => "UNAVAILABLE"
        };
    }
}
