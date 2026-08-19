using System.Text;
using Trdng.Bybit.MarketData;
using Trdng.Core.MarketData;
using Trdng.Gate.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class InstrumentMetadataTests
{
    [Fact]
    public void ParsesBybitLinearTickSize()
    {
        var json = """
        {"retCode":0,"retMsg":"OK","result":{"category":"linear","list":[{
          "symbol":"APTUSDT","priceFilter":{"tickSize":"0.001"}
        }]}}
        """;

        var tick = BybitInstrumentMetadataClient.ParseTickSize(
            Encoding.UTF8.GetBytes(json),
            "APTUSDT");

        Assert.Equal(0.001m, tick);
    }

    [Fact]
    public void ParsesGateOrderPriceRound()
    {
        var json = """
        [{"name":"APT_USDT","order_price_round":"0.001"}]
        """;

        var tick = GateInstrumentMetadataClient.ParseTickSize(
            Encoding.UTF8.GetBytes(json),
            "APT_USDT");

        Assert.Equal(0.001m, tick);
    }

    [Fact]
    public void ResolvesCompatibleVenueTicksToFinestOfficialGrid()
    {
        Assert.Equal(0.001m, InstrumentTickSize.Resolve(0.001m, 0.002m));
    }

    [Fact]
    public void RejectsIncompatibleVenueTicksForSafeFallback()
    {
        Assert.Null(InstrumentTickSize.Resolve(0.002m, 0.003m));
    }

    [Fact]
    public void RejectsMissingInstrumentInsteadOfGuessing()
    {
        var json = """
        {"retCode":0,"retMsg":"OK","result":{"category":"linear","list":[]}}
        """;

        Assert.Throws<InvalidDataException>(() =>
            BybitInstrumentMetadataClient.ParseTickSize(
                Encoding.UTF8.GetBytes(json),
                "APTUSDT"));
    }
}
