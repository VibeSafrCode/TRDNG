using System.Globalization;
using System.Text.Json;
using Trdng.Core.MarketData;

namespace Trdng.Bybit.MarketData;

public static class BybitPublicTradeMessageParser
{
    public static bool TryParse(
        ReadOnlyMemory<byte> utf8Json,
        out IReadOnlyList<PublicTrade> trades)
    {
        using var document = JsonDocument.Parse(utf8Json);
        var root = document.RootElement;

        if (!root.TryGetProperty("topic", out var topic) ||
            topic.GetString() is not { } topicValue ||
            !topicValue.StartsWith("publicTrade.", StringComparison.Ordinal))
        {
            trades = [];
            return false;
        }

        var data = root.GetProperty("data");
        var result = new PublicTrade[data.GetArrayLength()];
        var index = 0;

        foreach (var item in data.EnumerateArray())
        {
            result[index++] = new PublicTrade(
                item.GetProperty("i").GetString()
                    ?? throw new JsonException("Trade ID is missing."),
                item.GetProperty("s").GetString()
                    ?? throw new JsonException("Trade symbol is missing."),
                DateTimeOffset.FromUnixTimeMilliseconds(item.GetProperty("T").GetInt64()),
                item.GetProperty("S").GetString() switch
                {
                    "Buy" => AggressorSide.Buy,
                    "Sell" => AggressorSide.Sell,
                    var side => throw new JsonException($"Unknown aggressor side: {side}.")
                },
                ParseDecimal(item.GetProperty("p")),
                ParseDecimal(item.GetProperty("v")),
                item.GetProperty("seq").GetInt64());
        }

        trades = result;
        return true;
    }

    private static decimal ParseDecimal(JsonElement element) =>
        decimal.Parse(
            element.GetString() ?? throw new JsonException("Numeric string is missing."),
            NumberStyles.Number,
            CultureInfo.InvariantCulture);
}
