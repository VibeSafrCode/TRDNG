using System.Globalization;
using System.Text.Json;
using Trdng.Core.Instruments;
using Trdng.Core.MarketData;

namespace Trdng.Mexc.MarketData;

public enum MexcContractCatalogRejection
{
    Ineligible,
    InvalidCanonicalAsset,
    MissingOrWrongRequiredField,
    InvalidOther,
    DuplicateOrConflict
}

public sealed record MexcContractCatalogParseResult(
    IReadOnlyList<PublicCatalogEntry> Entries,
    IReadOnlyDictionary<MexcContractCatalogRejection, int> Rejections)
{
    public int InvalidEligibleCount => Rejections
        .Where(item => item.Key != MexcContractCatalogRejection.Ineligible)
        .Sum(item => item.Value);
}

public sealed class MexcContractInstrumentMetadataClient(HttpClient httpClient)
{
    internal const int MaximumContractDetailBytes = 8 * 1024 * 1024;
    private static readonly Uri Endpoint =
        new("https://contract.mexc.com/api/v1/contract/detail");

    public async Task<MexcContractCatalogParseResult> GetUsdtPerpetualCatalogResultAsync(
        CancellationToken cancellationToken = default)
    {
        var json = await BoundedHttpContentReader.GetJsonBytesAsync(
            httpClient, Endpoint, MaximumContractDetailBytes, cancellationToken)
            .ConfigureAwait(false);
        var result = ParseCatalogResult(json);
        if (result.Entries.Count == 0)
            throw new InvalidDataException("MEXC contract catalog contains no valid eligible entries.");
        return result;
    }

    public static MexcContractCatalogParseResult ParseCatalogResult(
        ReadOnlyMemory<byte> utf8Json)
    {
        using var document = JsonDocument.Parse(utf8Json);
        var root = document.RootElement;
        if (!IsSuccessfulRoot(root) ||
            !root.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("MEXC contract detail root is invalid.");

        var entries = new List<PublicCatalogEntry>();
        var counts = new Dictionary<MexcContractCatalogRejection, int>();
        var exact = new Dictionary<CanonicalInstrument, string>();
        var conflicts = new HashSet<CanonicalInstrument>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !TryRequiredString(item, "symbol", out var symbol) ||
                !TryRequiredString(item, "baseCoin", out var baseCoin) ||
                !TryRequiredString(item, "quoteCoin", out var quoteCoin) ||
                !TryRequiredString(item, "settleCoin", out var settleCoin) ||
                !TryRequiredInt(item, "state", out var state) ||
                !TryRequiredPositiveDecimal(item, "contractSize", out var contractSize) ||
                !TryRequiredPositiveDecimal(item, "priceUnit", out var priceUnit))
            {
                Add(counts, MexcContractCatalogRejection.MissingOrWrongRequiredField);
                continue;
            }
            if (state != 0 || quoteCoin != "USDT" || settleCoin != "USDT")
            {
                Add(counts, MexcContractCatalogRejection.Ineligible);
                continue;
            }
            if (!IsCanonicalAsset(baseCoin) || !IsCanonicalAsset(quoteCoin))
            {
                Add(counts, MexcContractCatalogRejection.InvalidCanonicalAsset);
                continue;
            }
            if (symbol.Any(char.IsWhiteSpace))
            {
                Add(counts, MexcContractCatalogRejection.InvalidOther);
                continue;
            }

            CanonicalInstrument instrument;
            try { instrument = new(baseCoin, quoteCoin, MarketProduct.Perpetual); }
            catch (ArgumentException)
            {
                Add(counts, MexcContractCatalogRejection.InvalidCanonicalAsset);
                continue;
            }
            if (conflicts.Contains(instrument))
            {
                Add(counts, MexcContractCatalogRejection.DuplicateOrConflict);
                continue;
            }
            if (exact.TryGetValue(instrument, out var existing))
            {
                if (existing == symbol)
                {
                    Add(counts, MexcContractCatalogRejection.DuplicateOrConflict);
                    continue;
                }
                entries.RemoveAll(entry => entry.Instrument == instrument);
                exact.Remove(instrument);
                conflicts.Add(instrument);
                Add(counts, MexcContractCatalogRejection.DuplicateOrConflict, 2);
                continue;
            }
            exact.Add(instrument, symbol);
            entries.Add(new(instrument, TradingVenue.Mexc, symbol, priceUnit, contractSize));
        }
        return new(entries, counts);
    }

    private static bool IsSuccessfulRoot(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        (!root.TryGetProperty("success", out var success) ||
            success.ValueKind == JsonValueKind.True) &&
        (!root.TryGetProperty("code", out var code) ||
            code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var value) && value == 0);

    private static bool TryRequiredString(JsonElement item, string name, out string value)
    {
        value = string.Empty;
        return item.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            property.GetString() is { Length: > 0 } text &&
            (value = text).Length > 0;
    }

    private static bool TryRequiredInt(JsonElement item, string name, out int value)
    {
        value = default;
        return item.TryGetProperty(name, out var property) &&
            (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value) ||
             property.ValueKind == JsonValueKind.String &&
             int.TryParse(property.GetString(), NumberStyles.Integer,
                 CultureInfo.InvariantCulture, out value));
    }

    private static bool TryRequiredPositiveDecimal(
        JsonElement item, string name, out decimal value)
    {
        value = default;
        if (!item.TryGetProperty(name, out var property)) return false;
        var text = property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ValueKind == JsonValueKind.Number ? property.GetRawText() : null;
        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value) && value > 0;
    }

    private static bool IsCanonicalAsset(string value) =>
        value.All(char.IsAsciiLetterOrDigit);

    private static void Add(
        Dictionary<MexcContractCatalogRejection, int> counts,
        MexcContractCatalogRejection rejection,
        int amount = 1) =>
        counts[rejection] = counts.GetValueOrDefault(rejection) + amount;
}
