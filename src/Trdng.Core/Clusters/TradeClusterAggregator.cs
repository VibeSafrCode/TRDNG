using Trdng.Core.MarketData;

namespace Trdng.Core.Clusters;

public sealed class TradeClusterAggregator
{
    private readonly TimeSpan _interval;
    private readonly decimal _priceStep;
    private readonly int _maxCompletedClusters;
    private readonly LinkedList<TradeCluster> _completed = [];
    private readonly SortedDictionary<decimal, MutableLevel> _currentLevels =
        new(Comparer<decimal>.Create(static (left, right) => right.CompareTo(left)));
    private DateTimeOffset? _currentStartsAt;

    public TradeClusterAggregator(
        TimeSpan interval,
        decimal priceStep,
        int maxCompletedClusters = 240)
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

        _interval = interval;
        _priceStep = priceStep;
        _maxCompletedClusters = maxCompletedClusters;
    }

    public IReadOnlyCollection<TradeCluster> Completed => _completed;

    public void Ingest(PublicTrade trade)
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
            return;
        }
        else if (bucketStart > _currentStartsAt)
        {
            CompleteCurrent();
            _currentStartsAt = bucketStart;
        }

        var price = FloorPrice(trade.Price, _priceStep);

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
    }

    public TradeCluster? CaptureCurrent()
    {
        if (_currentStartsAt is null)
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
    }

    private void CompleteCurrent()
    {
        if (_currentStartsAt is null)
        {
            return;
        }

        _completed.AddLast(CreateCluster(_currentStartsAt.Value));

        while (_completed.Count > _maxCompletedClusters)
        {
            _completed.RemoveFirst();
        }

        _currentLevels.Clear();
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
