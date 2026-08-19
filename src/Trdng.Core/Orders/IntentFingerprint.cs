using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Trdng.Core.Orders;

public static class IntentFingerprint
{
    public static string Create(MarketOrderIntent intent)
    {
        var canonical = string.Join('|',
            intent.ClientOrderId,
            intent.Venue,
            intent.Instrument.BaseAsset,
            intent.Instrument.QuoteAsset,
            intent.Instrument.Product,
            intent.Side,
            intent.Type,
            intent.SizingMode,
            intent.SizingValue.ToString(CultureInfo.InvariantCulture),
            intent.Price?.ToString(CultureInfo.InvariantCulture) ?? "null");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
