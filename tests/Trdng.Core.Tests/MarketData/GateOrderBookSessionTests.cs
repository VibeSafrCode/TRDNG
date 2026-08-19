using System.Text;
using Trdng.Core.MarketData;
using Trdng.Gate.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class GateOrderBookSessionTests
{
    [Fact]
    public void FullSnapshotConvertsContractsToBtc()
    {
        var json = """
        {"channel":"futures.obu","event":"update","result":{
          "full":true,"s":"ob.BTC_USDT.50","u":10,
          "b":[["63700.0","10000"]],"a":[["63700.1","2500"]]}}
        """;
        Assert.True(GateOrderBookMessageParser.TryParse(
            Encoding.UTF8.GetBytes(json),
            out var message));
        var session = new GateOrderBookSession(new OrderBookEngine());
        session.Apply(message!);

        var book = session.Engine.Capture(1);
        Assert.Equal(1m, book.Bids[0].Quantity);
        Assert.Equal(0.25m, book.Asks[0].Quantity);
    }

    [Fact]
    public void GapRequiresResynchronization()
    {
        var session = new GateOrderBookSession(new OrderBookEngine());
        session.Apply(Message(true, 10, 10));

        Assert.Throws<InvalidDataException>(() =>
            session.Apply(Message(false, 12, 12)));
    }

    private static GateOrderBookMessage Message(bool snapshot, long first, long last) =>
        new(snapshot, first, new OrderBookUpdate("BTCUSDT", last, last, [], []));
}
