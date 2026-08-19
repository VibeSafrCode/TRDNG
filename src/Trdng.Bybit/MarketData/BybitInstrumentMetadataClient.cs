using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace Trdng.Bybit.MarketData;

public sealed class BybitInstrumentMetadataClient(HttpClient httpClient)
{
    private static readonly Uri Endpoint =
        new("https://api.bybit.com/v5/market/instruments-info");

    public async Task<decimal> GetLinearTickSizeAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var uri = new Uri(
            $"{Endpoint}?category=linear&symbol={Uri.EscapeDataString(symbol.ToUpperInvariant())}");
        var json = await httpClient.GetByteArrayAsync(uri, cancellationToken)
            .ConfigureAwait(false);
        return ParseTickSize(json, symbol);
    }

    public static decimal ParseTickSize(
        ReadOnlyMemory<byte> utf8Json,
        string expectedSymbol)
    {
        using var document = JsonDocument.Parse(utf8Json);
        var root = document.RootElement;
        if (root.GetProperty("retCode").GetInt32() != 0)
        {
            throw new InvalidDataException("Bybit rejected instrument metadata request.");
        }

        foreach (var item in root.GetProperty("result").GetProperty("list")
                     .EnumerateArray())
        {
            if (!string.Equals(
                    item.GetProperty("symbol").GetString(),
                    expectedSymbol,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var tick = decimal.Parse(
                item.GetProperty("priceFilter").GetProperty("tickSize").GetString()
                    ?? throw new InvalidDataException("Bybit tickSize is missing."),
                NumberStyles.Number,
                CultureInfo.InvariantCulture);
            return tick > 0
                ? tick
                : throw new InvalidDataException("Bybit tickSize must be positive.");
        }

        throw new InvalidDataException(
            $"Bybit metadata does not contain {expectedSymbol}.");
    }
}
