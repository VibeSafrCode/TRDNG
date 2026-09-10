using Trdng.Core.Instruments;

namespace Trdng.Core.MarketData;

public sealed class MarketSelectionController : IAsyncDisposable
{
    private static readonly TradingVenue[] VenueOrder =
        [TradingVenue.Bybit, TradingVenue.Gate, TradingVenue.Mexc];

    private readonly Func<VenueInstrumentCapability, IPublicMarketDataClient> _factory;
    private readonly Func<CanonicalInstrument, TradingVenue, VenueInstrumentCapability?> _resolver;
    private readonly SemaphoreSlim _switchGate = new(1, 1);
    private readonly List<IPublicMarketDataClient> _clients = [];
    private long _latestRequestId;
    private int _disposeStarted;

    public MarketSelectionController(
        Func<VenueInstrumentCapability, IPublicMarketDataClient> factory,
        Func<CanonicalInstrument, TradingVenue, VenueInstrumentCapability?>? resolver = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _resolver = resolver ?? StarterInstrumentCatalog.Find;
    }

    public CanonicalInstrument? SelectedInstrument { get; private set; }
    public IReadOnlyList<IPublicMarketDataClient> Clients => _clients;
    public event Action? Resetting;

    public async Task<bool> SelectAsync(string baseAsset, MarketProduct product)
        => await SelectAsync(baseAsset, "USDT", product).ConfigureAwait(false);

    public async Task<bool> SelectAsync(string baseAsset, string quoteAsset, MarketProduct product)
        => await SelectAsync(baseAsset, quoteAsset, product, forceRefresh: false,
            CancellationToken.None).ConfigureAwait(false);

    public async Task<bool> SelectAsync(
        string baseAsset,
        string quoteAsset,
        MarketProduct product,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);
        var requestId = Interlocked.Increment(ref _latestRequestId);
        var instrument = new CanonicalInstrument(baseAsset, quoteAsset, product);
        await _switchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (requestId != Volatile.Read(ref _latestRequestId)) return false;
            if (!forceRefresh && SelectedInstrument == instrument && _clients.Count != 0)
                return false;

            var capabilities = VenueOrder
                .Select(venue => _resolver(instrument, venue))
                .Where(capability => capability?.CanStreamMarketData == true)
                .Cast<VenueInstrumentCapability>()
                .ToArray();
            if (capabilities.Length == 0) return false;

            var staged = new List<IPublicMarketDataClient>(capabilities.Length);
            try
            {
                foreach (var capability in capabilities)
                    staged.Add(_factory(capability));
            }
            catch
            {
                await DisposeClientsBestEffortAsync(staged).ConfigureAwait(false);
                throw;
            }

            var transferred = false;
            try
            {
                Resetting?.Invoke();
                var disposalFailure = await DisposeClientsBestEffortAsync(_clients)
                    .ConfigureAwait(false);
                _clients.Clear();
                SelectedInstrument = null;
                if (disposalFailure is not null)
                    throw new InvalidOperationException(
                        "Previous market-data clients could not be disposed safely.",
                        disposalFailure);
                cancellationToken.ThrowIfCancellationRequested();
                if (requestId != Volatile.Read(ref _latestRequestId)) return false;
                SelectedInstrument = instrument;
                _clients.AddRange(staged);
                transferred = true;
                return true;
            }
            finally
            {
                if (!transferred)
                    await DisposeClientsBestEffortAsync(staged).ConfigureAwait(false);
            }
        }
        finally { _switchGate.Release(); }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);
        Interlocked.Increment(ref _latestRequestId);
        await _switchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Resetting?.Invoke();
            var failure = await DisposeClientsBestEffortAsync(_clients).ConfigureAwait(false);
            _clients.Clear();
            SelectedInstrument = null;
            if (failure is not null)
                throw new InvalidOperationException(
                    "Market-data clients could not be cleared safely.", failure);
        }
        finally { _switchGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
        await _switchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _ = await DisposeClientsBestEffortAsync(_clients).ConfigureAwait(false);
            _clients.Clear();
            SelectedInstrument = null;
        }
        finally
        {
            _switchGate.Release();
            _switchGate.Dispose();
        }
    }

    private static async Task<Exception?> DisposeClientsBestEffortAsync(
        IEnumerable<IPublicMarketDataClient> clients)
    {
        Exception? first = null;
        foreach (var client in clients)
        {
            try { await client.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) when (exception is not (OutOfMemoryException or
                StackOverflowException or AccessViolationException))
            {
                first ??= exception;
            }
        }
        return first;
    }
}
