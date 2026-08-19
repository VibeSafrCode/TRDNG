using Trdng.Core.Instruments;

namespace Trdng.Core.Orders;

public enum OrderSide { Buy, Sell }
public enum OrderType { Market }
public enum OrderSizingMode { BaseQuantity, QuoteNotional }

public sealed record MarketOrderIntent(
    TradingVenue Venue,
    CanonicalInstrument Instrument,
    OrderSide Side,
    OrderType Type,
    OrderSizingMode SizingMode,
    decimal SizingValue,
    string ClientOrderId,
    decimal? Price = null);
