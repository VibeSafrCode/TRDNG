using System.Globalization;
using System.Text.Json;
using Trdng.Core.MarketData;

namespace Trdng.Bybit.MarketData;

public static class BybitOrderBookMessageParser
{
    public static bool TryParse(
        ReadOnlyMemory<byte> utf8Json,
        out BybitOrderBookMessage? message)
    {
        using var document = JsonDocument.Parse(utf8Json);
        var root = document.RootElement;

        if (!root.TryGetProperty("topic", out var topic) ||
            topic.GetString() is not { } topicValue ||
            !topicValue.StartsWith("orderbook.", StringComparison.Ordinal))
        {
            message = null;
            return false;
        }

        message = Parse(document.RootElement);
        return true;
    }

    public static BybitOrderBookMessage Parse(ReadOnlyMemory<byte> utf8Json)
    {
        using var document = JsonDocument.Parse(utf8Json);
        return Parse(document.RootElement);
    }

    private static BybitOrderBookMessage Parse(JsonElement root)
    {

        var type = root.GetProperty("type").GetString() switch
        {
            "snapshot" => BybitOrderBookMessageType.Snapshot,
            "delta" => BybitOrderBookMessageType.Delta,
            var value => throw new JsonException($"Unsupported order-book message type: {value}.")
        };

        var data = root.GetProperty("data");
        var update = new OrderBookUpdate(
            data.GetProperty("s").GetString()
                ?? throw new JsonException("Order-book symbol is missing."),
            data.GetProperty("u").GetInt64(),
            data.GetProperty("seq").GetInt64(),
            ParseLevels(data.GetProperty("b")),
            ParseLevels(data.GetProperty("a")));

        return new BybitOrderBookMessage(type, update);
    }

    private static IReadOnlyList<OrderBookLevel> ParseLevels(JsonElement element)
    {
        var levels = new OrderBookLevel[element.GetArrayLength()];
        var index = 0;

        foreach (var pair in element.EnumerateArray())
        {
            if (pair.GetArrayLength() != 2)
            {
                throw new JsonException("An order-book level must contain price and quantity.");
            }

            levels[index++] = new OrderBookLevel(
                ParseDecimal(pair[0]),
                ParseDecimal(pair[1]));
        }

        return levels;
    }

    private static decimal ParseDecimal(JsonElement element) =>
        decimal.Parse(
            element.GetString() ?? throw new JsonException("Numeric string is missing."),
            NumberStyles.Number,
            CultureInfo.InvariantCulture);
}
