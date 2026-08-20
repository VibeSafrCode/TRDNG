using System.Text;
using Trdng.Bybit.MarketData;
using Trdng.Core.Instruments;
using Trdng.Gate.MarketData;
using Trdng.Mexc.MarketData;

namespace Trdng.Core.Tests.Instruments;

public sealed class PublicInstrumentCatalogTests
{
    [Fact]
    public void NormalizesUnionKeepsProductsAndExactVenueSymbolsIsolated()
    {
        var catalog = new PublicInstrumentCatalog();
        var spot = new CanonicalInstrument("sol", "usdt", MarketProduct.Spot);
        var perp = new CanonicalInstrument("SOL", "USDT", MarketProduct.Perpetual);
        catalog.Replace([
            new(spot, TradingVenue.Mexc, "SOLUSDT", 0.001m),
            new(perp, TradingVenue.Gate, "SOL_USDT", 0.001m, 1m),
            new(perp, TradingVenue.Bybit, "SOLUSDT", 0.001m)]);

        Assert.Equal("SOLUSDT", catalog.Find(spot, TradingVenue.Mexc)!.VenueSymbol);
        Assert.Null(catalog.Find(spot, TradingVenue.Gate));
        Assert.Equal("SOL_USDT", catalog.Find(perp, TradingVenue.Gate)!.VenueSymbol);
        Assert.Equal("SOLUSDT", catalog.Find(perp, TradingVenue.Bybit)!.VenueSymbol);
    }

    [Fact]
    public void SearchIsBoundedAndProductIsolated()
    {
        var catalog = new PublicInstrumentCatalog();
        catalog.Replace(Enumerable.Range(0, 100).Select(index => new PublicCatalogEntry(
            new($"A{index}", "USDT", MarketProduct.Spot), TradingVenue.Mexc,
            $"A{index}USDT", 0.01m)));
        Assert.Equal(PublicInstrumentCatalog.MaxSearchResults,
            catalog.Search(MarketProduct.Spot, string.Empty).Count);
        Assert.Empty(catalog.Search(MarketProduct.Perpetual, string.Empty));
        Assert.Single(catalog.Search(MarketProduct.Spot, "A99/USDT"));
    }

    [Fact]
    public void OfficialShapeParsersExcludeUnsupportedAndPreserveExactSymbols()
    {
        var bybit = BybitInstrumentMetadataClient.ParseCatalogPage(Encoding.UTF8.GetBytes("""
          {"retCode":0,"result":{"list":[
          {"symbol":"SOLUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"SOL","quoteCoin":"USDT","settleCoin":"USDT","priceFilter":{"tickSize":"0.001"}},
          {"symbol":"BADUSDT","contractType":"LinearFutures","status":"Trading","baseCoin":"BAD","quoteCoin":"USDT","settleCoin":"USDT","priceFilter":{"tickSize":"1"}}],"nextPageCursor":"next"}}
          """));
        Assert.Single(bybit.Entries);
        Assert.Equal("next", bybit.NextCursor);

        var gate = GateInstrumentMetadataClient.ParseCatalog(Encoding.UTF8.GetBytes("""
          [{"name":"SOL_USDT","order_price_round":"0.001","quanto_multiplier":"1","in_delisting":false},
           {"name":"OLD_USDT","order_price_round":"1","quanto_multiplier":"1","in_delisting":true}]
          """));
        Assert.Single(gate);
        Assert.Equal("SOL_USDT", gate[0].VenueSymbol);

        var mexc = MexcInstrumentMetadataClient.ParseCatalog(Encoding.UTF8.GetBytes("""
          {"symbols":[
          {"symbol":"SOLUSDT","status":"1","baseAsset":"SOL","quoteAsset":"USDT","quotePrecision":3,"isSpotTradingAllowed":true},
          {"symbol":"OFFUSDT","status":"3","baseAsset":"OFF","quoteAsset":"USDT","quotePrecision":2,"isSpotTradingAllowed":false}]}
          """));
        Assert.Single(mexc);
        Assert.Equal("SOLUSDT", mexc[0].VenueSymbol);
        Assert.Null(mexc[0].TickSize);
    }

    [Fact]
    public void GateRequiresExactDelistingBoolean()
    {
        Assert.Throws<InvalidDataException>(() => GateInstrumentMetadataClient.ParseCatalog(
            Encoding.UTF8.GetBytes("[{\"name\":\"SOL_USDT\",\"order_price_round\":\".01\",\"quanto_multiplier\":\"1\"}]")));
        Assert.Throws<InvalidDataException>(() => GateInstrumentMetadataClient.ParseCatalog(
            Encoding.UTF8.GetBytes("[{\"name\":\"SOL_USDT\",\"order_price_round\":\".01\",\"quanto_multiplier\":\"1\",\"in_delisting\":\"false\"}]")));
    }

    [Fact]
    public void ValidationAllowsMexcWithoutTickButRejectsBadOptionalMultiplier()
    {
        var catalog = new PublicInstrumentCatalog();
        var spot = new CanonicalInstrument("SOL", "USDT", MarketProduct.Spot);
        catalog.Replace([new(spot, TradingVenue.Mexc, "SOLUSDT", null)]);
        Assert.NotNull(catalog.Find(spot, TradingVenue.Mexc));
        Assert.Throws<InvalidDataException>(() => catalog.Replace([
            new(spot, TradingVenue.Mexc, "SOLUSDT", null, 0m)]));
        Assert.Throws<InvalidDataException>(() => catalog.Replace([
            new(new("SOL", "USDT", MarketProduct.Perpetual), TradingVenue.Bybit,
                "SOLUSDT", null)]));
    }

    [Fact]
    public void FreshnessAndStartupChooserAreFailClosedAndSupportPartialCatalogs()
    {
        var now = DateTimeOffset.UnixEpoch + TimeSpan.FromHours(1);
        Assert.True(PublicCatalogFreshness.IsFresh(now, now, TimeSpan.FromMinutes(15)));
        Assert.False(PublicCatalogFreshness.IsFresh(now, now + TimeSpan.FromMinutes(16),
            TimeSpan.FromMinutes(15)));
        Assert.False(PublicCatalogFreshness.IsFresh(null, now, TimeSpan.FromMinutes(15)));

        var catalog = new PublicInstrumentCatalog();
        var spot = new CanonicalInstrument("SOL", "USDT", MarketProduct.Spot);
        catalog.Replace([new(spot, TradingVenue.Mexc, "SOLUSDT", null)]);
        Assert.Equal(spot, CatalogSelectionPolicy.ChooseInitial(catalog));
        Assert.Equal(spot, CatalogSelectionPolicy.ChooseForProduct(catalog,
            new("APT", "USDT", MarketProduct.Perpetual), MarketProduct.Spot));
        Assert.Null(CatalogSelectionPolicy.ChooseForProduct(catalog, spot,
            MarketProduct.Perpetual));
    }
}
