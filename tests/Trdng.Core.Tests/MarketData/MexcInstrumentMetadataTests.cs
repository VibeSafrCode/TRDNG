using System.Text;
using System.Globalization;
using Trdng.Mexc.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class MexcInstrumentMetadataTests
{
    [Theory]
    [InlineData("APTUSDT", "APT", 0.0001, 0.0001, 1, 600000)]
    [InlineData("BTCUSDT", "BTC", 0.01, 0.000001, 1, 4000000)]
    public void ParsesOnlyDocumentedExchangeMetadataSemantics(
        string symbol, string baseAsset, decimal tick, decimal minimumBase,
        decimal minimumMarketQuote, decimal maximumMarketQuote)
    {
        var baseText = minimumBase.ToString(CultureInfo.InvariantCulture);
        var json = $$"""{"symbols":[{"symbol":"{{symbol}}","status":"1","baseAsset":"{{baseAsset}}","quoteAsset":"USDT","quotePrecision":{{(tick == 0.01m ? 2 : 4)}},"baseSizePrecision":"{{baseText}}","isSpotTradingAllowed":true,"orderTypes":["LIMIT","MARKET"],"quoteAmountPrecisionMarket":"{{minimumMarketQuote}}","maxQuoteAmountMarket":"{{maximumMarketQuote}}","tradeSideType":1}]}""";
        var result = MexcInstrumentMetadataClient.Parse(Encoding.UTF8.GetBytes(json), symbol);
        Assert.Equal(tick, result.TickSize);
        Assert.Equal(minimumBase, result.MinimumBaseQuantity);
        Assert.Equal(minimumMarketQuote, result.MinimumMarketQuoteAmount);
        Assert.Equal(maximumMarketQuote, result.MaximumMarketQuoteAmount);
        Assert.Null(result.QuoteOrderQtyMarketAllowed); // absent in 2026-08-19 live payload
        Assert.Contains("MARKET", result.OrderTypes);
        Assert.True(result.IsSpotTradingAllowed);
    }

    [Fact]
    public void RejectsMissingRequestedSymbol() =>
        Assert.Throws<InvalidDataException>(() => MexcInstrumentMetadataClient.Parse(
            "{\"symbols\":[]}"u8.ToArray(), "BTCUSDT"));
}
