using System.Text;
using Trdng.Bybit.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class BybitOrderBookMessageParserTests
{
    [Fact]
    public void ParsesOfficialSnapshotShape()
    {
        const string json = """
            {
              "topic": "orderbook.50.BTCUSDT",
              "type": "snapshot",
              "ts": 1672304484978,
              "data": {
                "s": "BTCUSDT",
                "b": [["16493.50", "0.006"]],
                "a": [["16611.00", "0.029"]],
                "u": 18521288,
                "seq": 7961638724
              },
              "cts": 1672304484976
            }
            """;

        var message = BybitOrderBookMessageParser.Parse(Encoding.UTF8.GetBytes(json));

        Assert.Equal(BybitOrderBookMessageType.Snapshot, message.Type);
        Assert.Equal("BTCUSDT", message.Update.Symbol);
        Assert.Equal(18521288, message.Update.UpdateId);
        Assert.Equal(7961638724, message.Update.CrossSequence);
        Assert.Equal(16493.50m, message.Update.Bids[0].Price);
        Assert.Equal(0.006m, message.Update.Bids[0].Quantity);
    }
}
