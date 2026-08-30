using System.Globalization;
using System.Text.Json;
using Trdng.Core.Instruments;
using Trdng.Core.MarketData;

namespace Trdng.Mexc.MarketData;

public enum MexcCatalogRejection
{
    Ineligible,
    InvalidCanonicalAsset,
    MissingOrWrongRequiredField,
    InvalidOther,
    DuplicateOrConflict
}

public sealed record MexcCatalogParseResult(
    IReadOnlyList<PublicCatalogEntry> Entries,
    IReadOnlyDictionary<MexcCatalogRejection, int> Rejections)
{
    public int RejectedCount => Rejections.Values.Sum();
    public int InvalidEligibleCount => Rejections
        .Where(item => item.Key != MexcCatalogRejection.Ineligible)
        .Sum(item => item.Value);
}

public sealed class MexcInstrumentMetadataClient(HttpClient httpClient)
{
    internal const int MaximumExchangeInfoBytes = 8 * 1024 * 1024;
    private static readonly Uri Endpoint =
        new("https://api.mexc.com/api/v3/exchangeInfo");

    public async Task<MexcInstrumentMetadata> GetSpotAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var normalized = symbol.ToUpperInvariant();
        var json = await BoundedHttpContentReader.GetJsonBytesAsync(
            httpClient,
            new Uri($"{Endpoint}?symbol={Uri.EscapeDataString(normalized)}"),
            MaximumExchangeInfoBytes,
            cancellationToken).ConfigureAwait(false);
        return Parse(json, normalized);
    }

    public async Task<IReadOnlyList<PublicCatalogEntry>> GetSpotCatalogAsync(
        CancellationToken cancellationToken = default)
        => (await GetSpotCatalogResultAsync(cancellationToken).ConfigureAwait(false)).Entries;

    public async Task<MexcCatalogParseResult> GetSpotCatalogResultAsync(
        CancellationToken cancellationToken = default)
    {
        var json = await BoundedHttpContentReader.GetJsonBytesAsync(
            httpClient, Endpoint, MaximumExchangeInfoBytes, cancellationToken)
            .ConfigureAwait(false);
        var result = ParseCatalogResult(json);
        if (result.Entries.Count == 0)
            throw new InvalidDataException("MEXC catalog contains no valid eligible entries.");
        return result;
    }

    public static IReadOnlyList<PublicCatalogEntry> ParseCatalog(ReadOnlyMemory<byte> utf8Json)
        => ParseCatalogResult(utf8Json).Entries;

    public static MexcCatalogParseResult ParseCatalogResult(ReadOnlyMemory<byte> utf8Json)
    {
        using var document = JsonDocument.Parse(utf8Json);
        if (!document.RootElement.TryGetProperty("symbols", out var symbols) ||
            symbols.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("MEXC symbols root must be an array.");
        var entries = new List<PublicCatalogEntry>();
        var counts = new Dictionary<MexcCatalogRejection, int>();
        var exact = new Dictionary<CanonicalInstrument, string>();
        var conflicts = new HashSet<CanonicalInstrument>();
        foreach (var item in symbols.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !TryRequiredString(item, "symbol", out var symbol) ||
                !TryRequiredString(item, "baseAsset", out var baseAsset) ||
                !TryRequiredString(item, "quoteAsset", out var quoteAsset) ||
                !TryRequiredScalarString(item, "status", out var status) ||
                !item.TryGetProperty("isSpotTradingAllowed", out var allowed) ||
                allowed.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                Add(counts, MexcCatalogRejection.MissingOrWrongRequiredField);
                continue;
            }
            if (!allowed.GetBoolean() || status is not ("1" or "ENABLED"))
            {
                Add(counts, MexcCatalogRejection.Ineligible);
                continue;
            }
            if (!IsCanonicalAsset(baseAsset) || !IsCanonicalAsset(quoteAsset))
            {
                Add(counts, MexcCatalogRejection.InvalidCanonicalAsset);
                continue;
            }
            if (symbol.Any(char.IsWhiteSpace))
            {
                Add(counts, MexcCatalogRejection.InvalidOther);
                continue;
            }
            CanonicalInstrument instrument;
            try { instrument = new(baseAsset, quoteAsset, MarketProduct.Spot); }
            catch (ArgumentException)
            {
                Add(counts, MexcCatalogRejection.InvalidCanonicalAsset);
                continue;
            }
            if (conflicts.Contains(instrument))
            {
                Add(counts, MexcCatalogRejection.DuplicateOrConflict);
                continue;
            }
            if (exact.TryGetValue(instrument, out var existing))
            {
                if (existing == symbol)
                {
                    Add(counts, MexcCatalogRejection.DuplicateOrConflict);
                    continue;
                }
                entries.RemoveAll(entry => entry.Instrument == instrument);
                exact.Remove(instrument);
                conflicts.Add(instrument);
                Add(counts, MexcCatalogRejection.DuplicateOrConflict, 2);
                continue;
            }
            exact.Add(instrument, symbol);
            entries.Add(new(instrument, TradingVenue.Mexc, symbol, null));
        }
        return new(entries, counts);
    }

    private static bool TryRequiredString(JsonElement item, string name, out string value)
    {
        value = string.Empty;
        return item.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            property.GetString() is { Length: > 0 } text && (value = text).Length > 0;
    }

    private static bool TryRequiredScalarString(JsonElement item, string name, out string value)
    {
        value = string.Empty;
        if (!item.TryGetProperty(name, out var property)) return false;
        if (property.ValueKind == JsonValueKind.String && property.GetString() is { Length: > 0 } text)
        { value = text; return true; }
        if (property.ValueKind == JsonValueKind.Number)
        { value = property.GetRawText(); return value.Length > 0; }
        return false;
    }

    private static bool IsCanonicalAsset(string value) =>
        value.All(char.IsAsciiLetterOrDigit);

    private static void Add(Dictionary<MexcCatalogRejection, int> counts,
        MexcCatalogRejection rejection, int amount = 1) =>
        counts[rejection] = counts.GetValueOrDefault(rejection) + amount;

    public static MexcInstrumentMetadata Parse(
        ReadOnlyMemory<byte> utf8Json,
        string expectedSymbol)
    {
        using var document = JsonDocument.Parse(utf8Json);
        foreach (var item in document.RootElement.GetProperty("symbols").EnumerateArray())
        {
            var symbol = RequiredString(item, "symbol");
            if (!string.Equals(symbol, expectedSymbol, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var quotePrecision = item.GetProperty("quotePrecision").GetInt32();
            if (quotePrecision is < 0 or > 28)
            {
                throw new InvalidDataException("MEXC quotePrecision is outside decimal range.");
            }

            var tickSize = DecimalScale(quotePrecision);
            return new MexcInstrumentMetadata(
                symbol,
                RequiredString(item, "baseAsset"),
                RequiredString(item, "quoteAsset"),
                tickSize,
                RequiredScalar(item, "status"),
                item.GetProperty("isSpotTradingAllowed").GetBoolean(),
                item.TryGetProperty("orderTypes", out var orderTypes) &&
                    orderTypes.ValueKind == JsonValueKind.Array
                    ? orderTypes.EnumerateArray().Select(value => value.GetString() ?? "").ToArray()
                    : [],
                OptionalBoolean(item, "quoteOrderQtyMarketAllowed"),
                OptionalPositiveDecimal(item, "baseSizePrecision"),
                OptionalPositiveDecimal(item, "quoteAmountPrecisionMarket"),
                OptionalPositiveDecimal(item, "maxQuoteAmountMarket"),
                OptionalInt(item, "tradeSideType"));
        }

        throw new InvalidDataException($"MEXC metadata does not contain {expectedSymbol}.");
    }

    private static decimal DecimalScale(int precision)
    {
        var result = 1m;
        for (var index = 0; index < precision; index++)
        {
            result /= 10m;
        }

        return result;
    }

    private static decimal? OptionalPositiveDecimal(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
            return null;
        var text = property.ValueKind == JsonValueKind.String
            ? property.GetString() : property.GetRawText();
        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var result) && result > 0
            ? result : throw new InvalidDataException($"MEXC {name} must be positive decimal.");
    }

    private static bool? OptionalBoolean(JsonElement item, string name) =>
        !item.TryGetProperty(name, out var property) ? null : property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => throw new InvalidDataException($"MEXC {name} must be boolean.")
        };

    private static int? OptionalInt(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var property)) return null;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
            return number;
        if (property.ValueKind == JsonValueKind.String &&
            int.TryParse(property.GetString(), NumberStyles.None, CultureInfo.InvariantCulture,
                out number)) return number;
        throw new InvalidDataException($"MEXC {name} must be integer.");
    }

    private static string RequiredScalar(JsonElement item, string name)
    {
        var property = item.GetProperty(name);
        var value = property.ValueKind == JsonValueKind.String
            ? property.GetString() : property.GetRawText();
        return !string.IsNullOrWhiteSpace(value) ? value
            : throw new InvalidDataException($"MEXC {name} is missing.");
    }

    private static string RequiredString(JsonElement item, string name) =>
        item.GetProperty(name).GetString() is { Length: > 0 } value
            ? value
            : throw new InvalidDataException($"MEXC {name} is missing.");
}
