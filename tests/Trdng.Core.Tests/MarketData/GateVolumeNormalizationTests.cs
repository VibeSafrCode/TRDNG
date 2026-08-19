using System.Text;
using Trdng.Gate.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class GateVolumeNormalizationTests
{
    [Fact]
    public void AptContractsAreNormalizedToAptUnits()
    {
        var json = """
        {"channel":"futures.obu","event":"update","result":{
          "full":true,"s":"ob.APT_USDT.50","u":10,
          "b":[["5.800","25"]],"a":[["5.801","12"]]}}
        """;

        var parsed = GateOrderBookMessageParser.TryParse(
            Encoding.UTF8.GetBytes(json),
            out var message,
            contractMultiplier: 0.1m);

        Assert.True(parsed);
        Assert.Equal(2.5m, message!.Update.Bids[0].Quantity);
        Assert.Equal(1.2m, message.Update.Asks[0].Quantity);
    }

    [Fact]
    public void ZeroContractQuantityRemainsDeletionMarker()
    {
        var json = """
        {"channel":"futures.obu","event":"update","result":{
          "s":"ob.APT_USDT.50","U":11,"u":11,
          "b":[["5.800","0"]]}}
        """;

        GateOrderBookMessageParser.TryParse(
            Encoding.UTF8.GetBytes(json),
            out var message,
            contractMultiplier: 0.1m);

        Assert.Equal(0m, message!.Update.Bids[0].Quantity);
    }
}
