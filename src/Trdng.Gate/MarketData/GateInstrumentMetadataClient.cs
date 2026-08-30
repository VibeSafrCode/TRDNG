using System.Globalization;
using System.Text.Json;
using Trdng.Core.Instruments;
using Trdng.Core.MarketData;

namespace Trdng.Gate.MarketData;

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
    {
        var json = await BoundedHttpContentReader.GetJsonBytesAsync(
            httpClient, Endpoint, MaximumContractsBytes, cancellationToken)
            .ConfigureAwait(false);
        return ParseCatalog(json);
    }

    public static IReadOnlyList<PublicCatalogEntry> ParseCatalog(
        ReadOnlyMemory<byte> utf8Json)
    {
        using var document = JsonDocument.Parse(utf8Json);
        var entries = new List<PublicCatalogEntry>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("in_delisting", out var delisting) ||
                delisting.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("Gate in_delisting must be an exact boolean.");
            if (delisting.GetBoolean()) continue;
            var name = item.GetProperty("name").GetString();
            var tickText = item.GetProperty("order_price_round").GetString();
            var multiplierText = item.GetProperty("quanto_multiplier").GetString();
            if (name is null || !name.EndsWith("_USDT", StringComparison.Ordinal) ||
                !decimal.TryParse(tickText, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var tick) || tick <= 0 ||
                !decimal.TryParse(multiplierText, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var multiplier) || multiplier <= 0)
                throw new InvalidDataException("Gate catalog entry is incomplete.");
            var baseAsset = name[..^5];
            entries.Add(new(new(baseAsset, "USDT", MarketProduct.Perpetual),
                TradingVenue.Gate, name, tick, multiplier));
        }
        return entries;
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
