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
                (entry.Venue is TradingVenue.Bybit or TradingVenue.Gate && entry.TickSize is null))
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

public static class CatalogSelectionPolicy
{
    public static CanonicalInstrument? ChooseInitial(PublicInstrumentCatalog catalog)
    {
        var apt = new CanonicalInstrument("APT", "USDT", MarketProduct.Perpetual);
        if (catalog.Find(apt, TradingVenue.Bybit) is not null ||
            catalog.Find(apt, TradingVenue.Gate) is not null) return apt;
        return catalog.Search(MarketProduct.Perpetual, string.Empty, 1).FirstOrDefaultNullable()
            ?? catalog.Search(MarketProduct.Spot, string.Empty, 1).FirstOrDefaultNullable();
    }

    public static CanonicalInstrument? ChooseForProduct(PublicInstrumentCatalog catalog,
        CanonicalInstrument current, MarketProduct target)
    {
        var same = new CanonicalInstrument(current.BaseAsset, current.QuoteAsset, target);
        if (HasMarket(catalog, same)) return same;
        var apt = new CanonicalInstrument("APT", "USDT", target);
        if (HasMarket(catalog, apt)) return apt;
        return catalog.Search(target, string.Empty, 1).FirstOrDefaultNullable();
    }

    private static bool HasMarket(PublicInstrumentCatalog catalog, CanonicalInstrument instrument) =>
        Enum.GetValues<TradingVenue>().Any(venue => catalog.Find(instrument, venue) is not null);

    private static CanonicalInstrument? FirstOrDefaultNullable(
        this IReadOnlyList<CanonicalInstrument> instruments) =>
        instruments.Count == 0 ? null : instruments[0];
}
