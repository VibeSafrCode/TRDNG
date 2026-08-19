using Trdng.Core.MarketData;
using Trdng.Mexc.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class MexcLiveSmokeTests
{
    [Fact]
    [Trait("Category", "Live")]
    public async Task ReceivesRealAptUsdtSpotMetadataAndBookWhenExplicitlyEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("TRDNG_LIVE_TESTS"), "1", StringComparison.Ordinal))
            return;

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var metadata = await new MexcInstrumentMetadataClient(httpClient).GetSpotAsync("APTUSDT");
        Assert.Equal("APTUSDT", metadata.Symbol);
        Assert.True(metadata.TickSize > 0);
        Assert.True(metadata.MinimumBaseQuantity > 0);

        var received = new TaskCompletionSource<OrderBookSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var states = new List<string>();
        await using var client = new MexcPublicOrderBookClient(httpClient, "APTUSDT", maxConnectionAttempts: 1);
        client.SnapshotReceived += snapshot => received.TrySetResult(snapshot);
        client.StateChanged += (state, detail) => states.Add($"{state}: {detail}");
        client.DiagnosticReceived += detail => states.Add(detail);
        client.Start();

        OrderBookSnapshot book;
        try { book = await received.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        catch (TimeoutException exception)
        {
            throw new TimeoutException(string.Join(" | ", states), exception);
        }
        Assert.Equal("APTUSDT", book.Symbol);
        Assert.NotEmpty(book.Bids);
        Assert.NotEmpty(book.Asks);
        Assert.True(book.BestBid > 0);
        Assert.True(book.BestAsk > book.BestBid);
    }
}
