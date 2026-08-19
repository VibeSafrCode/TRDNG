using Trdng.Core.MarketData;

namespace Trdng.Mexc.MarketData;

public sealed class MexcOrderBookSession
{
    private readonly List<MexcDepthDelta> _buffer = [];
    private readonly string _symbol;
    private readonly int _maxBufferedDeltas;
    private OrderBookUpdate? _pendingSnapshot;

    public MexcOrderBookSession(
        OrderBookEngine engine,
        string symbol,
        int maxBufferedDeltas = 2_048)
    {
        Engine = engine ?? throw new ArgumentNullException(nameof(engine));
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBufferedDeltas);
        _symbol = symbol.ToUpperInvariant();
        _maxBufferedDeltas = maxBufferedDeltas;
    }

    public OrderBookEngine Engine { get; }
    public MexcOrderBookSessionState State { get; private set; } =
        MexcOrderBookSessionState.WaitingForSnapshot;
    public long LastVersion { get; private set; }
    public string LastDecision { get; private set; } = "not-started";

    public MexcOrderBookApplyResult BufferOrApply(MexcDepthDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        Validate(delta);
        if (!string.Equals(delta.Symbol, _symbol, StringComparison.Ordinal))
            throw new InvalidDataException("MEXC delta symbol does not match this session.");

        if (State == MexcOrderBookSessionState.ResyncRequired)
        {
            LastDecision = "already-resync-required";
            return MexcOrderBookApplyResult.ResyncRequired;
        }

        if (State == MexcOrderBookSessionState.WaitingForSnapshot)
        {
            if (_buffer.Count == _maxBufferedDeltas)
            {
                LastDecision = $"buffer-cap:{_maxBufferedDeltas}";
                return RequireResync();
            }
            _buffer.Add(delta);
            LastDecision = $"buffered:{delta.FromVersion}-{delta.ToVersion}";
            return _pendingSnapshot is null
                ? MexcOrderBookApplyResult.Buffered
                : TryActivatePendingSnapshot();
        }

        if (delta.ToVersion <= LastVersion)
        {
            LastDecision = $"stale:to={delta.ToVersion}<={LastVersion}";
            return MexcOrderBookApplyResult.IgnoredStale;
        }

        if (delta.FromVersion != LastVersion + 1)
        {
            LastDecision = $"post-bridge-gap:from={delta.FromVersion},expected={LastVersion + 1},to={delta.ToVersion}";
            return RequireResync();
        }

        ApplyDelta(delta);
        LastDecision = $"delta-applied:{delta.FromVersion}-{delta.ToVersion}";
        return MexcOrderBookApplyResult.DeltaApplied;
    }

    public MexcOrderBookApplyResult ApplySnapshot(OrderBookUpdate snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!string.Equals(snapshot.Symbol, _symbol, StringComparison.Ordinal))
            throw new InvalidDataException("MEXC snapshot symbol does not match this session.");
        _pendingSnapshot = snapshot;
        LastDecision = $"snapshot-pending:{snapshot.UpdateId}";
        return TryActivatePendingSnapshot();
    }

    private MexcOrderBookApplyResult TryActivatePendingSnapshot()
    {
        var snapshot = _pendingSnapshot
            ?? throw new InvalidOperationException("No pending MEXC snapshot.");

        var relevant = _buffer.Where(item => item.ToVersion >= snapshot.UpdateId).ToArray();
        if (relevant.Length == 0)
        {
            LastDecision = $"no-relevant-yet:snapshot={snapshot.UpdateId},buffered={_buffer.Count}";
            return MexcOrderBookApplyResult.Buffered;
        }

        var bridgeIndex = Array.FindIndex(relevant,
            item =>
                (item.FromVersion <= snapshot.UpdateId && snapshot.UpdateId <= item.ToVersion) ||
                item.FromVersion == snapshot.UpdateId + 1);
        if (bridgeIndex < 0)
        {
            LastDecision = $"initial-gap:snapshot={snapshot.UpdateId},expected-at-most={snapshot.UpdateId + 1},first={relevant[0].FromVersion}-{relevant[0].ToVersion}";
            return RequireResync();
        }

        Engine.ApplySnapshot(snapshot);
        LastVersion = snapshot.UpdateId;
        ApplyDelta(relevant[bridgeIndex]);
        for (var index = bridgeIndex + 1; index < relevant.Length; index++)
        {
            var next = relevant[index];
            if (next.ToVersion <= LastVersion) continue;
            if (next.FromVersion != LastVersion + 1)
            {
                LastDecision = $"buffered-post-bridge-gap:from={next.FromVersion},expected={LastVersion + 1},to={next.ToVersion}";
                return RequireResync();
            }
            ApplyDelta(next);
        }

        State = MexcOrderBookSessionState.Live;
        _pendingSnapshot = null;
        _buffer.Clear();
        LastDecision = $"bridge-success:snapshot={snapshot.UpdateId},last={LastVersion}";
        return MexcOrderBookApplyResult.SnapshotApplied;
    }

    public void OnDisconnected()
    {
        Engine.Reset();
        _buffer.Clear();
        _pendingSnapshot = null;
        LastVersion = 0;
        State = MexcOrderBookSessionState.WaitingForSnapshot;
        LastDecision = "disconnected-reset";
    }

    private void ApplyDelta(MexcDepthDelta delta)
    {
        var update = new OrderBookUpdate(
            delta.Symbol,
            delta.ToVersion,
            delta.ToVersion,
            delta.Bids,
            delta.Asks);
        if (Engine.LastUpdateId == delta.ToVersion)
            return;
        if (!Engine.TryApplyDelta(update))
        {
            LastDecision = $"engine-rejected:from={delta.FromVersion},to={delta.ToVersion},engine={Engine.LastUpdateId}";
            throw new InvalidDataException("MEXC delta could not be applied.");
        }
        LastVersion = delta.ToVersion;
    }

    private MexcOrderBookApplyResult RequireResync()
    {
        Engine.Reset();
        _buffer.Clear();
        _pendingSnapshot = null;
        LastVersion = 0;
        State = MexcOrderBookSessionState.ResyncRequired;
        return MexcOrderBookApplyResult.ResyncRequired;
    }

    private static void Validate(MexcDepthDelta delta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(delta.Symbol);
        if (delta.FromVersion < 0 || delta.ToVersion < delta.FromVersion)
            throw new InvalidDataException("Invalid MEXC depth version range.");
    }
}
