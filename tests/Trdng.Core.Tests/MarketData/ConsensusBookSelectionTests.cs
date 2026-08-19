using Trdng.Core.Instruments;
using Trdng.Core.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class ConsensusBookSelectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly MarketDataFreshnessOptions Freshness =
        new(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(2));

    [Fact]
    public void StaleIsExcludedAndProducesNoEligibleData()
    {
        var selected = Select(MarketProduct.Spot,
            Candidate("MEXC", MarketProduct.Spot, Now - Freshness.StaleAfter));
        Assert.Empty(selected);
    }

    [Fact]
    public void WarningIsIncluded()
    {
        var warning = Candidate("MEXC", MarketProduct.Spot, Now - TimeSpan.FromSeconds(1));
        Assert.Single(Select(MarketProduct.Spot, warning));
    }

    [Fact]
    public void DoesNotMixSpotAndPerpetual()
    {
        var selected = Select(MarketProduct.Perpetual,
            Candidate("MEXC", MarketProduct.Spot, Now),
            Candidate("BYBIT", MarketProduct.Perpetual, Now));
        Assert.Equal(["BYBIT"], selected.Select(item => item.Venue));
    }

    private static IReadOnlyList<ConsensusBookSelection.Candidate> Select(
        MarketProduct product,
        params ConsensusBookSelection.Candidate[] candidates) =>
        ConsensusBookSelection.Select(product, candidates, Now, Freshness);

    private static ConsensusBookSelection.Candidate Candidate(
        string venue, MarketProduct product, DateTimeOffset timestamp) =>
        new(venue, product, MarketDataConnectionState.Live,
            new OrderBookSnapshot(venue, 1, 1, [new(1, 1)], [new(2, 1)]), timestamp);
}
