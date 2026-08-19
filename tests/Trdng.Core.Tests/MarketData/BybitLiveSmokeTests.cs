using Trdng.Bybit.MarketData;
using Trdng.Core.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class BybitLiveSmokeTests
{
    [Fact]
    [Trait("Category", "Live")]
    public async Task ReceivesRealBtcUsdtSnapshotWhenExplicitlyEnabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("TRDNG_LIVE_TESTS"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var snapshotReceived =
            new TaskCompletionSource<OrderBookSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        await using var client = new BybitPublicOrderBookClient();
        client.SnapshotReceived += snapshot =>
            snapshotReceived.TrySetResult(snapshot);
        client.Start();

        var snapshot = await snapshotReceived.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal("BTCUSDT", snapshot.Symbol);
        Assert.NotEmpty(snapshot.Bids);
        Assert.NotEmpty(snapshot.Asks);
        Assert.True(snapshot.BestBid > 0);
        Assert.True(snapshot.BestAsk > snapshot.BestBid);
    }
}
