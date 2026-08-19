using System.Globalization;
using System.Text.Json;

namespace Trdng.Mexc.MarketData;

public sealed class MexcInstrumentMetadataClient(HttpClient httpClient)
{
    private static readonly Uri Endpoint =
        new("https://api.mexc.com/api/v3/exchangeInfo");

    public async Task<MexcInstrumentMetadata> GetSpotAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var normalized = symbol.ToUpperInvariant();
        var json = await httpClient.GetByteArrayAsync(
            new Uri($"{Endpoint}?symbol={Uri.EscapeDataString(normalized)}"),
            cancellationToken).ConfigureAwait(false);
        return Parse(json, normalized);
    }

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
