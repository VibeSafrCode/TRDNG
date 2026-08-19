using System.Globalization;
using System.Text.Json;
using Trdng.Core.MarketData;

namespace Trdng.Gate.MarketData;

public static class GateOrderBookMessageParser
{
    public static bool TryParse(
        ReadOnlyMemory<byte> utf8Json,
        out GateOrderBookMessage? message,
        decimal contractMultiplier = 0.0001m)
    {
        using var document = JsonDocument.Parse(utf8Json);
        var root = document.RootElement;
        if (!root.TryGetProperty("channel", out var channel) ||
            channel.GetString() != "futures.obu" ||
            !root.TryGetProperty("event", out var eventElement) ||
            eventElement.GetString() != "update")
        {
            message = null;
            return false;
        }

        var result = root.GetProperty("result");
        var snapshot =
            result.TryGetProperty("full", out var full) && full.GetBoolean();
        var updateId = result.GetProperty("u").GetInt64();
        var firstId = result.TryGetProperty("U", out var first)
            ? first.GetInt64()
            : updateId;
        var stream = result.GetProperty("s").GetString()
            ?? throw new JsonException("Gate stream name is missing.");
        var parts = stream.Split('.');
        if (parts.Length < 2)
        {
            throw new JsonException($"Invalid Gate stream name: {stream}.");
        }

        message = new GateOrderBookMessage(
            snapshot,
            firstId,
            new OrderBookUpdate(
                parts[1].Replace("_", string.Empty, StringComparison.Ordinal),
                updateId,
                updateId,
                ParseLevels(result, "b", contractMultiplier),
                ParseLevels(result, "a", contractMultiplier)));
        return true;
    }

    private static IReadOnlyList<OrderBookLevel> ParseLevels(
        JsonElement result,
        string property,
        decimal contractMultiplier)
    {
        if (!result.TryGetProperty(property, out var element))
        {
            return [];
        }

        return element.EnumerateArray()
            .Select(pair => new OrderBookLevel(
                ParseDecimal(pair[0]),
                ParseDecimal(pair[1]) * contractMultiplier))
            .ToArray();
    }

    private static decimal ParseDecimal(JsonElement element) =>
        element.ValueKind == JsonValueKind.String
            ? decimal.Parse(
                element.GetString()!,
                NumberStyles.Number,
                CultureInfo.InvariantCulture)
            : element.GetDecimal();
}
