namespace Trdng.Core.Orders;

public sealed record OrderSizingDefault(
    OrderSizingMode Mode,
    decimal Value,
    string Unit);

public static class OrderSizingDefaults
{
    public static OrderSizingDefault For(
        OrderSide side,
        string baseAsset,
        string quoteAsset) =>
        side == OrderSide.Buy
            ? new(OrderSizingMode.QuoteNotional, 10m, quoteAsset)
            : new(OrderSizingMode.BaseQuantity, 1m, baseAsset);
}
