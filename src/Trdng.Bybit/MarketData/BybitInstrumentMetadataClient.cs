using System.Globalization;
using System.Text.Json;
using Trdng.Core.Instruments;
using Trdng.Core.MarketData;

namespace Trdng.Bybit.MarketData;

public sealed class BybitInstrumentMetadataClient(HttpClient httpClient)
{
    internal const int MaximumInstrumentPageBytes = 4 * 1024 * 1024;
    private static readonly Uri Endpoint =
        new("https://api.bybit.com/v5/market/instruments-info");

    public async Task<decimal> GetLinearTickSizeAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var uri = new Uri(
            $"{Endpoint}?category=linear&symbol={Uri.EscapeDataString(symbol.ToUpperInvariant())}");
        var json = await BoundedHttpContentReader.GetJsonBytesAsync(
            httpClient, uri, MaximumInstrumentPageBytes, cancellationToken)
            .ConfigureAwait(false);
        return ParseTickSize(json, symbol);
    }

    public async Task<IReadOnlyList<PublicCatalogEntry>> GetLinearPerpetualCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        var entries = new List<PublicCatalogEntry>();
        var cursor = string.Empty;
        for (var page = 0; page < 20; page++)
        {
            var suffix = string.IsNullOrEmpty(cursor) ? string.Empty :
                $"&cursor={Uri.EscapeDataString(cursor)}";
            var json = await BoundedHttpContentReader.GetJsonBytesAsync(
                httpClient,
                new Uri($"{Endpoint}?category=linear&status=Trading&limit=1000{suffix}"),
                MaximumInstrumentPageBytes,
                cancellationToken).ConfigureAwait(false);
            var parsed = ParseCatalogPage(json);
            entries.AddRange(parsed.Entries);
            if (string.IsNullOrEmpty(parsed.NextCursor)) return entries;
            if (parsed.NextCursor == cursor)
                throw new InvalidDataException("Bybit catalog cursor did not advance.");
            cursor = parsed.NextCursor;
        }
        throw new InvalidDataException("Bybit catalog exceeded pagination bound.");
    }

    public static (IReadOnlyList<PublicCatalogEntry> Entries, string NextCursor)
        ParseCatalogPage(ReadOnlyMemory<byte> utf8Json)
    {
        using var document = JsonDocument.Parse(utf8Json);
        if (document.RootElement.GetProperty("retCode").GetInt32() != 0)
            throw new InvalidDataException("Bybit rejected catalog request.");
        var result = document.RootElement.GetProperty("result");
        var entries = new List<PublicCatalogEntry>();
        foreach (var item in result.GetProperty("list").EnumerateArray())
        {
            if (item.GetProperty("status").GetString() != "Trading" ||
                item.GetProperty("contractType").GetString() != "LinearPerpetual" ||
                item.GetProperty("quoteCoin").GetString() != "USDT" ||
                item.GetProperty("settleCoin").GetString() != "USDT") continue;
            var baseAsset = item.GetProperty("baseCoin").GetString();
            var quoteAsset = item.GetProperty("quoteCoin").GetString();
            var symbol = item.GetProperty("symbol").GetString();
            var tickText = item.GetProperty("priceFilter").GetProperty("tickSize").GetString();
            if (baseAsset is null || quoteAsset is null || symbol is null ||
                !decimal.TryParse(tickText, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var tick) || tick <= 0)
                throw new InvalidDataException("Bybit catalog entry is incomplete.");
            entries.Add(new(new(baseAsset, quoteAsset, MarketProduct.Perpetual),
                TradingVenue.Bybit, symbol, tick));
        }
        var next = result.TryGetProperty("nextPageCursor", out var cursor)
            ? cursor.GetString() ?? string.Empty : string.Empty;
        return (entries, next);
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
