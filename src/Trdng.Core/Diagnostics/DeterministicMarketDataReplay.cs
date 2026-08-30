using Trdng.Core.Clusters;
using Trdng.Core.Instruments;
using Trdng.Core.MarketData;

namespace Trdng.Core.Diagnostics;

public sealed record DeterministicReplayOptions
{
    public DeterministicReplayOptions(
        int cycles,
        int switchEveryCycles,
        int sampleEveryCycles,
        int bookDepth)
    {
        if (cycles is <= 0 or > 10_000_000)
            throw new ArgumentOutOfRangeException(nameof(cycles));
        if (switchEveryCycles is <= 0 || switchEveryCycles > cycles)
            throw new ArgumentOutOfRangeException(nameof(switchEveryCycles));
        if (sampleEveryCycles is <= 0 || sampleEveryCycles > cycles)
            throw new ArgumentOutOfRangeException(nameof(sampleEveryCycles));
        if (bookDepth is <= 1 or > 5_000)
            throw new ArgumentOutOfRangeException(nameof(bookDepth));
        Cycles = cycles;
        SwitchEveryCycles = switchEveryCycles;
        SampleEveryCycles = sampleEveryCycles;
        BookDepth = bookDepth;
    }

    public int Cycles { get; }
    public int SwitchEveryCycles { get; }
    public int SampleEveryCycles { get; }
    public int BookDepth { get; }
}

public sealed record DeterministicReplayResult(
    long Cycles,
    long AppliedBookUpdates,
    int MarketSwitches,
    int CreatedClients,
    int DisposedClients,
    int MaximumActiveClients,
    int MaximumObservedLevelsPerSide,
    int MaximumCompletedClusters,
    int MemorySamples);

public static class DeterministicMarketDataReplay
{
    private static readonly (string BaseAsset, MarketProduct Product)[] Markets =
    [
        ("BTC", MarketProduct.Perpetual),
        ("BTC", MarketProduct.Spot),
        ("SOL", MarketProduct.Perpetual),
        ("ETH", MarketProduct.Spot),
        ("APT", MarketProduct.Perpetual)
    ];

    public static async Task<DeterministicReplayResult> RunAsync(
        DeterministicReplayOptions options,
        RuntimeMemoryRecorder recorder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(recorder);

        var lifecycle = new ClientLifecycleCounters();
        var engines = Enumerable.Range(0, 3)
            .Select(_ => new OrderBookEngine(new OrderBookCapacityPolicy(
                options.BookDepth,
                checked(options.BookDepth * 2))))
            .ToArray();
        var clusters = Enumerable.Range(0, 3)
            .Select(_ => new TradeClusterAggregator(
                TimeSpan.FromSeconds(1),
                0.01m,
                maxCompletedClusters: 16,
                maximumPriceLevelsPerCluster: 64,
                maximumTradesPerCluster: 256))
            .ToArray();
        var controller = new MarketSelectionController(
            capability => lifecycle.Create(capability.Venue),
            Resolve);
        var marketIndex = 0;
        var switches = 0;
        var updateId = 1L;
        var maximumLevels = 0;
        var maximumClusters = 0;
        var appliedUpdates = 0L;

        controller.Resetting += () =>
        {
            foreach (var engine in engines) engine.Reset();
            foreach (var cluster in clusters) cluster.Reset();
        };

        recorder.Capture();
        try
        {
            for (var cycle = 0; cycle < options.Cycles; cycle++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (cycle % options.SwitchEveryCycles == 0)
                {
                    var market = Markets[marketIndex++ % Markets.Length];
                    if (!await controller.SelectAsync(market.BaseAsset, market.Product)
                            .ConfigureAwait(false))
                        throw new InvalidOperationException("Deterministic market switch was rejected.");
                    switches++;
                    var symbol = $"{market.BaseAsset}USDT";
                    foreach (var engine in engines)
                    {
                        engine.ApplySnapshot(Snapshot(symbol, updateId++, options.BookDepth));
                        appliedUpdates++;
                    }
                }

                var selected = controller.SelectedInstrument ??
                    throw new InvalidOperationException("Replay selection is missing.");
                var selectedSymbol = $"{selected.BaseAsset}USDT";
                for (var venue = 0; venue < engines.Length; venue++)
                {
                    var slot = cycle % options.BookDepth;
                    var bid = 100m - slot * 0.001m;
                    var ask = 101m + slot * 0.001m;
                    if (!engines[venue].TryApplyDelta(new(
                            selectedSymbol,
                            updateId,
                            updateId,
                            [new(bid, 1m + venue)],
                            [new(ask, 2m + venue)])))
                        throw new InvalidOperationException("Deterministic delta was rejected.");
                    appliedUpdates++;

                    clusters[venue].Ingest(new(
                        $"{venue}-{cycle}",
                        selectedSymbol,
                        DateTimeOffset.UnixEpoch.AddMilliseconds(cycle * 10L),
                        cycle % 2 == 0 ? AggressorSide.Buy : AggressorSide.Sell,
                        100m + (cycle % 32) * 0.01m,
                        1m,
                        updateId));
                }
                updateId++;

                if ((cycle + 1) % options.SampleEveryCycles == 0)
                {
                    foreach (var engine in engines)
                        maximumLevels = Math.Max(maximumLevels,
                            Math.Max(engine.Capture(options.BookDepth).Bids.Count,
                                engine.Capture(options.BookDepth).Asks.Count));
                    maximumClusters = Math.Max(maximumClusters,
                        clusters.Max(static cluster => cluster.Completed.Count));
                    recorder.Capture();
                }
            }
        }
        finally
        {
            await controller.DisposeAsync().ConfigureAwait(false);
        }

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        recorder.Capture();
        return new(
            options.Cycles,
            appliedUpdates,
            switches,
            lifecycle.Created,
            lifecycle.Disposed,
            lifecycle.MaximumActive,
            maximumLevels,
            maximumClusters,
            recorder.Samples.Count);
    }

