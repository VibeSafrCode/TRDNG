using System.Globalization;
using System.Text.Json;

namespace Trdng.Gate.MarketData;

public sealed class GateInstrumentMetadataClient(HttpClient httpClient)
{
    private static readonly Uri Endpoint =
        new("https://api.gateio.ws/api/v4/futures/usdt/contracts");

    public async Task<decimal> GetTickSizeAsync(
        string contract,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contract);
        var json = await httpClient.GetByteArrayAsync(Endpoint, cancellationToken)
            .ConfigureAwait(false);
        return ParseTickSize(json, contract);
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
