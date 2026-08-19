using System.Globalization;
using System.Text.Json;

namespace Trdng.Mexc.Private;

public enum MexcPrivateState
{
    NotConfigured, KeychainDenied, TimeUnsynced, Ready, PermissionDenied,
    RateLimited, Unavailable, Error
}

public static class MexcPrivatePresentation
{
    public static string Masked(MexcPrivateState state) => state switch
    {
        MexcPrivateState.Ready => "MEXC PRIVATE · READ-ONLY READY",
        MexcPrivateState.KeychainDenied => "MEXC PRIVATE · KEYCHAIN DENIED",
        MexcPrivateState.TimeUnsynced => "MEXC PRIVATE · TIME UNSYNCED",
        MexcPrivateState.PermissionDenied => "MEXC PRIVATE · PERMISSION DENIED",
        MexcPrivateState.RateLimited => "MEXC PRIVATE · RATE LIMITED",
        MexcPrivateState.Unavailable => "MEXC PRIVATE · UNAVAILABLE",
        MexcPrivateState.Error => "MEXC PRIVATE · ERROR",
        _ => "MEXC PRIVATE · KEY REQUIRED"
    };
}

public sealed record MexcBalance(string Asset, decimal Free, decimal Locked);
public sealed record MexcAccount(bool CanTrade, string AccountType,
    IReadOnlyList<MexcBalance> Balances);
public sealed record MexcOpenOrder(string Symbol, string OrderId, string ClientOrderId,
    decimal Price, decimal OriginalQuantity, decimal ExecutedQuantity,
    string Status, string Side, string Type, long Time, long UpdateTime);

public sealed record MexcPrivateResult<T>(MexcPrivateState State, T? Value)
{
    public static MexcPrivateResult<T> Fail(MexcPrivateState state) => new(state, default);
}

internal static class MexcPrivateJson
{
    public static MexcAccount ParseAccount(ReadOnlyMemory<byte> json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var balances = root.GetProperty("balances").EnumerateArray().Select(item =>
            new MexcBalance(Required(item, "asset"), Decimal(item, "free"),
                Decimal(item, "locked"))).ToArray();
        return new MexcAccount(root.GetProperty("canTrade").GetBoolean(),
            Required(root, "accountType"), balances);
    }

    public static IReadOnlyList<MexcOpenOrder> ParseOpenOrders(ReadOnlyMemory<byte> json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Select(item => new MexcOpenOrder(
            Required(item, "symbol"), Scalar(item, "orderId"),
            Required(item, "clientOrderId"), Decimal(item, "price"),
            Decimal(item, "origQty"), Decimal(item, "executedQty"),
            Required(item, "status"), Required(item, "side"), Required(item, "type"),
            item.GetProperty("time").GetInt64(), item.GetProperty("updateTime").GetInt64()
        )).ToArray();
    }

    public static int? ErrorCode(ReadOnlyMemory<byte> json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("code", out var code) && code.TryGetInt32(out var value)
                ? value : null;
        }
        catch (JsonException) { return null; }
    }

    private static decimal Decimal(JsonElement element, string name) =>
        decimal.TryParse(Required(element, name), NumberStyles.Number,
            CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value : throw new InvalidDataException("MEXC numeric field is invalid.");
    private static string Required(JsonElement element, string name) =>
        element.GetProperty(name).GetString() is { Length: > 0 } value
            ? value : throw new InvalidDataException("MEXC required field is missing.");
    private static string Scalar(JsonElement element, string name) =>
        element.GetProperty(name).ValueKind == JsonValueKind.String
            ? Required(element, name) : element.GetProperty(name).GetRawText();
}
