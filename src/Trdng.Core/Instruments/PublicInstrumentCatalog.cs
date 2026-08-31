namespace Trdng.Core.Instruments;

public sealed record PublicCatalogEntry(
    CanonicalInstrument Instrument,
    TradingVenue Venue,
    string VenueSymbol,
    decimal? TickSize,
    decimal? QuantityMultiplier = null);

public sealed class PublicInstrumentCatalog
{
    public const int MaxSearchResults = 40;
    private readonly object _sync = new();
    private IReadOnlyDictionary<(CanonicalInstrument, TradingVenue), PublicCatalogEntry> _entries =
        new Dictionary<(CanonicalInstrument, TradingVenue), PublicCatalogEntry>();

    public void Replace(IEnumerable<PublicCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var next = new Dictionary<(CanonicalInstrument, TradingVenue), PublicCatalogEntry>();
        foreach (var entry in entries)
        {
            if (!Enum.IsDefined(entry.Venue) || string.IsNullOrWhiteSpace(entry.VenueSymbol) ||
                entry.VenueSymbol.Any(char.IsWhiteSpace) ||
                (entry.TickSize is not null && entry.TickSize <= 0) ||
                (entry.QuantityMultiplier is not null && entry.QuantityMultiplier <= 0) ||
                ((entry.Venue is TradingVenue.Bybit or TradingVenue.Gate ||
                  entry.Venue == TradingVenue.Mexc &&
                  entry.Instrument.Product == MarketProduct.Perpetual) &&
                 entry.TickSize is null))
                throw new InvalidDataException("Public catalog entry is invalid.");
            var key = (entry.Instrument, entry.Venue);
            if (!next.TryAdd(key, entry) && next[key] != entry)
                throw new InvalidDataException("Conflicting official venue symbols.");
        }
        lock (_sync) _entries = next;
    }

    public VenueInstrumentCapability? Find(CanonicalInstrument instrument, TradingVenue venue)
    {
        PublicCatalogEntry? entry;
        lock (_sync) _entries.TryGetValue((instrument, venue), out entry);
        if (entry is null) return null;
        return new(entry.Instrument, entry.Venue, entry.VenueSymbol,
            CapabilityAvailability.Available, CapabilityAvailability.NotImplemented);
    }

    public decimal? TickSize(CanonicalInstrument instrument, TradingVenue venue)
    {
        lock (_sync) return _entries.TryGetValue((instrument, venue), out var entry)
            ? entry.TickSize : null;
    }

    public PublicCatalogEntry? Get(CanonicalInstrument instrument, TradingVenue venue)
    {
        lock (_sync) return _entries.TryGetValue((instrument, venue), out var entry)
            ? entry : null;
    }

    public IReadOnlyList<PublicCatalogEntry> Snapshot()
    {
        lock (_sync) return _entries.Values.ToArray();
    }

    public IReadOnlyList<CanonicalInstrument> Search(MarketProduct product, string query,
        int limit = MaxSearchResults)
    {
        if (limit is < 1 or > MaxSearchResults) throw new ArgumentOutOfRangeException(nameof(limit));
        var normalized = (query ?? string.Empty).Trim().ToUpperInvariant();
        CanonicalInstrument[] instruments;
        lock (_sync) instruments = _entries.Values
            .Where(entry => entry.Instrument.Product == product)
            .Select(entry => entry.Instrument).Distinct().ToArray();
        return instruments
            .Where(instrument => normalized.Length == 0 ||
                instrument.PairId.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                $"{instrument.BaseAsset}{instrument.QuoteAsset}".Contains(normalized,
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(instrument => instrument.QuoteAsset == "USDT" ? 0 : 1)
            .ThenBy(instrument => instrument.BaseAsset)
            .ThenBy(instrument => instrument.QuoteAsset)
            .Take(limit).ToArray();
    }
}

public static class PublicCatalogFreshness
{
    public static bool IsFresh(DateTimeOffset? loadedAt, DateTimeOffset now, TimeSpan maxAge) =>
        maxAge > TimeSpan.Zero && loadedAt is { } loaded && now >= loaded && now - loaded <= maxAge;
}

public enum PublicCatalogRefreshDecision
{
    Replace,
    RetainFresh,
    RetainStale
}

public enum PublicCatalogReconciliationAction
{
    None,
    Bootstrap,
    RebuildActive,
    FailClosedActive
}

public static class PublicCatalogReconciliationPolicy
{
    public static PublicCatalogReconciliationAction Decide(
        bool initialLoad,
        CanonicalInstrument? active,
        IReadOnlyList<PublicCatalogEntry?> previous,
        IReadOnlyList<PublicCatalogEntry?> current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        if (previous.Count != current.Count)
            throw new ArgumentException("Catalog mappings must have equal lengths.");
        if (active is null)
            return initialLoad
                ? PublicCatalogReconciliationAction.None
                : PublicCatalogReconciliationAction.Bootstrap;
        if (previous.SequenceEqual(current))
            return PublicCatalogReconciliationAction.None;
        return current.All(entry => entry is null)
            ? PublicCatalogReconciliationAction.FailClosedActive
            : PublicCatalogReconciliationAction.RebuildActive;
    }
}

public static class PublicCatalogRefreshPolicy
{
    public static bool ShouldRefresh(
        DateTimeOffset? loadedAt,
        DateTimeOffset now,
        TimeSpan refreshAfter)
    {
        if (refreshAfter <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(refreshAfter));
        return loadedAt is not { } loaded || now < loaded || now - loaded >= refreshAfter;
    }

    public static PublicCatalogRefreshDecision Decide(
        DateTimeOffset? loadedAt,
        DateTimeOffset now,
        TimeSpan maxAge,
        IReadOnlyList<VenueCatalogLoadResult> results,
        int expectedSourceCount,
        int candidateEntryCount)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (maxAge <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxAge));
        if (expectedSourceCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedSourceCount));
        if (candidateEntryCount < 0)
            throw new ArgumentOutOfRangeException(nameof(candidateEntryCount));
        if (loadedAt is null && candidateEntryCount > 0)
            return PublicCatalogRefreshDecision.Replace;
        if (results.Count == expectedSourceCount &&
            results.All(result => result.Succeeded && result.Entries.Count != 0))
            return PublicCatalogRefreshDecision.Replace;
        return PublicCatalogFreshness.IsFresh(loadedAt, now, maxAge)
            ? PublicCatalogRefreshDecision.RetainFresh
            : PublicCatalogRefreshDecision.RetainStale;
    }
}

