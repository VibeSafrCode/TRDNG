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
    public void GateCatalogKeepsValidPairsWhenAnotherEntryCannotBeRepresented()
    {
        var result = GateInstrumentMetadataClient.ParseCatalogResult(
            Encoding.UTF8.GetBytes("""
              [{"name":"BTC_USDT","order_price_round":"0.1","quanto_multiplier":"0.0001","in_delisting":false},
               {"name":"BAD_BASE_USDT","order_price_round":"0.1","quanto_multiplier":"1","in_delisting":false}]
              """));

        var entry = Assert.Single(result.Entries);
        Assert.Equal("BTC_USDT", entry.VenueSymbol);
        Assert.Equal(1, result.RejectedCount);
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
        Assert.Throws<InvalidDataException>(() => catalog.Replace([
            new(new("SOL", "USDT", MarketProduct.Perpetual), TradingVenue.Mexc,
                "SOL_USDT", null, 1m)]));
    }

    [Fact]
    public void MexcContractCatalogUsesOfficialPriceAndContractUnits()
    {
        var result = MexcContractInstrumentMetadataClient.ParseCatalogResult(
            Encoding.UTF8.GetBytes("""
              {"success":true,"code":0,"data":[
              {"symbol":"BTC_USDT","baseCoin":"BTC","quoteCoin":"USDT","settleCoin":"USDT","state":0,"contractSize":"0.0001","priceUnit":"0.5"},
              {"symbol":"OLD_USDT","baseCoin":"OLD","quoteCoin":"USDT","settleCoin":"USDT","state":3,"contractSize":"1","priceUnit":"0.01"},
              {"symbol":"BAD_USDT","baseCoin":"$BAD","quoteCoin":"USDT","settleCoin":"USDT","state":0,"contractSize":"1","priceUnit":"0.01"}]}
              """));

        var entry = Assert.Single(result.Entries);
        Assert.Equal(new CanonicalInstrument("BTC", "USDT", MarketProduct.Perpetual),
            entry.Instrument);
        Assert.Equal("BTC_USDT", entry.VenueSymbol);
        Assert.Equal(0.5m, entry.TickSize);
        Assert.Equal(0.0001m, entry.QuantityMultiplier);
        Assert.Equal(1, result.InvalidEligibleCount);
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

    [Fact]
    public void RefreshTimingAndReplacementAreBoundedAndFailClosed()
    {
        var now = DateTimeOffset.UnixEpoch + TimeSpan.FromHours(2);
        var loaded = now - TimeSpan.FromMinutes(10);
        var instrument = new CanonicalInstrument("BTC", "USDT", MarketProduct.Perpetual);
        VenueCatalogLoadResult Success(TradingVenue venue) => new(
            venue,
            [new(instrument, venue, venue == TradingVenue.Gate ? "BTC_USDT" : "BTCUSDT",
                0.1m)],
            null);

        Assert.False(PublicCatalogRefreshPolicy.ShouldRefresh(
            now - TimeSpan.FromMinutes(9), now, TimeSpan.FromMinutes(10)));
        Assert.True(PublicCatalogRefreshPolicy.ShouldRefresh(
            loaded, now, TimeSpan.FromMinutes(10)));
        Assert.True(PublicCatalogRefreshPolicy.ShouldRefresh(
            now + TimeSpan.FromSeconds(1), now, TimeSpan.FromMinutes(10)));

        var complete = new[]
        {
            Success(TradingVenue.Mexc),
            Success(TradingVenue.Mexc),
            Success(TradingVenue.Gate),
            Success(TradingVenue.Bybit)
        };
        Assert.Equal(PublicCatalogRefreshDecision.Replace,
            PublicCatalogRefreshPolicy.Decide(loaded, now, TimeSpan.FromMinutes(15),
                complete, 4, complete.Sum(result => result.Entries.Count)));

        var incomplete = complete[..3];
        Assert.Equal(PublicCatalogRefreshDecision.RetainFresh,
            PublicCatalogRefreshPolicy.Decide(now - TimeSpan.FromMinutes(14), now,
                TimeSpan.FromMinutes(15), incomplete, 4,
                incomplete.Sum(result => result.Entries.Count)));
        Assert.Equal(PublicCatalogRefreshDecision.RetainStale,
            PublicCatalogRefreshPolicy.Decide(now - TimeSpan.FromMinutes(16), now,
                TimeSpan.FromMinutes(15), incomplete, 4,
                incomplete.Sum(result => result.Entries.Count)));
        Assert.Equal(PublicCatalogRefreshDecision.Replace,
            PublicCatalogRefreshPolicy.Decide(null, now,
                TimeSpan.FromMinutes(15), incomplete, 4,
                incomplete.Sum(result => result.Entries.Count)));
        Assert.Equal(PublicCatalogRefreshDecision.RetainStale,
            PublicCatalogRefreshPolicy.Decide(null, now,
                TimeSpan.FromMinutes(15), [], 4, 0));
    }

    [Fact]
    public void StartupAndProductFallbackPreferBitcoinWhenAvailable()
    {
        var catalog = new PublicInstrumentCatalog();
        var apt = new CanonicalInstrument("APT", "USDT", MarketProduct.Perpetual);
        var btcPerpetual = new CanonicalInstrument("BTC", "USDT", MarketProduct.Perpetual);
        var btcSpot = new CanonicalInstrument("BTC", "USDT", MarketProduct.Spot);
        catalog.Replace([
            new(apt, TradingVenue.Bybit, "APTUSDT", 0.0001m),
            new(btcPerpetual, TradingVenue.Bybit, "BTCUSDT", 0.1m),
            new(btcSpot, TradingVenue.Mexc, "BTCUSDT", null)]);

        Assert.Equal(btcPerpetual, CatalogSelectionPolicy.ChooseInitial(catalog));
        Assert.Equal(btcSpot, CatalogSelectionPolicy.ChooseForProduct(
            catalog, apt, MarketProduct.Spot));
    }

    [Fact]
    public void ReconciliationBootstrapsRebuildsOrFailsClosedExactly()
    {
        var instrument = new CanonicalInstrument("BTC", "USDT", MarketProduct.Perpetual);
        var oldEntry = new PublicCatalogEntry(
            instrument, TradingVenue.Bybit, "BTCUSDT", 0.1m);
        var newEntry = oldEntry with { TickSize = 0.01m };
        PublicCatalogEntry?[] empty = [null, null, null];

        Assert.Equal(PublicCatalogReconciliationAction.None,
            PublicCatalogReconciliationPolicy.Decide(
                initialLoad: true, active: null, empty, empty));
        Assert.Equal(PublicCatalogReconciliationAction.Bootstrap,
            PublicCatalogReconciliationPolicy.Decide(
                initialLoad: false, active: null, empty, [newEntry, null, null]));
        Assert.Equal(PublicCatalogReconciliationAction.None,
            PublicCatalogReconciliationPolicy.Decide(
                initialLoad: false, instrument, [oldEntry, null, null],
                [oldEntry, null, null]));
        Assert.Equal(PublicCatalogReconciliationAction.RebuildActive,
            PublicCatalogReconciliationPolicy.Decide(
                initialLoad: false, instrument, [oldEntry, null, null],
                [newEntry, null, null]));
        Assert.Equal(PublicCatalogReconciliationAction.FailClosedActive,
            PublicCatalogReconciliationPolicy.Decide(
                initialLoad: false, instrument, [oldEntry, null, null], empty));
    }

    [Fact]
    public void PresentationDoesNotOverwriteLoadingOrErrorWithoutObservation()
    {
        var now = DateTimeOffset.UnixEpoch + TimeSpan.FromHours(1);
        Assert.Equal("КАТАЛОГ · ЗАГРУЗКА", CatalogPresentationPolicy.PreserveOrMarkStale(
            "КАТАЛОГ · ЗАГРУЗКА", null, now, TimeSpan.FromMinutes(15)));
        Assert.Equal("КАТАЛОГ · ОШИБКА", CatalogPresentationPolicy.PreserveOrMarkStale(
            "КАТАЛОГ · ОШИБКА", null, now, TimeSpan.FromMinutes(15)));
        Assert.Equal("КАТАЛОГ · ЧАСТИЧНО ДОСТУПЕН",
            CatalogPresentationPolicy.PreserveOrMarkStale("КАТАЛОГ · ЧАСТИЧНО ДОСТУПЕН",
                now, now + TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15)));
        Assert.Equal("КАТАЛОГ · УСТАРЕЛ", CatalogPresentationPolicy.PreserveOrMarkStale(
            "КАТАЛОГ · ГОТОВ", now, now + TimeSpan.FromMinutes(16),
            TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public async Task ExpectedVenueParserFailureIsIsolatedAndUnionStillStarts()
    {
        var spot = new CanonicalInstrument("SOL", "USDT", MarketProduct.Spot);
        var failed = PublicCatalogLoadIsolation.LoadAsync(TradingVenue.Gate,
            () => throw new ArgumentException("synthetic malformed venue entry"));
        var valid = PublicCatalogLoadIsolation.LoadAsync(TradingVenue.Mexc,
            () => Task.FromResult<IReadOnlyList<PublicCatalogEntry>>([
                new(spot, TradingVenue.Mexc, "SOLUSDT", null)]));
        var results = await Task.WhenAll(failed, valid);

        Assert.Equal("INVALID_METADATA", results[0].FailureCategory);
        Assert.True(results[1].Succeeded);
        var catalog = new PublicInstrumentCatalog();
        catalog.Replace(results.SelectMany(result => result.Entries));
        Assert.Equal(spot, CatalogSelectionPolicy.ChooseInitial(catalog));
    }

    [Fact]
    public void MexcCatalogRejectsEntriesIndependentlyWithTypedCounts()
    {
        var result = MexcInstrumentMetadataClient.ParseCatalogResult(Encoding.UTF8.GetBytes("""
          {"symbols":[
          {"symbol":"SOLUSDT","status":"1","baseAsset":"SOL","quoteAsset":"USDT","isSpotTradingAllowed":true},
          {"symbol":"OFFUSDT","status":"3","baseAsset":"OFF","quoteAsset":"USDT","isSpotTradingAllowed":true},
          {"symbol":"BADUSDT","status":"1","baseAsset":"$BAD","quoteAsset":"USDT","isSpotTradingAllowed":true},
          {"symbol":"MISSUSDT","status":"1","quoteAsset":"USDT","isSpotTradingAllowed":true},
          {"symbol":"BAD SYMBOL","status":"1","baseAsset":"BAD","quoteAsset":"USDT","isSpotTradingAllowed":true},
          {"symbol":"BTCUSDT","status":"1","baseAsset":"BTC","quoteAsset":"USDT","isSpotTradingAllowed":true},
          {"symbol":"SOLUSDT2","status":"1","baseAsset":"SOL","quoteAsset":"USDT","isSpotTradingAllowed":true}]}
          """));

        Assert.Single(result.Entries);
        Assert.Equal("BTCUSDT", result.Entries[0].VenueSymbol);
        Assert.Equal(6, result.RejectedCount);
        Assert.Equal(5, result.InvalidEligibleCount);
        Assert.Equal(1, result.Rejections[MexcCatalogRejection.Ineligible]);
        Assert.Equal(1, result.Rejections[MexcCatalogRejection.InvalidCanonicalAsset]);
        Assert.Equal(1, result.Rejections[MexcCatalogRejection.MissingOrWrongRequiredField]);
        Assert.Equal(1, result.Rejections[MexcCatalogRejection.InvalidOther]);
        Assert.Equal(2, result.Rejections[MexcCatalogRejection.DuplicateOrConflict]);
    }

    [Fact]
    public void MexcCatalogRejectsInvalidRootAndCanReportAllInvalid()
    {
        Assert.Throws<InvalidDataException>(() =>
            MexcInstrumentMetadataClient.ParseCatalogResult(
                Encoding.UTF8.GetBytes("{\"symbols\":{}}")));
        var allInvalid = MexcInstrumentMetadataClient.ParseCatalogResult(
            Encoding.UTF8.GetBytes("""
              {"symbols":[{"symbol":"OFFUSDT","status":"3","baseAsset":"OFF",
              "quoteAsset":"USDT","isSpotTradingAllowed":false}]}
              """));
        Assert.Empty(allInvalid.Entries);
        Assert.Equal(1, allInvalid.RejectedCount);
    }

    [Fact]
    public async Task SuccessWithRejectionsIsDistinctFromCleanSuccess()
    {
        var spot = new CanonicalInstrument("SOL", "USDT", MarketProduct.Spot);
        var partial = await PublicCatalogLoadIsolation.LoadBatchAsync(TradingVenue.Mexc,
            () => Task.FromResult(new PublicCatalogBatch([
                new(spot, TradingVenue.Mexc, "SOLUSDT", null)], 2)));
        Assert.True(partial.Succeeded);
        Assert.True(partial.HasRejections);
        Assert.Equal(2, partial.RejectedCount);
    }

    [Fact]
    public void ExactDuplicateRetainsMappingButDifferentSymbolRemovesPairPermanently()
    {
        var exact = MexcInstrumentMetadataClient.ParseCatalogResult(Encoding.UTF8.GetBytes("""
          {"symbols":[
          {"symbol":"SOLUSDT","status":"1","baseAsset":"SOL","quoteAsset":"USDT","isSpotTradingAllowed":true},
          {"symbol":"SOLUSDT","status":"1","baseAsset":"SOL","quoteAsset":"USDT","isSpotTradingAllowed":true},
          {"symbol":"BTCUSDT","status":"1","baseAsset":"BTC","quoteAsset":"USDT","isSpotTradingAllowed":true}]}
          """));
        Assert.Contains(exact.Entries, entry => entry.VenueSymbol == "SOLUSDT");

        var conflict = MexcInstrumentMetadataClient.ParseCatalogResult(Encoding.UTF8.GetBytes("""
          {"symbols":[
          {"symbol":"SOLUSDT","status":"1","baseAsset":"SOL","quoteAsset":"USDT","isSpotTradingAllowed":true},
          {"symbol":"SOL-USDT","status":"1","baseAsset":"SOL","quoteAsset":"USDT","isSpotTradingAllowed":true},
          {"symbol":"SOLUSDT3","status":"1","baseAsset":"SOL","quoteAsset":"USDT","isSpotTradingAllowed":true},
          {"symbol":"BTCUSDT","status":"1","baseAsset":"BTC","quoteAsset":"USDT","isSpotTradingAllowed":true}]}
          """));
        Assert.DoesNotContain(conflict.Entries, entry => entry.Instrument.BaseAsset == "SOL");
        Assert.Contains(conflict.Entries, entry => entry.VenueSymbol == "BTCUSDT");
        Assert.Equal(3, conflict.Rejections[MexcCatalogRejection.DuplicateOrConflict]);
    }

    [Fact]
    public void IneligibleOnlyDoesNotMarkEligibleCatalogAsPartial()
    {
        var result = MexcInstrumentMetadataClient.ParseCatalogResult(Encoding.UTF8.GetBytes("""
          {"symbols":[
          {"symbol":"SOLUSDT","status":"1","baseAsset":"SOL","quoteAsset":"USDT","isSpotTradingAllowed":true},
          {"symbol":"OFFUSDT","status":"3","baseAsset":"OFF","quoteAsset":"USDT","isSpotTradingAllowed":false}]}
          """));
        Assert.Equal(1, result.RejectedCount);
        Assert.Equal(0, result.InvalidEligibleCount);
    }
}
