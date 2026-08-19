using Trdng.Core.Instruments;

namespace Trdng.Core.MarketData;

public static class ConsensusBookSelection
{
    public sealed record Candidate(
        string Venue,
        MarketProduct Product,
        MarketDataConnectionState State,
        OrderBookSnapshot? Book,
        DateTimeOffset LastSnapshotAt);

    public static IReadOnlyList<Candidate> Select(
        MarketProduct selectedProduct,
        IEnumerable<Candidate> candidates,
        DateTimeOffset now,
        MarketDataFreshnessOptions freshness) =>
        candidates.Where(candidate =>
                candidate.Product == selectedProduct &&
                SharedScaleBookSelection.IsEligible(
                    candidate.State,
                    candidate.Book,
                    candidate.LastSnapshotAt,
                    now,
                    freshness))
            .ToArray();
}
