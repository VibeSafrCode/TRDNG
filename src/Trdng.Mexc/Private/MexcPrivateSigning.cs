using System.Security.Cryptography;
using System.Text;
using Trdng.Core.Orders;

namespace Trdng.Mexc.Private;

public static class MexcHmacSigner
{
    public static string Sign(ReadOnlySpan<byte> secret, string exactQuery)
    {
        if (secret.IsEmpty) throw new ArgumentException("Secret is required.", nameof(secret));
        var bytes = Encoding.UTF8.GetBytes(exactQuery);
        try { return Convert.ToHexString(HMACSHA256.HashData(secret, bytes)).ToLowerInvariant(); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}

public sealed class MexcSignedRequest : IDisposable
{
    internal MexcSignedRequest(HttpRequestMessage request) => Request = request;
    internal HttpRequestMessage Request { get; }
    public override string ToString() => "MEXC SIGNED REQUEST · REDACTED";
    public void Dispose() => Request.Dispose();
}

public static class MexcSignedRequestBuilder
{
    public const int MaxApiKeyBytes = 256;
    private static readonly Uri BaseUri = new("https://api.mexc.com");
    private static readonly HashSet<string> AllowedPaths =
        ["/api/v3/account", "/api/v3/openOrders"];
    private const string OrderTestPath = "/api/v3/order/test";

    public static MexcSignedRequest BuildGet(string path,
        IReadOnlyDictionary<string, string?> parameters, long timestamp, int recvWindow,
        ReadOnlySpan<byte> apiKey, ReadOnlySpan<byte> secret)
    {
        if (!AllowedPaths.Contains(path)) throw new InvalidOperationException("Endpoint denied.");
        if (recvWindow is <= 0 or > 60_000) throw new ArgumentOutOfRangeException(nameof(recvWindow));
        if (timestamp <= 0 || secret.IsEmpty)
            throw new InvalidOperationException("Signed request prerequisites are missing.");
        ValidateApiKey(apiKey);
        var query = BuildCanonicalQuery(path, parameters, timestamp, recvWindow);
        var signature = MexcHmacSigner.Sign(secret, query);
        var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(BaseUri, $"{path}?{query}&signature={signature}"));
        var boundedHeader = Encoding.ASCII.GetString(apiKey);
        if (!request.Headers.TryAddWithoutValidation("X-MEXC-APIKEY", boundedHeader))
        {
            request.Dispose();
            throw new InvalidOperationException("API key header was rejected.");
        }
        return new MexcSignedRequest(request);
    }

    public static string BuildCanonicalQuery(string path,
        IReadOnlyDictionary<string, string?> parameters, long timestamp, int recvWindow)
    {
        if (!AllowedPaths.Contains(path)) throw new InvalidOperationException("Endpoint denied.");
        if (recvWindow is <= 0 or > 60_000) throw new ArgumentOutOfRangeException(nameof(recvWindow));
        if (timestamp <= 0) throw new ArgumentOutOfRangeException(nameof(timestamp));
        var allowed = path == "/api/v3/account" ? Array.Empty<string>() : ["symbol"];
        if (parameters.Keys.Any(key => !allowed.Contains(key, StringComparer.Ordinal)) ||
            (path == "/api/v3/openOrders" &&
             (!parameters.TryGetValue("symbol", out var symbol) || string.IsNullOrWhiteSpace(symbol))))
            throw new InvalidOperationException("Signed parameter denied.");
        var items = parameters.Where(item => item.Value is not null)
            .Select(item => (item.Key, Value: item.Value!)).ToList();
        items.Add(("recvWindow", recvWindow.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        items.Add(("timestamp", timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return string.Join('&', items.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{Encode(item.Key)}={Encode(item.Value)}"));
    }

    internal static MexcSignedRequest BuildOrderTestPost(
        IReadOnlyDictionary<string, string> orderParameters, long timestamp, int recvWindow,
        ReadOnlySpan<byte> apiKey, ReadOnlySpan<byte> secret)
    {
        ValidateApiKey(apiKey);
        if (secret.IsEmpty) throw new InvalidOperationException("Signed request prerequisites are missing.");
        var body = BuildOrderTestCanonicalBody(orderParameters, timestamp, recvWindow);
        var signature = MexcHmacSigner.Sign(secret, body);
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri, OrderTestPath))
        {
            Content = new StringContent($"{body}&signature={signature}", Encoding.UTF8,
                "application/x-www-form-urlencoded")
        };
        var boundedHeader = Encoding.ASCII.GetString(apiKey);
        if (!request.Headers.TryAddWithoutValidation("X-MEXC-APIKEY", boundedHeader))
        {
            request.Dispose();
            throw new InvalidOperationException("API key header was rejected.");
        }
        return new MexcSignedRequest(request);
    }

    internal static string BuildOrderTestCanonicalBody(
        IReadOnlyDictionary<string, string> parameters, long timestamp, int recvWindow)
    {
        if (recvWindow is <= 0 or > 60_000) throw new ArgumentOutOfRangeException(nameof(recvWindow));
        if (timestamp <= 0) throw new ArgumentOutOfRangeException(nameof(timestamp));
        string[] allowed = ["symbol", "side", "type", "quantity", "quoteOrderQty", "newClientOrderId"];
        if (parameters.Keys.Any(key => !allowed.Contains(key, StringComparer.Ordinal)) ||
            !parameters.TryGetValue("symbol", out var symbol) || !ValidSymbol(symbol) ||
            !parameters.TryGetValue("side", out var side) || side is not ("BUY" or "SELL") ||
            !parameters.TryGetValue("type", out var type) || type != "MARKET" ||
            !parameters.TryGetValue("newClientOrderId", out var clientId) ||
            !ClientOrderIdPolicy.IsValid(clientId) ||
            (side == "BUY") != parameters.ContainsKey("quoteOrderQty") ||
            (side == "SELL") != parameters.ContainsKey("quantity") ||
            parameters.ContainsKey("quantity") == parameters.ContainsKey("quoteOrderQty") ||
            !MexcDecimalWire.IsValid(side == "BUY"
                ? parameters.GetValueOrDefault("quoteOrderQty")
                : parameters.GetValueOrDefault("quantity")) ||
            parameters.Values.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Order test parameter boundary rejected.");
        var items = parameters.Select(item => (item.Key, item.Value)).ToList();
        items.Add(("recvWindow", recvWindow.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        items.Add(("timestamp", timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return string.Join('&', items.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{Encode(item.Key)}={Encode(item.Value)}"));
    }

    private static bool ValidSymbol(string symbol) => symbol.Length is >= 2 and <= 32 &&
        symbol.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9');

    private static string Encode(string value) => Uri.EscapeDataString(value);

    private static void ValidateApiKey(ReadOnlySpan<byte> apiKey)
    {
        if (apiKey.IsEmpty || apiKey.Length > MaxApiKeyBytes)
            throw new InvalidOperationException("API key boundary rejected.");
        foreach (var value in apiKey)
            if (value is < 0x21 or > 0x7e)
                throw new InvalidOperationException("API key boundary rejected.");
    }
}

internal static class MexcDecimalWire
{
    public static string Format(decimal value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        return value.ToString("G29", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static bool IsValid(string? text)
    {
        if (text is not { Length: > 0 and <= 64 }) return false;
        var dot = -1;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '.')
            {
                if (dot >= 0 || index == 0 || index == text.Length - 1) return false;
                dot = index;
                continue;
            }
            if (character is < '0' or > '9') return false;
        }
        var integerLength = dot < 0 ? text.Length : dot;
        if (integerLength > 1 && text[0] == '0') return false;
        return decimal.TryParse(text, System.Globalization.NumberStyles.AllowDecimalPoint,
            System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0;
    }
}
