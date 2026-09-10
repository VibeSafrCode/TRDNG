using System.Text;
using Trdng.Mexc.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class MexcContractPublicOrderBookTests
{
    [Fact]
    public void ParsesSortsCapsAndNormalizesContractsToBaseAsset()
    {
        var snapshot = MexcContractPublicOrderBookClient.ParseDepth(
            Encoding.UTF8.GetBytes("""
              {"success":true,"code":0,"data":{"version":123,
              "bids":[["100","20",1],["101","10",1],["99","0",1]],
              "asks":[["103","5",1],["102","15",1]]}}
              """),
            "BTC_USDT", 0.0001m, 2);

        Assert.Equal(123, snapshot.UpdateId);
        Assert.Equal(101m, snapshot.Bids[0].Price);
        Assert.Equal(0.001m, snapshot.Bids[0].Quantity);
        Assert.Equal(102m, snapshot.Asks[0].Price);
        Assert.Equal(0.0015m, snapshot.Asks[0].Quantity);
        Assert.Equal(2, snapshot.Bids.Count);
        Assert.Equal(2, snapshot.Asks.Count);
    }

    [Theory]
    [InlineData("{\"success\":false,\"code\":0,\"data\":{}}")]
    [InlineData("{\"success\":true,\"code\":0,\"data\":{\"version\":1,\"bids\":[[100,1]],\"asks\":[[99,1]]}}")]
    [InlineData("{\"success\":true,\"code\":0,\"data\":{\"version\":1,\"bids\":[[100,-1]],\"asks\":[[101,1]]}}")]
    public void RejectsFailedCrossedOrInvalidDepth(string json)
    {
        Assert.Throws<InvalidDataException>(() =>
            MexcContractPublicOrderBookClient.ParseDepth(
                Encoding.UTF8.GetBytes(json), "BTC_USDT", 0.0001m, 50));
    }
}
