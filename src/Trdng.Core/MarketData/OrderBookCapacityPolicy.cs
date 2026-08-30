namespace Trdng.Core.MarketData;

public sealed record OrderBookCapacityPolicy
{
    public const int DefaultMaximumLevelsPerSide = 5_000;
    public const int DefaultMaximumLevelsPerUpdate = 10_000;

    public OrderBookCapacityPolicy(
        int maximumLevelsPerSide = DefaultMaximumLevelsPerSide,
        int maximumLevelsPerUpdate = DefaultMaximumLevelsPerUpdate,
        decimal? maximumPrice = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLevelsPerSide);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLevelsPerUpdate);

        if (maximumPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPrice));
        }

        MaximumLevelsPerSide = maximumLevelsPerSide;
        MaximumLevelsPerUpdate = maximumLevelsPerUpdate;
        MaximumPrice = maximumPrice;
    }

    public int MaximumLevelsPerSide { get; }

    public int MaximumLevelsPerUpdate { get; }

    public decimal? MaximumPrice { get; }
}

public enum OrderBookPolicyViolationCode
{
    UpdateTooLarge,
    SideCapacityExceeded,
    DuplicatePrice,
    InvalidLevel,
    PriceLimitExceeded,
    CrossedBook
}

public sealed class OrderBookPolicyViolationException : IOException
{
    public OrderBookPolicyViolationException(OrderBookPolicyViolationCode code)
        : base($"ORDER_BOOK_{ToSafeCode(code)}") => Code = code;

    public OrderBookPolicyViolationCode Code { get; }

    public string SafeCode => Message;

    private static string ToSafeCode(OrderBookPolicyViolationCode code) => code switch
    {
        OrderBookPolicyViolationCode.UpdateTooLarge => "UPDATE_TOO_LARGE",
        OrderBookPolicyViolationCode.SideCapacityExceeded => "SIDE_CAPACITY_EXCEEDED",
        OrderBookPolicyViolationCode.DuplicatePrice => "DUPLICATE_PRICE",
        OrderBookPolicyViolationCode.InvalidLevel => "INVALID_LEVEL",
        OrderBookPolicyViolationCode.PriceLimitExceeded => "PRICE_LIMIT_EXCEEDED",
        OrderBookPolicyViolationCode.CrossedBook => "CROSSED",
        _ => "POLICY_VIOLATION"
    };
}
