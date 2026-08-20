using Trdng.Core.Clusters;
using Trdng.Core.Instruments;
using Trdng.Core.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class MarketSelectionControllerTests
{
    [Fact]
    public async Task SelectionCreatesOnlySupportedPerpetualClientsAndResetsAtomically()
    {
        var created = new List<FakeClient>();
        await using var controller = new MarketSelectionController(capability =>
        {
            var client = new FakeClient(capability.Venue, capability.VenueSymbol);
            created.Add(client);
            return client;
        });
        var resets = 0;
        controller.Resetting += () => resets++;

        Assert.True(await controller.SelectAsync("APT", MarketProduct.Perpetual));
        Assert.Equal("APT/USDT:PERPETUAL", controller.SelectedInstrument!.Value.Id);
        Assert.Equal(2, controller.Clients.Count);
        Assert.DoesNotContain(created, client => client.TradingVenue == TradingVenue.Mexc);

        Assert.True(await controller.SelectAsync("BTC", MarketProduct.Perpetual));
        Assert.Equal(2, resets);
        Assert.All(created.Take(2), client => Assert.True(client.Disposed));
        Assert.Equal(["BYBIT", "GATE"], controller.Clients.Select(client => client.Venue));
    }

    [Fact]
    public async Task SelectingSameAssetDoesNotCreateDuplicateClients()
    {
        var count = 0;
        await using var controller = new MarketSelectionController(capability =>
        {
            count++;
            return new FakeClient(capability.Venue, capability.VenueSymbol);
        });
        await controller.SelectAsync("APT", MarketProduct.Perpetual);
        Assert.False(await controller.SelectAsync("APT", MarketProduct.Perpetual));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task RapidAptBtcAptSwitchKeepsOnlyFinalGenerationActive()
    {
        var created = new List<FakeClient>();
        await using var controller = new MarketSelectionController(capability =>
        {
            var client = new FakeClient(capability.Venue, capability.VenueSymbol);
            created.Add(client);
            return client;
        });

        await controller.SelectAsync("APT", MarketProduct.Perpetual);
        await Task.WhenAll(
            controller.SelectAsync("BTC", MarketProduct.Spot),
            controller.SelectAsync("APT", MarketProduct.Perpetual));

        Assert.Equal("APT/USDT:PERPETUAL", controller.SelectedInstrument!.Value.Id);
        Assert.Equal(2, controller.Clients.Count);
        Assert.All(created.Except(controller.Clients.Cast<FakeClient>()), client => Assert.True(client.Disposed));
        Assert.All(controller.Clients.Cast<FakeClient>(), client => Assert.False(client.Disposed));
    }

    [Fact]
    public async Task SpotCreatesOnlyMexcAndNeverPerpetualClients()
    {
        var created = new List<FakeClient>();
        await using var controller = new MarketSelectionController(capability =>
        {
            var client = new FakeClient(capability.Venue, capability.VenueSymbol);
            created.Add(client);
            return client;
        });

        await controller.SelectAsync("BTC", MarketProduct.Spot);

        Assert.Equal("BTC/USDT:SPOT", controller.SelectedInstrument!.Value.Id);
        Assert.Single(controller.Clients);
        Assert.Equal(TradingVenue.Mexc, created.Single().TradingVenue);
        Assert.Equal("BTCUSDT", created.Single().Symbol);
    }

    [Fact]
    public async Task ResetClearsOldBookBeforeNewClientsAreCreated()
    {
        var visibleBook = new List<int>();
        await using var controller = new MarketSelectionController(capability =>
        {
            Assert.Empty(visibleBook);
            return new FakeClient(capability.Venue, capability.VenueSymbol);
        });
        controller.Resetting += visibleBook.Clear;
        await controller.SelectAsync("APT", MarketProduct.Perpetual);
        visibleBook.Add(1);

        await controller.SelectAsync("BTC", MarketProduct.Spot);

        Assert.Empty(visibleBook);
    }

    [Fact]
    public async Task LatestConcurrentProductRequestWinsEvenWhenEarlierDisposeIsDelayed()
    {
        var disposeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDispose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayDisposal = false;
        await using var controller = new MarketSelectionController(capability =>
            new FakeClient(capability.Venue, capability.VenueSymbol, async () =>
            {
                if (!delayDisposal) return;
                disposeEntered.TrySetResult();
                await releaseDispose.Task;
            }));
        await controller.SelectAsync("APT", MarketProduct.Perpetual);
        delayDisposal = true;

        var earlier = controller.SelectAsync("BTC", MarketProduct.Spot);
        await disposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var latest = controller.SelectAsync("APT", MarketProduct.Spot);
        releaseDispose.TrySetResult();
        await Task.WhenAll(earlier, latest);

        Assert.Equal("APT/USDT:SPOT", controller.SelectedInstrument!.Value.Id);
        Assert.Single(controller.Clients);
        Assert.Equal("MEXC", controller.Clients[0].Venue);
    }

    [Fact]
    public void VenueCardReportsStaleAndUnavailableHonestly()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal("STALE", VenueCardStatus.Resolve(
            MarketDataConnectionState.Live, now - TimeSpan.FromSeconds(3), now,
            MarketDataFreshnessOptions.ScalpingDefault));
        Assert.Equal("UNAVAILABLE", VenueCardStatus.Resolve(
            MarketDataConnectionState.Disconnected, null, now,
            MarketDataFreshnessOptions.ScalpingDefault));
    }

    [Fact]
    public void MexcPerpetualCapabilityRemainsUnavailableAndBlocked()
    {
        var capability = StarterInstrumentCatalog.Find(
            new CanonicalInstrument("APT", "USDT", MarketProduct.Perpetual), TradingVenue.Mexc);
        Assert.NotNull(capability);
        Assert.False(capability.CanStreamMarketData);
        Assert.Equal(CapabilityAvailability.Blocked, capability.Trading);
    }

    [Fact]
    public async Task DynamicResolverCreatesOnlyExactOfficialVenueSymbols()
    {
        var instrument = new CanonicalInstrument("SOL", "USDT", MarketProduct.Perpetual);
        var catalog = new PublicInstrumentCatalog();
        catalog.Replace([
            new(instrument, TradingVenue.Bybit, "SOLUSDT", 0.001m),
            new(instrument, TradingVenue.Gate, "SOL_USDT", 0.001m, 1m)]);
        await using var controller = new MarketSelectionController(
            capability => new FakeClient(capability.Venue, capability.VenueSymbol), catalog.Find);

        Assert.True(await controller.SelectAsync("SOL", "USDT", MarketProduct.Perpetual));
        Assert.Equal(["SOLUSDT", "SOL_USDT"], controller.Clients
            .Cast<FakeClient>().Select(client => client.Symbol).ToArray());
        Assert.DoesNotContain(controller.Clients.Cast<FakeClient>(),
            client => client.TradingVenue == TradingVenue.Mexc);
    }

    [Fact]
    public async Task UnsupportedDynamicInstrumentCreatesNoClientWithoutGuessing()
    {
        var catalog = new PublicInstrumentCatalog();
        var supported = new CanonicalInstrument("APT", "USDT", MarketProduct.Perpetual);
        catalog.Replace([new(supported, TradingVenue.Bybit, "APTUSDT", 0.001m)]);
        await using var controller = new MarketSelectionController(
            capability => new FakeClient(capability.Venue, capability.VenueSymbol), catalog.Find);
        Assert.True(await controller.SelectAsync("APT", "USDT", MarketProduct.Perpetual));
        var existing = controller.Clients.Single();
        Assert.False(await controller.SelectAsync("SOL", "USDT", MarketProduct.Perpetual));
        Assert.Same(existing, controller.Clients.Single());
        Assert.False(((FakeClient)existing).Disposed);
        Assert.Equal(supported, controller.SelectedInstrument);
    }

    private sealed class FakeClient : IPublicMarketDataClient
    {
        private readonly Func<Task>? _onDispose;

        public FakeClient(
            TradingVenue tradingVenue,
            string symbol,
            Func<Task>? onDispose = null)
        {
            TradingVenue = tradingVenue;
            Symbol = symbol;
            _onDispose = onDispose;
        }

        public TradingVenue TradingVenue { get; }
        public string Symbol { get; }
        public string Venue => TradingVenue.ToString().ToUpperInvariant();
        public bool Disposed { get; private set; }
        public event Action<OrderBookSnapshot>? SnapshotReceived { add { } remove { } }
        public event Action<TradeCluster>? ClusterReceived { add { } remove { } }
        public event Action<IReadOnlyList<PublicTrade>>? TradesReceived { add { } remove { } }
        public event Action<MarketDataConnectionState, string?>? StateChanged { add { } remove { } }
        public void Start() { }
        public async ValueTask DisposeAsync()
        {
            Disposed = true;
            if (_onDispose is not null) await _onDispose();
        }
    }
}
