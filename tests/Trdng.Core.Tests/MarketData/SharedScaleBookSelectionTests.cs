using Trdng.Core.Instruments;
using Trdng.Core.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class SharedScaleBookSelectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly MarketDataFreshnessOptions Freshness =
        new(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(2));
    private static readonly OrderBookSnapshot Mexc = Book("MEXC");
    private static readonly OrderBookSnapshot Gate = Book("GATE");
    private static readonly OrderBookSnapshot Bybit = Book("BYBIT");

    [Fact]
    public void PerpetualIncludesOnlyLiveGateAndBybit()
    {
        var books = SharedScaleBookSelection.Select(MarketProduct.Perpetual,
            Live(Mexc), Live(Gate),
            new(MarketDataConnectionState.Reconnecting, Bybit, Now), Now, Freshness);
        Assert.Equal(["GATE"], books.Select(book => book.Symbol));
    }

    [Fact]
    public void SpotIncludesOnlyLiveMexc()
    {
        var books = SharedScaleBookSelection.Select(MarketProduct.Spot,
            Live(Mexc), Live(Gate), Live(Bybit), Now, Freshness);
        Assert.Equal(["MEXC"], books.Select(book => book.Symbol));
    }

    [Fact]
    public void WarningAgeIsStillIncluded()
    {
        var warning = new SharedScaleBookSelection.Candidate(
            MarketDataConnectionState.Live, Gate, Now - TimeSpan.FromSeconds(1));
        var books = SharedScaleBookSelection.Select(MarketProduct.Perpetual,
            Live(Mexc), warning, Live(Bybit), Now, Freshness);
        Assert.Contains(Gate, books);
    }

    [Fact]
    public void StaleAgeIsExcluded()
    {
        var stale = new SharedScaleBookSelection.Candidate(
            MarketDataConnectionState.Live, Gate, Now - Freshness.StaleAfter);
        var books = SharedScaleBookSelection.Select(MarketProduct.Perpetual,
            Live(Mexc), stale, Live(Bybit), Now, Freshness);
        Assert.DoesNotContain(Gate, books);
    }

    private static SharedScaleBookSelection.Candidate Live(OrderBookSnapshot book) =>
        new(MarketDataConnectionState.Live, book, Now);

    private static OrderBookSnapshot Book(string symbol) =>
        new(symbol, 1, 1, [new(1, 1)], [new(2, 1)]);
}
