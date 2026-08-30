using System.Globalization;
using System.Text.Json;
using Trdng.Core.Instruments;
using Trdng.Core.MarketData;

namespace Trdng.Gate.MarketData;

public sealed record GateCatalogParseResult(
    IReadOnlyList<PublicCatalogEntry> Entries,
    int RejectedCount);

public sealed class GateInstrumentMetadataClient(HttpClient httpClient)
{
    internal const int MaximumContractsBytes = 8 * 1024 * 1024;
    private static readonly Uri Endpoint =
        new("https://api.gateio.ws/api/v4/futures/usdt/contracts");

    public async Task<decimal> GetTickSizeAsync(
        string contract,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contract);
        var json = await BoundedHttpContentReader.GetJsonBytesAsync(
            httpClient, Endpoint, MaximumContractsBytes, cancellationToken)
            .ConfigureAwait(false);
        return ParseTickSize(json, contract);
    }

    public async Task<IReadOnlyList<PublicCatalogEntry>> GetUsdtPerpetualCatalogAsync(
        CancellationToken cancellationToken = default)
        => (await GetUsdtPerpetualCatalogResultAsync(cancellationToken)
            .ConfigureAwait(false)).Entries;

    public async Task<GateCatalogParseResult> GetUsdtPerpetualCatalogResultAsync(
        CancellationToken cancellationToken = default)
    {
        var json = await BoundedHttpContentReader.GetJsonBytesAsync(
            httpClient, Endpoint, MaximumContractsBytes, cancellationToken)
            .ConfigureAwait(false);
        var result = ParseCatalogResult(json);
        if (result.Entries.Count == 0)
            throw new InvalidDataException("Gate catalog contains no valid eligible entries.");
        return result;
    }

    public static IReadOnlyList<PublicCatalogEntry> ParseCatalog(
        ReadOnlyMemory<byte> utf8Json)
    {
        var result = ParseCatalogResult(utf8Json);
        if (result.Entries.Count == 0 && result.RejectedCount > 0)
            throw new InvalidDataException("Gate catalog contains no valid entries.");
        return result.Entries;
    }

    public static GateCatalogParseResult ParseCatalogResult(
        ReadOnlyMemory<byte> utf8Json)
    {
        using var document = JsonDocument.Parse(utf8Json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Gate contracts root must be an array.");
        var entries = new List<PublicCatalogEntry>();
        var exact = new Dictionary<CanonicalInstrument, PublicCatalogEntry>();
        var conflicts = new HashSet<CanonicalInstrument>();
        var rejected = 0;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                rejected++;
                continue;
            }
            if (!item.TryGetProperty("in_delisting", out var delisting) ||
                delisting.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                rejected++;
                continue;
            }
            if (delisting.GetBoolean()) continue;
            var name = item.TryGetProperty("name", out var nameProperty) &&
                nameProperty.ValueKind == JsonValueKind.String ? nameProperty.GetString() : null;
            var tickText = item.TryGetProperty("order_price_round", out var tickProperty) &&
                tickProperty.ValueKind == JsonValueKind.String ? tickProperty.GetString() : null;
            var multiplierText = item.TryGetProperty("quanto_multiplier", out var multiplierProperty) &&
                multiplierProperty.ValueKind == JsonValueKind.String ? multiplierProperty.GetString() : null;
            if (name is null || !name.EndsWith("_USDT", StringComparison.Ordinal) ||
                name.Any(char.IsWhiteSpace) ||
                !decimal.TryParse(tickText, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var tick) || tick <= 0 ||
                !decimal.TryParse(multiplierText, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var multiplier) || multiplier <= 0)
            {
                rejected++;
                continue;
            }
            var baseAsset = name[..^5];
            CanonicalInstrument instrument;
            try { instrument = new(baseAsset, "USDT", MarketProduct.Perpetual); }
            catch (ArgumentException)
            {
                rejected++;
                continue;
            }
            if (conflicts.Contains(instrument))
            {
                rejected++;
                continue;
            }
            var entry = new PublicCatalogEntry(
                instrument, TradingVenue.Gate, name, tick, multiplier);
            if (exact.TryGetValue(instrument, out var existing))
            {
                if (existing == entry)
                {
                    rejected++;
                    continue;
                }
                entries.Remove(existing);
                exact.Remove(instrument);
                conflicts.Add(instrument);
                rejected += 2;
                continue;
            }
            exact.Add(instrument, entry);
            entries.Add(entry);
        }
        return new(entries, rejected);
    }

    public static decimal ParseTickSize(
        ReadOnlyMemory<byte> utf8Json,
        string expectedContract)
    {
        using var document = JsonDocument.Parse(utf8Json);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!string.Equals(
                    item.GetProperty("name").GetString(),
                    expectedContract,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var tick = decimal.Parse(
                item.GetProperty("order_price_round").GetString()
                    ?? throw new InvalidDataException(
                        "Gate order_price_round is missing."),
                NumberStyles.Number,
                CultureInfo.InvariantCulture);
            return tick > 0
                ? tick
                : throw new InvalidDataException(
                    "Gate order_price_round must be positive.");
        }

        throw new InvalidDataException(
            $"Gate metadata does not contain {expectedContract}.");
    }
}
