using Trdng.Core.MarketData;

namespace Trdng.Core.Clusters;

public enum TradeClusterIngestResult
{
    Accepted,
    IgnoredOutOfOrder,
    CapacityExceeded,
    IgnoredOverflowedInterval
}

public readonly record struct TradeClusterCapacityMetrics(
    int CurrentPriceLevelCount,
    int CurrentTradeCount,
    bool IsCurrentIntervalOverflowed,
    long AcceptedTradeCount,
    long RejectedTradeCount,
    long IgnoredOutOfOrderTradeCount,
    long OverflowedClusterCount);

public sealed class TradeClusterAggregator
{
    public const int DefaultMaximumPriceLevelsPerCluster = 4_096;
    public const int DefaultMaximumTradesPerCluster = 100_000;

    private readonly TimeSpan _interval;
    private readonly decimal _priceStep;
    private readonly int _maxCompletedClusters;
    private readonly int _maximumPriceLevelsPerCluster;
    private readonly int _maximumTradesPerCluster;
    private readonly LinkedList<TradeCluster> _completed = [];
    private readonly SortedDictionary<decimal, MutableLevel> _currentLevels =
        new(Comparer<decimal>.Create(static (left, right) => right.CompareTo(left)));
    private DateTimeOffset? _currentStartsAt;
    private int _currentTradeCount;
    private bool _currentIntervalOverflowed;
    private long _acceptedTradeCount;
    private long _rejectedTradeCount;
    private long _ignoredOutOfOrderTradeCount;
    private long _overflowedClusterCount;

    public TradeClusterAggregator(
        TimeSpan interval,
        decimal priceStep,
        int maxCompletedClusters = 240,
        int maximumPriceLevelsPerCluster = DefaultMaximumPriceLevelsPerCluster,
        int maximumTradesPerCluster = DefaultMaximumTradesPerCluster)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        if (priceStep <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(priceStep));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCompletedClusters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPriceLevelsPerCluster);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTradesPerCluster);

        _interval = interval;
        _priceStep = priceStep;
        _maxCompletedClusters = maxCompletedClusters;
        _maximumPriceLevelsPerCluster = maximumPriceLevelsPerCluster;
        _maximumTradesPerCluster = maximumTradesPerCluster;
    }

    public IReadOnlyCollection<TradeCluster> Completed => _completed;

    public TradeClusterCapacityMetrics Metrics => new(
        _currentLevels.Count,
        _currentTradeCount,
        _currentIntervalOverflowed,
        _acceptedTradeCount,
        _rejectedTradeCount,
        _ignoredOutOfOrderTradeCount,
        _overflowedClusterCount);

    public TradeClusterIngestResult Ingest(PublicTrade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);

        if (trade.Price <= 0 || trade.Quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(trade),
                "Trade price and quantity must be positive.");
        }

        var bucketStart = FloorTime(trade.MatchedAt, _interval);

        if (_currentStartsAt is null)
        {
            _currentStartsAt = bucketStart;
        }
        else if (bucketStart < _currentStartsAt)
        {
            _ignoredOutOfOrderTradeCount++;
            return TradeClusterIngestResult.IgnoredOutOfOrder;
        }
        else if (bucketStart > _currentStartsAt)
        {
            CompleteCurrent();
            _currentStartsAt = bucketStart;
        }

        if (_currentIntervalOverflowed)
        {
            _rejectedTradeCount++;
            return TradeClusterIngestResult.IgnoredOverflowedInterval;
        }

        var price = FloorPrice(trade.Price, _priceStep);

        if (_currentTradeCount == _maximumTradesPerCluster ||
            (!_currentLevels.ContainsKey(price) &&
             _currentLevels.Count == _maximumPriceLevelsPerCluster))
        {
            _currentIntervalOverflowed = true;
            _overflowedClusterCount++;
            _rejectedTradeCount++;
            _currentLevels.Clear();
            return TradeClusterIngestResult.CapacityExceeded;
        }

        if (!_currentLevels.TryGetValue(price, out var level))
        {
            level = new MutableLevel();
            _currentLevels.Add(price, level);
        }

        if (trade.Aggressor == AggressorSide.Buy)
        {
            level.AskVolume += trade.Quantity;
        }
        else
        {
            level.BidVolume += trade.Quantity;
        }

        _currentTradeCount++;
        _acceptedTradeCount++;
        return TradeClusterIngestResult.Accepted;
    }

    public TradeCluster? CaptureCurrent()
    {
        if (_currentStartsAt is null || _currentIntervalOverflowed)
        {
            return null;
        }

        return CreateCluster(_currentStartsAt.Value);
    }

    public void Reset()
    {
        _completed.Clear();
        _currentLevels.Clear();
        _currentStartsAt = null;
        _currentTradeCount = 0;
        _currentIntervalOverflowed = false;
        _acceptedTradeCount = 0;
        _rejectedTradeCount = 0;
        _ignoredOutOfOrderTradeCount = 0;
        _overflowedClusterCount = 0;
    }

    private void CompleteCurrent()
    {
        if (_currentStartsAt is null)
        {
            return;
        }

        if (!_currentIntervalOverflowed)
        {
            _completed.AddLast(CreateCluster(_currentStartsAt.Value));

            while (_completed.Count > _maxCompletedClusters)
            {
                _completed.RemoveFirst();
            }
        }

        _currentLevels.Clear();
        _currentTradeCount = 0;
        _currentIntervalOverflowed = false;
    }

    private TradeCluster CreateCluster(DateTimeOffset startsAt) =>
        new(
            startsAt,
            _interval,
            _currentLevels.Select(static pair =>
                new ClusterLevel(
                    pair.Key,
                    pair.Value.BidVolume,
                    pair.Value.AskVolume)).ToArray());

    private static decimal FloorPrice(decimal price, decimal step) =>
        decimal.Floor(price / step) * step;

    private static DateTimeOffset FloorTime(DateTimeOffset value, TimeSpan interval)
    {
        var ticks = value.UtcTicks - (value.UtcTicks % interval.Ticks);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private sealed class MutableLevel
    {
        public decimal BidVolume { get; set; }

        public decimal AskVolume { get; set; }
    }
}
