using System.Globalization;
using System.Text.Json;
using Trdng.Core.MarketData;

namespace Trdng.Mexc.MarketData;

public static class MexcDepthSnapshotParser
{
    public static OrderBookUpdate Parse(ReadOnlyMemory<byte> utf8Json, string symbol)
    {
        using var document = JsonDocument.Parse(utf8Json);
        var root = document.RootElement;
        var version = root.GetProperty("lastUpdateId").GetInt64();
        return new OrderBookUpdate(
            symbol.ToUpperInvariant(),
            version,
            version,
            ParseLevels(root.GetProperty("bids")),
            ParseLevels(root.GetProperty("asks")));
    }

    private static IReadOnlyList<OrderBookLevel> ParseLevels(JsonElement values) =>
        values.EnumerateArray().Select(static level => ParseLevel(level)).ToArray();

    private static OrderBookLevel ParseLevel(JsonElement level)
    {
        var price = decimal.Parse(level[0].GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture);
        var quantity = decimal.Parse(level[1].GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture);
        if (price <= 0) throw new InvalidDataException("MEXC snapshot price must be positive.");
        if (quantity < 0) throw new InvalidDataException("MEXC snapshot quantity cannot be negative.");
        return new OrderBookLevel(price, quantity);
    }
}
