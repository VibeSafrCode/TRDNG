using System.Text;
using Trdng.Bybit.MarketData;
using Trdng.Core.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class BybitPublicTradeMessageParserTests
{
    [Fact]
    public void ParsesPublicTradeBatch()
    {
        const string json = """
            {
              "topic": "publicTrade.BTCUSDT",
              "type": "snapshot",
              "ts": 1672304486868,
              "data": [{
                "T": 1672304486865,
                "s": "BTCUSDT",
                "S": "Buy",
                "v": "0.001",
                "p": "16578.50",
                "L": "PlusTick",
                "i": "trade-1",
                "BT": false,
                "seq": 1783284617
              }]
            }
            """;

        var parsed = BybitPublicTradeMessageParser.TryParse(
            Encoding.UTF8.GetBytes(json),
            out var trades);

        Assert.True(parsed);
        var trade = Assert.Single(trades);
        Assert.Equal("BTCUSDT", trade.Symbol);
        Assert.Equal(AggressorSide.Buy, trade.Aggressor);
        Assert.Equal(16578.50m, trade.Price);
        Assert.Equal(0.001m, trade.Quantity);
    }
}
