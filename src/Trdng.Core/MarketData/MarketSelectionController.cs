using Trdng.Core.Instruments;

namespace Trdng.Core.MarketData;

public sealed class MarketSelectionController : IAsyncDisposable
{
    private static readonly TradingVenue[] VenueOrder =
        [TradingVenue.Bybit, TradingVenue.Gate, TradingVenue.Mexc];

    private readonly Func<VenueInstrumentCapability, IPublicMarketDataClient> _factory;
    private readonly SemaphoreSlim _switchGate = new(1, 1);
    private readonly List<IPublicMarketDataClient> _clients = [];
    private long _latestRequestId;

    public MarketSelectionController(
        Func<VenueInstrumentCapability, IPublicMarketDataClient> factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public CanonicalInstrument? SelectedInstrument { get; private set; }
    public IReadOnlyList<IPublicMarketDataClient> Clients => _clients;
    public event Action? Resetting;

    public async Task<bool> SelectAsync(string baseAsset, MarketProduct product)
    {
        var requestId = Interlocked.Increment(ref _latestRequestId);
        var instrument = new CanonicalInstrument(baseAsset, "USDT", product);
        await _switchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (requestId != Volatile.Read(ref _latestRequestId)) return false;
            if (SelectedInstrument == instrument && _clients.Count != 0) return false;

            Resetting?.Invoke();
            foreach (var client in _clients) await client.DisposeAsync().ConfigureAwait(false);
            _clients.Clear();
            if (requestId != Volatile.Read(ref _latestRequestId)) return false;
            SelectedInstrument = instrument;

            foreach (var venue in VenueOrder)
            {
                var capability = StarterInstrumentCatalog.Find(instrument, venue);
                if (capability?.CanStreamMarketData != true) continue;
                _clients.Add(_factory(capability));
            }
            return true;
        }
        finally { _switchGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _switchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var client in _clients) await client.DisposeAsync().ConfigureAwait(false);
            _clients.Clear();
        }
        finally
        {
            _switchGate.Release();
            _switchGate.Dispose();
        }
    }
}
