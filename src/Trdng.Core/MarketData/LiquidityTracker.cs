namespace Trdng.Core.MarketData;

public sealed class LiquidityTracker
{
    private static readonly TimeSpan HoldingAge = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ChangeHighlightAge = TimeSpan.FromMilliseconds(900);
    private readonly Dictionary<LevelKey, TrackedLevel> _levels = [];

    public void ObserveTrades(IEnumerable<PublicTrade> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);

        foreach (var trade in trades)
        {
            var side = trade.Aggressor == AggressorSide.Buy
                ? LiquiditySide.Ask
                : LiquiditySide.Bid;

            if (_levels.TryGetValue(new LevelKey(side, trade.Price), out var tracked))
            {
                tracked.ExecutedVolume += trade.Quantity;
            }
        }
    }

    public IReadOnlyDictionary<(LiquiditySide Side, decimal Price), LiquidityLevelState> Observe(
        OrderBookSnapshot snapshot,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var result = new Dictionary<(LiquiditySide, decimal), LiquidityLevelState>();
        ObserveSide(snapshot.Bids, LiquiditySide.Bid, observedAt, result);
        ObserveSide(snapshot.Asks, LiquiditySide.Ask, observedAt, result);
        Prune(observedAt);
        return result;
    }

    public void Reset() => _levels.Clear();

    private void ObserveSide(
        IReadOnlyList<OrderBookLevel> levels,
        LiquiditySide side,
        DateTimeOffset observedAt,
        Dictionary<(LiquiditySide, decimal), LiquidityLevelState> result)
    {
        foreach (var level in levels)
        {
            var key = new LevelKey(side, level.Price);

            if (!_levels.TryGetValue(key, out var tracked))
            {
                tracked = new TrackedLevel(level.Quantity, observedAt);
                _levels.Add(key, tracked);
            }

            var previousQuantity = tracked.Quantity;
            var changeRatio = previousQuantity == 0
                ? 0
                : (level.Quantity - previousQuantity) / previousQuantity;

            if (changeRatio >= 0.25m)
            {
                tracked.LastBehavior = LiquidityBehavior.Building;
                tracked.BehaviorChangedAt = observedAt;
            }
            else if (changeRatio <= -0.25m)
            {
                var removed = previousQuantity - level.Quantity;
                tracked.LastBehavior = tracked.ExecutedVolume >= removed * 0.5m
                    ? LiquidityBehavior.Absorbing
                    : LiquidityBehavior.Pulling;
                tracked.BehaviorChangedAt = observedAt;
            }
            else if (tracked.ExecutedVolume >= decimal.Max(0.01m, level.Quantity * 0.25m) &&
                     level.Quantity >= previousQuantity * 0.75m)
            {
                tracked.LastBehavior = LiquidityBehavior.Absorbing;
                tracked.BehaviorChangedAt = observedAt;
            }

            tracked.Quantity = level.Quantity;
            tracked.LastSeenAt = observedAt;

            var behavior = observedAt - tracked.BehaviorChangedAt <= ChangeHighlightAge
                ? tracked.LastBehavior
                : observedAt - tracked.FirstSeenAt >= HoldingAge
                    ? LiquidityBehavior.Holding
                    : LiquidityBehavior.Normal;

            result[(side, level.Price)] = new LiquidityLevelState(
                side,
                level.Price,
                level.Quantity,
                observedAt - tracked.FirstSeenAt,
                tracked.ExecutedVolume,
                behavior);

            // Execution is evaluated against the next visible book update.
            tracked.ExecutedVolume = 0;
        }
    }

    private void Prune(DateTimeOffset observedAt)
    {
        foreach (var key in _levels
                     .Where(pair => observedAt - pair.Value.LastSeenAt > TimeSpan.FromSeconds(3))
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _levels.Remove(key);
        }
    }

    private readonly record struct LevelKey(LiquiditySide Side, decimal Price);

    private sealed class TrackedLevel(decimal quantity, DateTimeOffset observedAt)
    {
        public decimal Quantity { get; set; } = quantity;

        public DateTimeOffset FirstSeenAt { get; } = observedAt;

        public DateTimeOffset LastSeenAt { get; set; } = observedAt;

        public decimal ExecutedVolume { get; set; }

        public LiquidityBehavior LastBehavior { get; set; } = LiquidityBehavior.Normal;

        public DateTimeOffset BehaviorChangedAt { get; set; } = DateTimeOffset.MinValue;
    }
}