    private static OrderBookUpdate Snapshot(string symbol, long updateId, int depth)
    {
        var bids = new OrderBookLevel[depth];
        var asks = new OrderBookLevel[depth];
        for (var index = 0; index < depth; index++)
        {
            bids[index] = new(100m - index * 0.001m, 1m);
            asks[index] = new(101m + index * 0.001m, 1m);
        }
        return new(symbol, updateId, updateId, bids, asks);
    }

    private static VenueInstrumentCapability? Resolve(
        CanonicalInstrument instrument,
        TradingVenue venue)
    {
        var available = instrument.Product switch
        {
            MarketProduct.Spot => venue == TradingVenue.Mexc,
            MarketProduct.Perpetual => venue is TradingVenue.Bybit or TradingVenue.Gate,
            _ => false
        };
        return available
            ? new(instrument, venue,
                venue == TradingVenue.Gate
                    ? $"{instrument.BaseAsset}_{instrument.QuoteAsset}"
                    : $"{instrument.BaseAsset}{instrument.QuoteAsset}",
                CapabilityAvailability.Available,
                CapabilityAvailability.NotImplemented)
            : null;
    }

    private sealed class ClientLifecycleCounters
    {
        private int _active;
        public int Created { get; private set; }
        public int Disposed { get; private set; }
        public int MaximumActive { get; private set; }

        public IPublicMarketDataClient Create(TradingVenue venue)
        {
            Created++;
            _active++;
            MaximumActive = Math.Max(MaximumActive, _active);
            return new ReplayClient(venue, () =>
            {
                Disposed++;
                _active--;
            });
        }
    }

    private sealed class ReplayClient(TradingVenue venue, Action onDispose)
        : IPublicMarketDataClient
    {
        private bool _disposed;
        public string Venue => venue.ToString().ToUpperInvariant();
        public event Action<OrderBookSnapshot>? SnapshotReceived { add { } remove { } }
        public event Action<TradeCluster>? ClusterReceived { add { } remove { } }
        public event Action<IReadOnlyList<PublicTrade>>? TradesReceived { add { } remove { } }
        public event Action<MarketDataConnectionState, string?>? StateChanged { add { } remove { } }
        public void Start() { }
        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                onDispose();
            }
            return ValueTask.CompletedTask;
        }
    }
}
