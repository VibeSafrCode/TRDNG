using Trdng.Core.MarketData;

namespace Trdng.Mexc.MarketData;

public sealed class MexcOrderBookSession
{
    private readonly List<MexcDepthDelta> _buffer = [];
    private readonly string _symbol;
    private readonly int _maxBufferedDeltas;
    private readonly int _maxBufferedLevels;
    private OrderBookUpdate? _pendingSnapshot;
    private int _bufferedLevelCount;

    public MexcOrderBookSession(
        OrderBookEngine engine,
        string symbol,
        int maxBufferedDeltas = 2_048,
        int maxBufferedLevels = 20_000)
    {
        Engine = engine ?? throw new ArgumentNullException(nameof(engine));
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBufferedDeltas);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBufferedLevels);
        _symbol = symbol.ToUpperInvariant();
        _maxBufferedDeltas = maxBufferedDeltas;
        _maxBufferedLevels = maxBufferedLevels;
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
            try
            {
                Engine.ValidateIncomingUpdate(ToUpdate(delta));
            }
            catch (OrderBookPolicyViolationException exception)
            {
                LastDecision = $"capacity:{exception.SafeCode}";
                return RequireResync();
            }

            var deltaLevelCount = (long)delta.Bids.Count + delta.Asks.Count;
            if (_buffer.Count == _maxBufferedDeltas)
            {
                LastDecision = $"buffer-cap:{_maxBufferedDeltas}";
                return RequireResync();
            }
            if ((long)_bufferedLevelCount + deltaLevelCount > _maxBufferedLevels)
            {
                LastDecision = $"buffer-level-cap:{_maxBufferedLevels}";
                return RequireResync();
            }
            _buffer.Add(delta);
            _bufferedLevelCount += (int)deltaLevelCount;
            LastDecision = $"buffered:{delta.FromVersion}-{delta.ToVersion}";
            if (_pendingSnapshot is null)
            {
                return MexcOrderBookApplyResult.Buffered;
            }

            try
            {
                return TryActivatePendingSnapshot();
            }
            catch (OrderBookPolicyViolationException exception)
            {
                LastDecision = $"capacity:{exception.SafeCode}";
                return RequireResync();
            }
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

        try
        {
            ApplyDelta(delta);
        }
        catch (OrderBookPolicyViolationException exception)
        {
            LastDecision = $"capacity:{exception.SafeCode}";
            return RequireResync();
        }
        LastDecision = $"delta-applied:{delta.FromVersion}-{delta.ToVersion}";
        return MexcOrderBookApplyResult.DeltaApplied;
    }

    public MexcOrderBookApplyResult ApplySnapshot(OrderBookUpdate snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!string.Equals(snapshot.Symbol, _symbol, StringComparison.Ordinal))
            throw new InvalidDataException("MEXC snapshot symbol does not match this session.");

        try
        {
            Engine.ValidateIncomingUpdate(snapshot);
        }
        catch (OrderBookPolicyViolationException exception)
        {
            LastDecision = $"capacity:{exception.SafeCode}";
            return RequireResync();
        }

        _pendingSnapshot = snapshot;
        LastDecision = $"snapshot-pending:{snapshot.UpdateId}";
        try
        {
            return TryActivatePendingSnapshot();
        }
        catch (OrderBookPolicyViolationException exception)
        {
            LastDecision = $"capacity:{exception.SafeCode}";
            return RequireResync();
        }
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
        _bufferedLevelCount = 0;
        LastDecision = $"bridge-success:snapshot={snapshot.UpdateId},last={LastVersion}";
        return MexcOrderBookApplyResult.SnapshotApplied;
    }

    public void OnDisconnected()
    {
        Engine.Reset();
        _buffer.Clear();
        _bufferedLevelCount = 0;
        _pendingSnapshot = null;
        LastVersion = 0;
        State = MexcOrderBookSessionState.WaitingForSnapshot;
        LastDecision = "disconnected-reset";
    }

    private void ApplyDelta(MexcDepthDelta delta)
    {
        var update = ToUpdate(delta);
        if (Engine.LastUpdateId == delta.ToVersion)
            return;
        if (!Engine.TryApplyDelta(update))
        {
            LastDecision = $"engine-rejected:from={delta.FromVersion},to={delta.ToVersion},engine={Engine.LastUpdateId}";
            throw new InvalidDataException("MEXC delta could not be applied.");
        }
        LastVersion = delta.ToVersion;
    }

    private static OrderBookUpdate ToUpdate(MexcDepthDelta delta) => new(
        delta.Symbol,
        delta.ToVersion,
        delta.ToVersion,
        delta.Bids,
        delta.Asks);

    private MexcOrderBookApplyResult RequireResync()
    {
        Engine.Reset();
        _buffer.Clear();
        _bufferedLevelCount = 0;
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