public static class CatalogPresentationPolicy
{
    public static string PreserveOrMarkStale(string current, DateTimeOffset? loadedAt,
        DateTimeOffset now, TimeSpan maxAge) =>
        loadedAt is not null && !PublicCatalogFreshness.IsFresh(loadedAt, now, maxAge)
            ? "КАТАЛОГ · УСТАРЕЛ" : current;
}

public sealed record VenueCatalogLoadResult(
    TradingVenue Venue,
    IReadOnlyList<PublicCatalogEntry> Entries,
    string? FailureCategory,
    int RejectedCount = 0)
{
    public bool Succeeded => FailureCategory is null;
    public bool HasRejections => RejectedCount > 0;
}

public sealed record PublicCatalogBatch(
    IReadOnlyList<PublicCatalogEntry> Entries,
    int RejectedCount);

public static class PublicCatalogLoadIsolation
{
    public static async Task<VenueCatalogLoadResult> LoadAsync(TradingVenue venue,
        Func<Task<IReadOnlyList<PublicCatalogEntry>>> loader)
    {
        try { return new(venue, await loader().ConfigureAwait(false), null); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
            InvalidDataException or System.Text.Json.JsonException or FormatException or
            ArgumentException or OverflowException)
        {
            return new(venue, [], exception switch
            {
                HttpRequestException or TaskCanceledException => "NETWORK",
                System.Text.Json.JsonException or InvalidDataException or FormatException or
                    ArgumentException or OverflowException => "INVALID_METADATA",
                _ => "ERROR"
            });
        }
    }


    public static async Task<VenueCatalogLoadResult> LoadBatchAsync(TradingVenue venue,
        Func<Task<PublicCatalogBatch>> loader)
    {
        try
        {
            var batch = await loader().ConfigureAwait(false);
            if (batch.Entries.Count == 0)
                return new(venue, [], "INVALID_METADATA", batch.RejectedCount);
            return new(venue, batch.Entries, null, batch.RejectedCount);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
            InvalidDataException or System.Text.Json.JsonException or FormatException or
            ArgumentException or OverflowException)
        {
            return new(venue, [], exception is HttpRequestException or TaskCanceledException
                ? "NETWORK" : "INVALID_METADATA");
        }
    }
}

public static class CatalogSelectionPolicy
{
    public static CanonicalInstrument? ChooseInitial(PublicInstrumentCatalog catalog)
    {
        var bitcoin = new CanonicalInstrument("BTC", "USDT", MarketProduct.Perpetual);
        if (catalog.Find(bitcoin, TradingVenue.Bybit) is not null ||
            catalog.Find(bitcoin, TradingVenue.Gate) is not null) return bitcoin;
        return catalog.Search(MarketProduct.Perpetual, string.Empty, 1).FirstOrDefaultNullable()
            ?? catalog.Search(MarketProduct.Spot, string.Empty, 1).FirstOrDefaultNullable();
    }

    public static CanonicalInstrument? ChooseForProduct(PublicInstrumentCatalog catalog,
        CanonicalInstrument current, MarketProduct target)
    {
        var same = new CanonicalInstrument(current.BaseAsset, current.QuoteAsset, target);
        if (HasMarket(catalog, same)) return same;
        var bitcoin = new CanonicalInstrument("BTC", "USDT", target);
        if (HasMarket(catalog, bitcoin)) return bitcoin;
        return catalog.Search(target, string.Empty, 1).FirstOrDefaultNullable();
    }

    private static bool HasMarket(PublicInstrumentCatalog catalog, CanonicalInstrument instrument) =>
        Enum.GetValues<TradingVenue>().Any(venue => catalog.Find(instrument, venue) is not null);

    private static CanonicalInstrument? FirstOrDefaultNullable(
        this IReadOnlyList<CanonicalInstrument> instruments) =>
        instruments.Count == 0 ? null : instruments[0];
}
