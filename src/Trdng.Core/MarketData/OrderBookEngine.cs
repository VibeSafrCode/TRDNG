namespace Trdng.Core.MarketData;

public sealed class OrderBookEngine
{
    private static readonly IComparer<decimal> BidComparer =
        Comparer<decimal>.Create(static (left, right) => right.CompareTo(left));

    private readonly OrderBookCapacityPolicy _capacity;
    private SortedDictionary<decimal, decimal> _bids;
    private SortedDictionary<decimal, decimal> _asks;
    private string? _symbol;

    public OrderBookEngine(OrderBookCapacityPolicy? capacity = null)
    {
        _capacity = capacity ?? new OrderBookCapacityPolicy();
        _bids = new(BidComparer);
        _asks = [];
    }

    public OrderBookCapacityPolicy Capacity => _capacity;

    public bool HasSnapshot { get; private set; }

    public long LastUpdateId { get; private set; }

    public long LastCrossSequence { get; private set; }

    public long RejectedUpdateCount { get; private set; }

    public OrderBookPolicyViolationCode? LastViolationCode { get; private set; }

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
        ValidateIncomingUpdate(update);

        var bids = new SortedDictionary<decimal, decimal>(BidComparer);
        var asks = new SortedDictionary<decimal, decimal>();

        try
        {
            ApplyValidatedLevels(bids, update.Bids);
            ApplyValidatedLevels(asks, update.Asks);
            ValidateResult(bids, asks);
        }
        catch (OrderBookPolicyViolationException exception)
        {
            RecordRejection(exception.Code);
            throw;
        }

        _bids = bids;
        _asks = asks;
        _symbol = update.Symbol;
        LastUpdateId = update.UpdateId;
        LastCrossSequence = update.CrossSequence;
        HasSnapshot = true;
    }

    public bool TryApplyDelta(OrderBookUpdate update)
    {
        ValidateIncomingUpdate(update);

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

        try
        {
            ValidateDeltaResult(update);
        }
        catch (OrderBookPolicyViolationException exception)
        {
            RecordRejection(exception.Code);
            throw;
        }

        ApplyValidatedLevels(_bids, update.Bids);
        ApplyValidatedLevels(_asks, update.Asks);
        LastUpdateId = update.UpdateId;
        LastCrossSequence = update.CrossSequence;
        return true;
    }

    public void ValidateIncomingUpdate(OrderBookUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        try
        {
            ValidateEnvelope(update);
            ValidateLevels(update.Bids);
            ValidateLevels(update.Asks);
        }
        catch (OrderBookPolicyViolationException exception)
        {
            RecordRejection(exception.Code);
            throw;
        }
    }

    public OrderBookSnapshot Capture(int depth)
    {
        if (!HasSnapshot || _symbol is null)
        {
            throw new InvalidOperationException("An order-book snapshot has not been applied.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);

        if (depth > _capacity.MaximumLevelsPerSide)
        {
            throw new ArgumentOutOfRangeException(
                nameof(depth),
                "Capture depth exceeds the configured order-book capacity.");
        }

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

    private static void ApplyValidatedLevels(
        SortedDictionary<decimal, decimal> target,
        IReadOnlyList<OrderBookLevel> levels)
    {
        foreach (var level in levels)
        {
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

    private void ValidateLevels(IReadOnlyList<OrderBookLevel> levels)
    {
        var prices = new HashSet<decimal>();

        foreach (var level in levels)
        {
            if (!prices.Add(level.Price))
            {
                throw new OrderBookPolicyViolationException(
                    OrderBookPolicyViolationCode.DuplicatePrice);
            }

            if (level.Price <= 0 || level.Quantity < 0)
            {
                throw new OrderBookPolicyViolationException(
                    OrderBookPolicyViolationCode.InvalidLevel);
            }

            if (_capacity.MaximumPrice is { } maximumPrice &&
                level.Price > maximumPrice)
            {
                throw new OrderBookPolicyViolationException(
                    OrderBookPolicyViolationCode.PriceLimitExceeded);
            }
        }
    }

    private void ValidateEnvelope(OrderBookUpdate update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(update.Symbol);
        ArgumentOutOfRangeException.ThrowIfNegative(update.UpdateId);
        ArgumentOutOfRangeException.ThrowIfNegative(update.CrossSequence);
        ArgumentNullException.ThrowIfNull(update.Bids);
        ArgumentNullException.ThrowIfNull(update.Asks);

        var totalLevels = (long)update.Bids.Count + update.Asks.Count;
        if (totalLevels > _capacity.MaximumLevelsPerUpdate)
        {
            throw new OrderBookPolicyViolationException(
                OrderBookPolicyViolationCode.UpdateTooLarge);
        }
    }

    private void ValidateResult(
        SortedDictionary<decimal, decimal> bids,
        SortedDictionary<decimal, decimal> asks)
    {
        if (bids.Count > _capacity.MaximumLevelsPerSide ||
            asks.Count > _capacity.MaximumLevelsPerSide)
        {
            throw new OrderBookPolicyViolationException(
                OrderBookPolicyViolationCode.SideCapacityExceeded);
        }

        if (bids.Count > 0 && asks.Count > 0 && bids.First().Key >= asks.First().Key)
        {
            throw new OrderBookPolicyViolationException(
                OrderBookPolicyViolationCode.CrossedBook);
        }
    }

    private void ValidateDeltaResult(OrderBookUpdate update)
    {
        var bidChanges = update.Bids.ToDictionary(
            static level => level.Price,
            static level => level.Quantity);
        var askChanges = update.Asks.ToDictionary(
            static level => level.Price,
            static level => level.Quantity);

        if (ProjectedCount(_bids, bidChanges) > _capacity.MaximumLevelsPerSide ||
            ProjectedCount(_asks, askChanges) > _capacity.MaximumLevelsPerSide)
        {
            throw new OrderBookPolicyViolationException(
                OrderBookPolicyViolationCode.SideCapacityExceeded);
        }

        var bestBid = ProjectedBest(_bids, bidChanges, isBid: true);
        var bestAsk = ProjectedBest(_asks, askChanges, isBid: false);
        if (bestBid is { } bid && bestAsk is { } ask && bid >= ask)
        {
            throw new OrderBookPolicyViolationException(
                OrderBookPolicyViolationCode.CrossedBook);
        }
    }

    private static int ProjectedCount(
        SortedDictionary<decimal, decimal> current,
        IReadOnlyDictionary<decimal, decimal> changes)
    {
        var count = current.Count;
        foreach (var (price, quantity) in changes)
        {
            var exists = current.ContainsKey(price);
            if (quantity == 0 && exists)
            {
                count--;
            }
            else if (quantity > 0 && !exists)
            {
                count++;
            }
        }
        return count;
    }

    private static decimal? ProjectedBest(
        SortedDictionary<decimal, decimal> current,
        IReadOnlyDictionary<decimal, decimal> changes,
        bool isBid)
    {
        decimal? best = null;

        foreach (var (price, _) in current)
        {
            if (!changes.TryGetValue(price, out var quantity) || quantity > 0)
            {
                best = price;
                break;
            }
        }

        foreach (var (price, quantity) in changes)
        {
            if (quantity == 0)
            {
                continue;
            }

            if (best is { } currentBest &&
                (isBid ? price <= currentBest : price >= currentBest))
            {
                continue;
            }

            best = price;
        }

        return best;
    }

    private void RecordRejection(OrderBookPolicyViolationCode code)
    {
        RejectedUpdateCount++;
        LastViolationCode = code;
    }
}
