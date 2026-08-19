using System.Globalization;
using System.Text.Json;
using Trdng.Core.MarketData;

namespace Trdng.Gate.MarketData;

public static class GateTradeMessageParser
{
    public static bool TryParse(
        ReadOnlyMemory<byte> utf8Json,
        out IReadOnlyList<PublicTrade> trades,
        decimal contractMultiplier = 0.0001m)
    {
        using var document = JsonDocument.Parse(utf8Json);
        var root = document.RootElement;
        if (!root.TryGetProperty("channel", out var channel) ||
            channel.GetString() != "futures.trades" ||
            !root.TryGetProperty("event", out var eventElement) ||
            eventElement.GetString() != "update")
        {
            trades = [];
            return false;
        }

        trades = root.GetProperty("result").EnumerateArray().Select(item =>
        {
            var size = ParseDecimal(item.GetProperty("size"));
            var time = item.TryGetProperty("create_time_ms", out var timeMs)
                ? timeMs.GetInt64()
                : item.GetProperty("create_time").GetInt64() * 1000;
            var id = item.GetProperty("id").GetInt64();
            return new PublicTrade(
                id.ToString(CultureInfo.InvariantCulture),
                item.GetProperty("contract").GetString()!
                    .Replace("_", string.Empty, StringComparison.Ordinal),
                DateTimeOffset.FromUnixTimeMilliseconds(time),
                size > 0 ? AggressorSide.Buy : AggressorSide.Sell,
                ParseDecimal(item.GetProperty("price")),
                decimal.Abs(size) * contractMultiplier,
                id);
        }).ToArray();
        return true;
    }

    private static decimal ParseDecimal(JsonElement element) =>
        element.ValueKind == JsonValueKind.String
            ? decimal.Parse(
                element.GetString()!,
                NumberStyles.Number,
                CultureInfo.InvariantCulture)
            : element.GetDecimal();
}
