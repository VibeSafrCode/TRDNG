namespace Trdng.Core.MarketData;

public sealed class OrderBookEngine
{
    private readonly SortedDictionary<decimal, decimal> _bids =
        new(Comparer<decimal>.Create(static (left, right) => right.CompareTo(left)));

    private readonly SortedDictionary<decimal, decimal> _asks = new();
    private string? _symbol;

    public bool HasSnapshot { get; private set; }

    public long LastUpdateId { get; private set; }

    public long LastCrossSequence { get; private set; }

    public void Reset()
    {
        _bids.Clear();
        _asks.Clear();
        _symbol = null;
        HasSnapshot = false;
        LastUpdateId = 0;
        LastCrossSequence = 0;
    }

    public void ApplySnapshot(OrderBookUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        ValidateUpdate(update);

        _bids.Clear();
        _asks.Clear();
        ApplyLevels(_bids, update.Bids);
        ApplyLevels(_asks, update.Asks);

        _symbol = update.Symbol;
        LastUpdateId = update.UpdateId;
        LastCrossSequence = update.CrossSequence;
        HasSnapshot = true;
    }

    public bool TryApplyDelta(OrderBookUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        ValidateUpdate(update);

        if (!HasSnapshot)
        {
            return false;
        }

        if (!string.Equals(_symbol, update.Symbol, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot apply {update.Symbol} delta to {_symbol} order book.");
        }

        if (update.UpdateId <= LastUpdateId ||
            update.CrossSequence <= LastCrossSequence)
        {
            return false;
        }

        ApplyLevels(_bids, update.Bids);
        ApplyLevels(_asks, update.Asks);
        LastUpdateId = update.UpdateId;
        LastCrossSequence = update.CrossSequence;
        return true;
    }

    public OrderBookSnapshot Capture(int depth)
    {
        if (!HasSnapshot || _symbol is null)
        {
            throw new InvalidOperationException("An order-book snapshot has not been applied.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);

        return new OrderBookSnapshot(
            _symbol,
            LastUpdateId,
            LastCrossSequence,
            CaptureSide(_bids, depth),
            CaptureSide(_asks, depth));
    }

    private static IReadOnlyList<OrderBookLevel> CaptureSide(
        SortedDictionary<decimal, decimal> source,
        int depth)
    {
        var result = new List<OrderBookLevel>(Math.Min(depth, source.Count));

        foreach (var (price, quantity) in source)
        {
            if (result.Count == depth)
            {
                break;
            }

            result.Add(new OrderBookLevel(price, quantity));
        }

        return result;
    }

    private static void ApplyLevels(
        SortedDictionary<decimal, decimal> target,
        IReadOnlyList<OrderBookLevel> levels)
    {
        foreach (var level in levels)
        {
            if (level.Price <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(levels),
                    level.Price,
                    "Price must be greater than zero.");
            }

            if (level.Quantity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(levels),
                    level.Quantity,
                    "Quantity cannot be negative.");
            }

            if (level.Quantity == 0)
            {
                target.Remove(level.Price);
            }
            else
            {
                target[level.Price] = level.Quantity;
            }
        }
    }

    private static void ValidateUpdate(OrderBookUpdate update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(update.Symbol);
        ArgumentOutOfRangeException.ThrowIfNegative(update.UpdateId);
        ArgumentOutOfRangeException.ThrowIfNegative(update.CrossSequence);
    }
}
