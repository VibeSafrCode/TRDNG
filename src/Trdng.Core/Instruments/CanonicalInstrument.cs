using System.Text.Json.Serialization;

namespace Trdng.Core.Instruments;

public readonly record struct CanonicalInstrument
{
    [JsonConstructor]
    public CanonicalInstrument(
        string baseAsset,
        string quoteAsset,
        MarketProduct product)
    {
        BaseAsset = Normalize(baseAsset, nameof(baseAsset));
        QuoteAsset = Normalize(quoteAsset, nameof(quoteAsset));
        if (BaseAsset == QuoteAsset)
        {
            throw new ArgumentException(
                "Base and quote assets must be different.",
                nameof(quoteAsset));
        }

        Product = product;
    }

    public string BaseAsset { get; }

    public string QuoteAsset { get; }

    public MarketProduct Product { get; }

    public string PairId => $"{BaseAsset}/{QuoteAsset}";

    public string Id => $"{PairId}:{Product.ToString().ToUpperInvariant()}";

    private static string Normalize(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Any(static character =>
                !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException(
                "Asset codes must contain ASCII letters or digits only.",
                parameterName);
        }

        return normalized;
    }
}
