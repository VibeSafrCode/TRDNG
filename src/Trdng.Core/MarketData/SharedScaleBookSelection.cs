using Trdng.Core.Instruments;

namespace Trdng.Core.MarketData;

public static class SharedScaleBookSelection
{
    public readonly record struct Candidate(
        MarketDataConnectionState State,
        OrderBookSnapshot? Book,
        DateTimeOffset LastSnapshotAt);

    public static IReadOnlyList<OrderBookSnapshot> Select(
        MarketProduct product,
        Candidate mexc,
        Candidate gate,
        Candidate bybit,
        DateTimeOffset now,
        MarketDataFreshnessOptions freshness)
    {
        var candidates = product == MarketProduct.Spot
            ? new[] { mexc }
            : new[] { gate, bybit };
        return candidates
            .Where(item => IsEligible(item.State, item.Book, item.LastSnapshotAt, now, freshness))
            .Select(item => item.Book!)
            .ToArray();
    }

    public static bool IsEligible(
        MarketDataConnectionState state,
        OrderBookSnapshot? book,
        DateTimeOffset lastSnapshotAt,
        DateTimeOffset now,
        MarketDataFreshnessOptions freshness) =>
        state == MarketDataConnectionState.Live &&
        book is not null &&
        lastSnapshotAt != default &&
        now - lastSnapshotAt < freshness.StaleAfter;
}
