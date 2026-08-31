using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Trdng.Bybit.MarketData;
using Trdng.Core.Clusters;
using Trdng.Core.MarketData;
using Trdng.Core.Instruments;
using Trdng.Gate.MarketData;
using Trdng.Mexc.MarketData;
using Trdng.Mexc.Private;
using Trdng.Core.Orders;
using Trdng.Core.Credentials;
using Trdng.Core.Diagnostics;

namespace Trdng.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase, IAsyncDisposable
{
    private BybitPublicOrderBookClient? _client;
    private GatePublicMarketDataClient? _gateClient;
    private IPublicMarketDataClient? _mexcClient;
    private readonly MarketSelectionController _selectionController;
    private readonly PublicInstrumentCatalog _publicCatalog = new();
    private readonly object _catalogSync = new();
    private DateTimeOffset? _catalogLoadedAt;
    private string _catalogBaseState = "КАТАЛОГ · ЗАГРУЗКА";
    private static readonly TimeSpan CatalogMaxAge = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CatalogRefreshAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CatalogRefreshCheckInterval = TimeSpan.FromMinutes(1);
    private readonly CancellationTokenSource _catalogLifetime = new();
    private readonly Task _initializationTask;
    private Task? _catalogRefreshTask;
    private readonly SemaphoreSlim _selectionLifecycleGate = new(1, 1);
    private Action<OrderBookSnapshot>? _bybitSnapshotHandler;
    private Action<TradeCluster>? _bybitClusterHandler;
    private Action<IReadOnlyList<PublicTrade>>? _bybitTradesHandler;
    private Action<MarketDataConnectionState, string?>? _bybitStateHandler;
    private Action<OrderBookSnapshot>? _gateSnapshotHandler;
    private Action<IReadOnlyList<PublicTrade>>? _gateTradesHandler;
    private Action<MarketDataConnectionState, string?>? _gateStateHandler;
    private Action<OrderBookSnapshot>? _mexcSnapshotHandler;
    private Action<MarketDataConnectionState, string?>? _mexcStateHandler;
    private readonly SelectionGeneration _generation = new();
    private long _latestRequestId;
    private readonly VenueLiquidityTrackers _liquidityTrackers = new();
    private readonly HttpClient _catalogHttpClient =
        PublicHttpTransport.CreateClient(TimeSpan.FromSeconds(15));
    private readonly HttpClient _publicMarketDataHttpClient =
        PublicHttpTransport.CreateClient(TimeSpan.FromSeconds(5));
    private CancellationTokenSource _metadataLifetime = new();
    private readonly MarketDataFreshnessOptions _freshness;
    private readonly DispatcherTimer _healthTimer;
    private readonly DispatcherTimer _renderTimer;
    private readonly DispatcherTimer _bookSettingsSaveTimer;
    private readonly FileBookDisplaySettingsStore? _bookSettingsStore;
    private readonly LatestBookDisplaySettingsWriter? _bookSettingsWriter;
    private int _disposeStarted;
    private readonly BoundedRenderUpdateGate _renderUpdateGate = new();
    private readonly object _marketDataSync = new();
    private OrderBookSnapshot? _latestSnapshot;
    private DateTimeOffset _latestSnapshotAt;
    private MarketDataConnectionState _bybitState =
        MarketDataConnectionState.Connecting;
    private IReadOnlyDictionary<(LiquiditySide Side, decimal Price), LiquidityLevelState>
        _latestLiquidity =
            new Dictionary<(LiquiditySide, decimal), LiquidityLevelState>();
    private OrderBookSnapshot? _latestGateSnapshot;
    private DateTimeOffset _latestGateSnapshotAt;
    private MarketDataConnectionState _gateState =
        MarketDataConnectionState.Connecting;
    private MarketDataConnectionState _mexcState =
        MarketDataConnectionState.Disconnected;
    private DateTimeOffset _latestMexcSnapshotAt;
    private OrderBookSnapshot? _latestMexcSnapshot;
    private IReadOnlyDictionary<(LiquiditySide Side, decimal Price), LiquidityLevelState>
        _latestMexcLiquidity =
            new Dictionary<(LiquiditySide, decimal), LiquidityLevelState>();
    private decimal? _bybitTickSize;
    private decimal? _gateTickSize;
    private decimal? _mexcTickSize;
    private IReadOnlyDictionary<(LiquiditySide Side, decimal Price), LiquidityLevelState>
        _latestGateLiquidity =
            new Dictionary<(LiquiditySide, decimal), LiquidityLevelState>();
    private TradeCluster? _latestCluster;
    private int _scaleEligibilityMask = -1;
    private readonly DryRunOrderFactory _dryRunOrderFactory =
        new(new ClientOrderIdGenerator());
    private readonly DryRunAuditTrail _dryRunAudit = new(128);
    private readonly DryRunConfirmationController _dryRunConfirmation;
    private MarketOrderIntent? _currentDryRunIntent;
    private OrderValidationResult? _currentDryRunValidation;
    private PreparedDryRun? _preparedDryRun;
    private readonly SimulationOrderStore _simulationStore;
    private readonly SimulationPlaybackCoordinator _simulationPlayback;
    private bool _simulationJournalAvailable;
    private readonly CredentialAudit _credentialAudit;
    private readonly ICredentialVault _credentialVault;
    private readonly CredentialPairController _readOnlyCredentials;
    private readonly CredentialPairController _orderTestCredentials;
    private int _memoryEmergencyStarted;
    private int _memoryWarningActive;
    private int _warningRenderTick;
    public MainViewModel() : this(MarketDataFreshnessOptions.ScalpingDefault)
    {
    }

    internal MainViewModel(MarketDataFreshnessOptions freshness)
    {
        _freshness = freshness ?? throw new ArgumentNullException(nameof(freshness));
        _credentialAudit = new CredentialAudit(32);
        ICredentialVault nativeVault = OperatingSystem.IsMacOS()
            ? new MacOsKeychainCredentialVault()
            : new UnavailableCredentialVault();
        _credentialVault = new AuditedCredentialVault(nativeVault, _credentialAudit);
        _dryRunConfirmation = new(
            TimeSpan.FromSeconds(8), _dryRunAudit);
        _readOnlyCredentials = new(_credentialVault,
            MexcCredentialProvider.ApiKeyIdentity, MexcCredentialProvider.SecretIdentity,
            () => _dryRunConfirmation.KillSwitchEngaged, TimeSpan.FromSeconds(8));
        _orderTestCredentials = new(_credentialVault,
            MexcOrderTestCredentialProvider.ApiKeyIdentity,
            MexcOrderTestCredentialProvider.SecretIdentity,
            () => _dryRunConfirmation.KillSwitchEngaged, TimeSpan.FromSeconds(8));
        MexcBookSettings = new(TradingVenue.Mexc, 200);
        GateBookSettings = new(TradingVenue.Gate, 50);
        BybitBookSettings = new(TradingVenue.Bybit, 200);
        _bookSettingsStore = CreateBookDisplaySettingsStore();
        _bookSettingsWriter = _bookSettingsStore is null
            ? null
            : new LatestBookDisplaySettingsWriter(
                settings => _bookSettingsStore.Save(settings),
                ObserveBookSettingsSave);
        RestoreBookDisplaySettings();
        _bookSettingsSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _bookSettingsSaveTimer.Tick += BookSettingsSaveTimerOnTick;
        (_simulationStore, _simulationJournalAvailable) = CreateSimulationStore();
        _simulationPlayback = new(_simulationStore);
        SimulationTimeline = _simulationJournalAvailable
            ? DescribeRecoveredSimulation(_simulationStore)
            : "JOURNAL BLOCKED · FAIL CLOSED";
        _selectionController = new MarketSelectionController(CreateClient, _publicCatalog.Find);
        _selectionController.Resetting += OnSelectionResetting;
        MexcBookSettings.Changed += OnBookSettingsChanged;
        GateBookSettings.Changed += OnBookSettingsChanged;
        BybitBookSettings.Changed += OnBookSettingsChanged;
        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(
                1d / BoundedBookScalePolicy.MaximumRenderHertz)
        };
        _renderTimer.Tick += RenderTimerOnTick;
        _renderTimer.Start();
        _healthTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _healthTimer.Tick += HealthTimerOnTick;
        _healthTimer.Start();
        _initializationTask = InitializeAsync();
        _ = RefreshCredentialStatusAsync();
    }

    public ObservableCollection<BookLevelViewModel> Asks { get; } = [];

    public ObservableCollection<BookLevelViewModel> Bids { get; } = [];

    public ObservableCollection<ClusterLevelViewModel> ClusterLevels { get; } = [];

    public ObservableCollection<BookLevelViewModel> GateAsks { get; } = [];

    public ObservableCollection<BookLevelViewModel> GateBids { get; } = [];

    public ObservableCollection<BookLevelViewModel> MexcAsks { get; } = [];

    public ObservableCollection<BookLevelViewModel> MexcBids { get; } = [];

    [ObservableProperty]
    public partial string RuntimeSafetyStatus { get; set; } = string.Empty;

    public VenueBookSettingsViewModel MexcBookSettings { get; }

    public VenueBookSettingsViewModel GateBookSettings { get; }

    public VenueBookSettingsViewModel BybitBookSettings { get; }

    [ObservableProperty]
    public partial string ConnectionStatus { get; set; } = "CONNECTING";

    [ObservableProperty]
    public partial string LastPrice { get; set; } = "—";

    [ObservableProperty]
    public partial string Spread { get; set; } = "—";

    [ObservableProperty]
    public partial string ClusterInterval { get; set; } = "15 СЕК";

    [ObservableProperty]
    public partial string ClusterDelta { get; set; } = "Δ —";

    [ObservableProperty]
    public partial string GateConnectionStatus { get; set; } = "CONNECTING";

    [ObservableProperty]
    public partial string GateLastPrice { get; set; } = "—";

    [ObservableProperty]
    public partial string GateSpread { get; set; } = "—";

    [ObservableProperty]
    public partial string MexcLastPrice { get; set; } = "—";

    [ObservableProperty]
    public partial string MexcSpread { get; set; } = "—";

    [ObservableProperty]
    public partial string ConsensusVerdict { get; set; } = "НЕТ КОНСЕНСУСА";

    [ObservableProperty]
    public partial string ConsensusColor { get; set; } = "#8B93A1";

    [ObservableProperty]
    public partial string CrossVenueDivergence { get; set; } =
        "РАСХОЖДЕНИЕ: ОЖИДАНИЕ";

    [ObservableProperty]
    public partial string CrossVenueDivergenceColor { get; set; } = "#8B93A1";

    [ObservableProperty]
    public partial string SharedScaleLabel { get; set; } = "НЕЗАВИСИМЫЕ ШКАЛЫ: —";

    [ObservableProperty]
    public partial string SelectedAsset { get; set; } = "BTC";

    [ObservableProperty]
    public partial string SelectedQuote { get; set; } = "USDT";

    [ObservableProperty]
    public partial string InstrumentSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CatalogState { get; set; } = "КАТАЛОГ · ЗАГРУЗКА";

    public ObservableCollection<string> InstrumentSearchResults { get; } = [];

    [ObservableProperty]
    public partial string InstrumentTitle { get; set; } = "BTC / USDT · PERPETUAL";

    [ObservableProperty]
    public partial string ProductLabel { get; set; } = "PERPETUAL";

    [ObservableProperty]
    public partial string BybitCardTitle { get; set; } = "BYBIT · BTCUSDT · PERPETUAL";

    [ObservableProperty]
    public partial string GateCardTitle { get; set; } = "GATE · BTC_USDT · PERPETUAL";

    [ObservableProperty]
    public partial string MexcConnectionStatus { get; set; } = "CONNECTING";

    [ObservableProperty]
    public partial string MexcCardTitle { get; set; } = "MEXC · BTC_USDT · PERPETUAL";

    [ObservableProperty]
    public partial string MexcEmptyState { get; set; } = "ПОДКЛЮЧЕНИЕ К ПУБЛИЧНОМУ СТАКАНУ…";

    [ObservableProperty]
    public partial string GateEmptyState { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BybitEmptyState { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ActiveDryRunVenue { get; set; } = "GATE";

    [ObservableProperty]
    public partial string DryRunSideLabel { get; set; } = "BUY MARKET";

    [ObservableProperty]
    public partial string DryRunValueText { get; set; } = "10";

    [ObservableProperty]
    public partial string DryRunUnit { get; set; } = "USDT";

    [ObservableProperty]
    public partial string DryRunPreview { get; set; } = "МОДЕЛИРОВАНИЕ · METADATA REQUIRED";

    [ObservableProperty]
    public partial string DryRunRoute { get; set; } = "GATE · BTC_USDT · PERPETUAL";

    [ObservableProperty]
    public partial string DryRunRiskState { get; set; } = "STOP · ENGAGED";

    [ObservableProperty]
    public partial string DryRunLimitLabel { get; set; } =
        "SIMULATION PROFILE · MAX 10 USDT · MAX 1 BASE";

    [ObservableProperty]
    public partial string DryRunConfirmationState { get; set; } =
        "СНАЧАЛА ОТКЛЮЧИТЕ STOP ДЛЯ СИМУЛЯЦИИ";

    [ObservableProperty]
    public partial string SimulationTimeline { get; set; } = "LIFECYCLE · НЕТ ЗАПИСЕЙ";

    [ObservableProperty]
    public partial string ReadOnlyCredentialStatus { get; set; } = "ПРОВЕРКА KEYCHAIN";

    [ObservableProperty]
    public partial string OrderTestCredentialStatus { get; set; } = "ПРОВЕРКА KEYCHAIN";

    [ObservableProperty]
    public partial string ReadOnlyApiKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReadOnlySecret { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OrderTestApiKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OrderTestSecret { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ReadOnlyReplaceConfirmed { get; set; }

    [ObservableProperty]
    public partial bool OrderTestReplaceConfirmed { get; set; }

    [ObservableProperty]
    public partial string MexcPrivateStatus { get; set; } =
        MexcPrivatePresentation.Masked(MexcPrivateState.NotConfigured);

    [ObservableProperty]
    public partial string MexcOrderTestStatus { get; set; } =
        MexcOrderTestPresentation.Masked(MexcOrderTestState.KeyRequired);

    [ObservableProperty]
    public partial bool CanRevokeReadOnlyCredential { get; set; }

    [ObservableProperty]
    public partial bool CanConfirmRevokeReadOnlyCredential { get; set; }

    [ObservableProperty]
    public partial bool CanConfirmReplaceReadOnlyCredential { get; set; }

    [ObservableProperty]
    public partial bool CanRevokeOrderTestCredential { get; set; }

    [ObservableProperty]
    public partial bool CanConfirmRevokeOrderTestCredential { get; set; }

    [ObservableProperty]
    public partial bool CanConfirmReplaceOrderTestCredential { get; set; }

    [ObservableProperty]
    public partial string ReadOnlyCredentialAction { get; set; } = "";

    [ObservableProperty]
    public partial string OrderTestCredentialAction { get; set; } = "";

    private OrderSide _dryRunSide = OrderSide.Buy;

    public MarketProduct SelectedProduct { get; private set; } = MarketProduct.Perpetual;

    public async Task SelectAssetAsync(string baseAsset)
    {
        await SelectMarketAsync(baseAsset, "USDT", SelectedProduct).ConfigureAwait(false);
    }

    public Task SelectInstrumentAsync(string pairId)
    {
        var parts = pairId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) throw new ArgumentException("Invalid pair id.", nameof(pairId));
        return SelectMarketAsync(parts[0], parts[1], SelectedProduct);
    }

    public async Task SelectProductAsync(MarketProduct product)
    {
        if (!PublicCatalogFreshness.IsFresh(
                CatalogLoadedAt(), DateTimeOffset.UtcNow, CatalogMaxAge))
        {
            CatalogState = "КАТАЛОГ · УСТАРЕЛ";
            return;
        }
        var current = new CanonicalInstrument(SelectedAsset, SelectedQuote, SelectedProduct);
        var target = CatalogSelectionPolicy.ChooseForProduct(_publicCatalog, current, product);
        RefreshCatalogSearch(product);
        if (target is null)
        {
            CatalogState = "КАТАЛОГ · НЕТ ИНСТРУМЕНТОВ ДЛЯ ПРОДУКТА";
            return;
        }
        await SelectMarketAsync(target.Value.BaseAsset, target.Value.QuoteAsset, product)
            .ConfigureAwait(false);
    }

    public async Task SelectMarketAsync(string baseAsset, MarketProduct product)
        => await SelectMarketAsync(baseAsset, "USDT", product).ConfigureAwait(false);

    public async Task SelectMarketAsync(string baseAsset, string quoteAsset, MarketProduct product)
        => await SelectMarketAsync(baseAsset, quoteAsset, product,
            CancellationToken.None, forceRefresh: false).ConfigureAwait(false);

    private async Task SelectMarketAsync(
        string baseAsset,
        string quoteAsset,
        MarketProduct product,
        CancellationToken cancellationToken,
        bool forceRefresh)
    {
        if (!PublicCatalogFreshness.IsFresh(
                CatalogLoadedAt(), DateTimeOffset.UtcNow, CatalogMaxAge))
        {
            await Dispatcher.UIThread.InvokeAsync(() => CatalogState = "КАТАЛОГ · УСТАРЕЛ");
            return;
        }
        var requestId = Interlocked.Increment(ref _latestRequestId);
        await _selectionLifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SelectMarketUnderLifecycleGateAsync(baseAsset, quoteAsset, product,
                requestId, cancellationToken, forceRefresh).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ConnectionStatus = GateConnectionStatus = "UNAVAILABLE";
                CrossVenueDivergence = "НЕ УДАЛОСЬ ПЕРЕКЛЮЧИТЬ АКТИВ";
            });
        }
        finally { _selectionLifecycleGate.Release(); }
    }

    private async Task<bool> SelectMarketUnderLifecycleGateAsync(
        string baseAsset,
        string quoteAsset,
        MarketProduct product,
        long? requestId,
        CancellationToken cancellationToken,
        bool forceRefresh)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (requestId is { } expected && expected != Volatile.Read(ref _latestRequestId))
            return false;
        if (!await _selectionController.SelectAsync(baseAsset, quoteAsset, product,
                forceRefresh, cancellationToken).ConfigureAwait(false))
            return false;
        cancellationToken.ThrowIfCancellationRequested();
        if (requestId is { } current && current != Volatile.Read(ref _latestRequestId))
            return false;
        var selected = _selectionController.SelectedInstrument!.Value;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            SelectedProduct = selected.Product;
            SelectedAsset = selected.BaseAsset;
            SelectedQuote = selected.QuoteAsset;
            ApplySelectionLabels(selected);
        });

        _client = _selectionController.Clients.OfType<BybitPublicOrderBookClient>()
            .SingleOrDefault();
        _gateClient = _selectionController.Clients.OfType<GatePublicMarketDataClient>()
            .SingleOrDefault();
        _mexcClient = _selectionController.Clients
            .SingleOrDefault(client => client.Venue == "MEXC");
        AttachClients();
        _metadataLifetime = new CancellationTokenSource();
        _ = LoadInstrumentMetadataSafelyAsync(
            selected.BaseAsset,
            selected.QuoteAsset,
            selected.Product,
            _metadataLifetime.Token);
        foreach (var client in _selectionController.Clients) client.Start();
        return true;
    }

    public void SelectDryRunVenue(TradingVenue venue)
    {
        var instrument = new CanonicalInstrument(SelectedAsset, SelectedQuote, SelectedProduct);
        var capability = StarterInstrumentCatalog.Find(instrument, venue);
        if (DryRunCapabilityPolicy.Evaluate(capability) == DryRunCapabilityMode.Denied)
        {
            Enum.TryParse<TradingVenue>(ActiveDryRunVenue, true, out var activeVenue);
            DryRunPreview = DryRunVenueSelectionMessage.Rejected(venue, activeVenue);
            return;
        }
        InvalidateDryRunConfirmation("VENUE CHANGED");
        ActiveDryRunVenue = venue.ToString().ToUpperInvariant();
        RefreshDryRunPreview();
    }

    public void SelectDryRunSide(OrderSide side)
    {
        InvalidateDryRunConfirmation("SIDE CHANGED");
        _dryRunSide = side;
        var sizing = OrderSizingDefaults.For(side, SelectedAsset, SelectedQuote);
        DryRunSideLabel = side == OrderSide.Buy ? "BUY MARKET" : "SELL MARKET";
        DryRunUnit = sizing.Unit;
        DryRunValueText = sizing.Value.ToString(CultureInfo.InvariantCulture);
        RefreshDryRunPreview();
    }

    partial void OnDryRunValueTextChanged(string value)
    {
        InvalidateDryRunConfirmation("VALUE CHANGED");
        RefreshDryRunPreview();
    }

    partial void OnInstrumentSearchTextChanged(string value) =>
        RefreshCatalogSearch(SelectedProduct);

    private void RefreshDryRunPreview()
    {
        _currentDryRunIntent = null;
        _currentDryRunValidation = null;
        if (!Enum.TryParse<TradingVenue>(ActiveDryRunVenue, true, out var venue) ||
            !OrderSizingValueParser.TryParse(DryRunValueText, out var value))
        {
            DryRunPreview = "НЕВЕРНОЕ ЧИСЛО";
            return;
        }
        var instrument = new CanonicalInstrument(SelectedAsset, SelectedQuote, SelectedProduct);
        var mode = OrderSizingDefaults.For(
            _dryRunSide, instrument.BaseAsset, instrument.QuoteAsset).Mode;
        var result = _dryRunOrderFactory.Create(
            venue, instrument, _dryRunSide, mode, value, filters: null);
        _currentDryRunIntent = result.Intent;
        _currentDryRunValidation = result.Validation;
        var capability = StarterInstrumentCatalog.Find(instrument, venue);
        DryRunRoute = capability is null
            ? $"{venue} · {instrument.BaseAsset}/{instrument.QuoteAsset} · {ProductLabel}"
            : $"{venue} · {capability.VenueSymbol} · {ProductLabel}";
        DryRunPreview = result.Validation.Status switch
        {
            OrderValidationStatus.NeedsMetadata =>
                $"{DryRunSideLabel} · {value} {DryRunUnit} · METADATA REQUIRED",
            OrderValidationStatus.Blocked => "НЕДОСТУПНО ДЛЯ МОДЕЛИРОВАНИЯ",
            OrderValidationStatus.Invalid => result.Validation.Message,
            _ => $"{DryRunSideLabel} · {value} {DryRunUnit} · VALIDATED"
        };
    }

    public void DisengageDryRunStop()
    {
        _readOnlyCredentials.Invalidate();
        _orderTestCredentials.Invalidate();
        CanRevokeReadOnlyCredential = false;
        CanRevokeOrderTestCredential = false;
        CanConfirmRevokeReadOnlyCredential = false;
        CanConfirmRevokeOrderTestCredential = false;
        if (!_simulationJournalAvailable)
        {
            DryRunConfirmationState = "JOURNAL BLOCKED · STOP ОСТАЁТСЯ ВКЛЮЧЕН";
            return;
        }
        RefreshDryRunPreview();
        if (_currentDryRunIntent is null)
        {
            DryRunConfirmationState = "НЕТ КОРРЕКТНОЙ DRY-RUN КОМАНДЫ";
            return;
        }
        var profile = SimulationProfile(_currentDryRunIntent);
        if (_dryRunConfirmation.DisengageForSimulation(profile))
        {
            _simulationPlayback.SetStop(false);
            DryRunRiskState = "SAFE · SIMULATION ONLY";
            DryRunConfirmationState = "МОЖНО ПОДГОТОВИТЬ ТОЛЬКО СИМУЛЯЦИЮ";
        }
    }

    public void EngageDryRunStop()
    {
        _dryRunConfirmation.EngageKillSwitch();
        _simulationPlayback.SetStop(true);
        _preparedDryRun = null;
        DryRunRiskState = "STOP · ENGAGED";
        DryRunConfirmationState = "СИМУЛЯЦИЯ ЗАБЛОКИРОВАНА";
        _readOnlyCredentials.Invalidate();
        _orderTestCredentials.Invalidate();
        _ = RefreshCredentialStatusAsync();
    }

    public Task SaveReadOnlyCredentialsAsync(bool replace) =>
        SaveCredentialPairAsync(_readOnlyCredentials, isReadOnly: true, replace: replace);

    public Task SaveOrderTestCredentialsAsync(bool replace) =>
        SaveCredentialPairAsync(_orderTestCredentials, isReadOnly: false, replace: replace);

    public async Task ArmCredentialRevokeAsync(bool isReadOnly) =>
        ApplyCredentialPresentation(isReadOnly, await (isReadOnly
            ? _readOnlyCredentials.ArmRevokeAsync()
            : _orderTestCredentials.ArmRevokeAsync()));

    public async Task ConfirmCredentialRevokeAsync(bool isReadOnly)
    {
        var controller = isReadOnly ? _readOnlyCredentials : _orderTestCredentials;
        ApplyCredentialPresentation(isReadOnly, await controller.ConfirmRevokeAsync());
        await RefreshCredentialStatusAsync();
    }

    public void PrepareDryRunSimulation()
    {
        RefreshDryRunPreview();
        if (_currentDryRunIntent is null || _currentDryRunValidation is null)
        {
            DryRunConfirmationState = "КОМАНДА НЕ ПОДГОТОВЛЕНА";
            return;
        }
        var result = _dryRunConfirmation.Prepare(
            _currentDryRunIntent,
            _currentDryRunValidation,
            SimulationProfile(_currentDryRunIntent),
            CurrentReferencePrice(_currentDryRunIntent.Venue));
        _preparedDryRun = result.Candidate;
        DryRunConfirmationState = result.Status == PrepareStatus.Prepared
            ? "ПОДГОТОВЛЕНО · ПОДТВЕРДИТЕ СИМУЛЯЦИЮ"
            : result.Reason;
    }

    public void ConfirmDryRunSimulation()
    {
        if (_preparedDryRun is null || _currentDryRunIntent is null)
        {
            DryRunConfirmationState = "НЕТ ДЕЙСТВУЮЩЕГО ПОДТВЕРЖДЕНИЯ";
            return;
        }
        var result = _dryRunConfirmation.Confirm(
            _preparedDryRun.Token, _currentDryRunIntent);
        DryRunConfirmationState = result.Reason;
        if (result.Status == ConfirmationStatus.Confirmed)
        {
            if (!_simulationJournalAvailable)
            {
                SimulationTimeline = "JOURNAL BLOCKED · СИМУЛЯЦИЯ НЕ ЗАПИСАНА";
                _preparedDryRun = null;
                return;
            }
            try
            {
                _simulationPlayback.ActivateConfirmed(_currentDryRunIntent);
                SimulationTimeline = $"{_currentDryRunIntent.ClientOrderId} · CONFIRMED";
                _preparedDryRun = null;
            }
            catch (Exception exception) when (IsExpectedJournalFault(exception))
            {
                BlockSimulationJournal(exception.Message);
            }
        }
    }

    public void PlayDryRunSimulation()
    {
        if (!_simulationJournalAvailable)
        {
            SimulationTimeline = "JOURNAL BLOCKED · FAIL CLOSED";
            return;
        }
        if (_simulationPlayback.StopEngaged)
        {
            SimulationTimeline = "STOP · ПРОИГРЫВАНИЕ ЗАБЛОКИРОВАНО";
            return;
        }
        if (!_simulationPlayback.HasActivePlayback)
        {
            SimulationTimeline = "ПРЕДЫДУЩАЯ ИСТОРИЯ СОХРАНЕНА · ПОДГОТОВЬТЕ ЗАНОВО";
            return;
        }
        try
        {
            var record = _simulationPlayback.Play(SimulationScenario.PartialAndFill);
            SimulationTimeline = record.State == SimulationOrderState.Unknown
                ? "UNKNOWN · ТРЕБУЕТСЯ СВЕРКА · НЕ ПОВТОРЯТЬ"
                : $"CONFIRMED → SUBMITTED → ACKNOWLEDGED → PARTIAL → {record.State.ToString().ToUpperInvariant()}";
        }
        catch (Exception exception) when (IsExpectedJournalFault(exception))
        {
            BlockSimulationJournal(exception.Message);
        }
    }

    private static (SimulationOrderStore Store, bool Available) CreateSimulationStore()
    {
        try
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var path = Path.Combine(root, "TRDNG", "simulation-journal.v1.jsonl");
            return (new SimulationOrderStore(
                new FileSimulationJournal(path, 256), 128), true);
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or InvalidDataException or
            System.Text.Json.JsonException or InvalidOperationException)
        {
            return (new SimulationOrderStore(
                new InMemorySimulationJournal(1), 8), false);
        }
    }

    private void InvalidateDryRunConfirmation(string reason)
    {
        _dryRunConfirmation.InvalidateConfirmation(reason);
        _simulationPlayback.InvalidateActive();
        _preparedDryRun = null;
        DryRunConfirmationState = _dryRunConfirmation.KillSwitchEngaged
            ? "СНАЧАЛА ОТКЛЮЧИТЕ STOP ДЛЯ СИМУЛЯЦИИ"
            : "ИЗМЕНЕНИЕ · ПОДГОТОВЬТЕ СИМУЛЯЦИЮ ЗАНОВО";
        if (_simulationStore.Orders.Count > 0)
            SimulationTimeline = "ПРЕДЫДУЩАЯ ИСТОРИЯ СОХРАНЕНА · ПОДГОТОВЬТЕ ЗАНОВО";
    }

    private void BlockSimulationJournal(string reason)
    {
        _simulationJournalAvailable = false;
        _simulationPlayback.SetStop(true);
        _dryRunConfirmation.EngageKillSwitch("JOURNAL FAULT");
        _preparedDryRun = null;
        DryRunRiskState = "STOP · JOURNAL BLOCKED";
        DryRunConfirmationState = "FAIL CLOSED · СИМУЛЯЦИЯ НЕДОСТУПНА";
        SimulationTimeline = $"JOURNAL BLOCKED · {reason}";
    }

    private static bool IsExpectedJournalFault(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or
            InvalidDataException or System.Text.Json.JsonException or
            InvalidOperationException;

    private static string DescribeRecoveredSimulation(SimulationOrderStore store)
    {
        var latest = store.Orders.Values.OrderByDescending(order => order.UpdatedAt).FirstOrDefault();
        if (latest is null) return "LIFECYCLE · НЕТ ЗАПИСЕЙ";
        return latest.State == SimulationOrderState.Unknown
            ? $"{latest.Intent.ClientOrderId} · UNKNOWN · ТРЕБУЕТСЯ СВЕРКА · НЕ ПОВТОРЯТЬ"
            : $"RECOVERED · {latest.Intent.ClientOrderId} · {latest.State.ToString().ToUpperInvariant()}";
    }

    private static RiskProfile SimulationProfile(MarketOrderIntent intent) =>
        new("SIMULATION · 10 USDT CAP", RiskProfileMode.Simulation, true,
            intent.Venue, intent.Instrument, intent.Side, intent.SizingMode,
            10m, 1m, TimeSpan.FromSeconds(2));

    private ReferencePrice? CurrentReferencePrice(TradingVenue venue)
    {
        lock (_marketDataSync)
        {
            var (snapshot, observedAt) = venue switch
            {
                TradingVenue.Mexc => (_latestMexcSnapshot, _latestMexcSnapshotAt),
                TradingVenue.Gate => (_latestGateSnapshot, _latestGateSnapshotAt),
                TradingVenue.Bybit => (_latestSnapshot, _latestSnapshotAt),
                _ => (null, default)
            };
            return ExecutableReferencePrice.Select(snapshot, _dryRunSide, observedAt);
        }
    }

    public void UpdateBookViewport(TradingVenue venue, double width, double totalHeight)
    {
        const double headerAndSpreadHeight = 80;
        var halfHeight = Math.Max(0,
            (totalHeight - headerAndSpreadHeight) / 2 -
            BookDisplayPolicy.SpreadSafetyMarginPerSide);
        BookSettings(venue).SetViewport(width, halfHeight);
    }

    public void AdjustBookDepth(TradingVenue venue, int direction) =>
        BookSettings(venue).AdjustDepth(direction);

    public void ResetBookPalette(TradingVenue venue) =>
        BookSettings(venue).ResetPalette();

    private VenueBookSettingsViewModel BookSettings(TradingVenue venue) => venue switch
    {
        TradingVenue.Mexc => MexcBookSettings,
        TradingVenue.Gate => GateBookSettings,
        TradingVenue.Bybit => BybitBookSettings,
        _ => throw new ArgumentOutOfRangeException(nameof(venue))
    };

    private void OnBookSettingsChanged()
    {
        RequestRenderRefresh();
        if (_bookSettingsStore is null) return;
        SetBookSettingsPersistenceState("НАСТРОЙКИ · ОЖИДАЮТ СОХРАНЕНИЯ");
        _bookSettingsSaveTimer.Stop();
        _bookSettingsSaveTimer.Start();
    }

    private void BookSettingsSaveTimerOnTick(object? sender, EventArgs eventArgs)
    {
        _bookSettingsSaveTimer.Stop();
        QueueBookDisplaySettingsSave();
    }

    private void QueueBookDisplaySettingsSave()
    {
        if (_bookSettingsWriter is null)
        {
            SetBookSettingsPersistenceState("НАСТРОЙКИ · ТОЛЬКО СЕССИЯ");
            return;
        }
        _bookSettingsWriter.Queue(CaptureBookDisplaySettings());
    }

    private void ObserveBookSettingsSave(BookDisplaySettingsSaveState result)
    {
        Dispatcher.UIThread.Post(() => SetBookSettingsPersistenceState(result switch
        {
            BookDisplaySettingsSaveState.Saved =>
                "НАСТРОЙКИ · СОХРАНЕНЫ ЛОКАЛЬНО",
            BookDisplaySettingsSaveState.Invalid =>
                "НАСТРОЙКИ · ОШИБКА · НЕ СОХРАНЕНЫ",
            _ => "НАСТРОЙКИ · ХРАНИЛИЩЕ НЕДОСТУПНО"
        }));
    }

    private async Task FlushBookDisplaySettingsAsync()
    {
        if (_bookSettingsWriter is null) return;
        await _bookSettingsWriter.FlushAsync(CaptureBookDisplaySettings())
            .ConfigureAwait(false);
    }

    private BookDisplaySettingsSnapshot[] CaptureBookDisplaySettings() =>
    [
        MexcBookSettings.Snapshot(),
        GateBookSettings.Snapshot(),
        BybitBookSettings.Snapshot()
    ];

    private void RestoreBookDisplaySettings()
    {
        var loaded = _bookSettingsStore?.Load();
        if (loaded?.State == BookDisplaySettingsLoadState.Loaded)
        {
            foreach (var pair in loaded.Settings)
                BookSettings(pair.Key).Apply(pair.Value);
        }
        SetBookSettingsPersistenceState(loaded?.State switch
        {
            BookDisplaySettingsLoadState.Loaded => "НАСТРОЙКИ · ЗАГРУЖЕНЫ ЛОКАЛЬНО",
            BookDisplaySettingsLoadState.Invalid => "НАСТРОЙКИ · ФАЙЛ ОТКЛОНЕН · БАЗОВЫЕ",
            BookDisplaySettingsLoadState.Unavailable => "НАСТРОЙКИ · ХРАНИЛИЩЕ НЕДОСТУПНО",
            _ => "НАСТРОЙКИ · ПО УМОЛЧАНИЮ"
        });
    }

    private void SetBookSettingsPersistenceState(string state)
    {
        MexcBookSettings.PersistenceState = state;
        GateBookSettings.PersistenceState = state;
        BybitBookSettings.PersistenceState = state;
    }

    private static FileBookDisplaySettingsStore? CreateBookDisplaySettingsStore()
    {
        try
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root)) return null;
            return new FileBookDisplaySettingsStore(Path.Combine(
                root, "MacMakeMoney_618", "book-display-settings.v1.json"));
        }
        catch (Exception exception) when (exception is ArgumentException or
            NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
        await _catalogLifetime.CancelAsync().ConfigureAwait(false);
        try { await _initializationTask.ConfigureAwait(false); }
        catch (OperationCanceledException) when (_catalogLifetime.IsCancellationRequested) { }
        if (_catalogRefreshTask is not null)
        {
            try { await _catalogRefreshTask.ConfigureAwait(false); }
            catch (OperationCanceledException) when (_catalogLifetime.IsCancellationRequested) { }
        }

        _bookSettingsSaveTimer.Stop();
        _bookSettingsSaveTimer.Tick -= BookSettingsSaveTimerOnTick;
        MexcBookSettings.Changed -= OnBookSettingsChanged;
        GateBookSettings.Changed -= OnBookSettingsChanged;
        BybitBookSettings.Changed -= OnBookSettingsChanged;
        await FlushBookDisplaySettingsAsync().ConfigureAwait(false);

        await _selectionLifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _healthTimer.Stop();
            _healthTimer.Tick -= HealthTimerOnTick;
            _renderTimer.Stop();
            _renderTimer.Tick -= RenderTimerOnTick;
            await _metadataLifetime.CancelAsync();
            DetachClients();
            _selectionController.Resetting -= OnSelectionResetting;
            await _selectionController.DisposeAsync();
            _metadataLifetime.Dispose();
            _catalogLifetime.Dispose();
            _catalogHttpClient.Dispose();
            _publicMarketDataHttpClient.Dispose();
        }
        finally
        {
            _selectionLifecycleGate.Release();
            _selectionLifecycleGate.Dispose();
        }
    }

    internal async Task EmergencyStopPublicDataAsync()
    {
        if (Interlocked.Exchange(ref _memoryEmergencyStarted, 1) != 0) return;
        await _selectionLifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _catalogLifetime.CancelAsync().ConfigureAwait(false);
            await _metadataLifetime.CancelAsync().ConfigureAwait(false);
            DetachClients();
            _selectionController.Resetting -= OnSelectionResetting;
            await _selectionController.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _selectionLifecycleGate.Release();
        }
    }

    internal void ApplyMemoryWarning()
    {
        if (Interlocked.Exchange(ref _memoryWarningActive, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
            RuntimeSafetyStatus = "ПАМЯТЬ · WARNING · КАДЕНС СНИЖЕН");
    }

    private void OnSnapshotReceived(OrderBookSnapshot snapshot, long expectedGeneration)
    {
        lock (_marketDataSync)
        {
            if (!_generation.IsCurrent(expectedGeneration)) return;
            var observedAt = DateTimeOffset.UtcNow;
            _latestSnapshot = snapshot;
            _latestSnapshotAt = observedAt;
            _latestLiquidity = _liquidityTrackers.Bybit.Observe(snapshot, observedAt);
        }

        RequestRenderRefresh();
    }

    private void RebuildBook()
    {
        var now = DateTimeOffset.UtcNow;
        var bybitScale = SelectedProduct == MarketProduct.Perpetual &&
            SharedScaleBookSelection.IsEligible(_bybitState, _latestSnapshot,
                _latestSnapshotAt, now, _freshness)
            ? SharedPriceScale.Build(_latestSnapshot, null,
                BybitBookSettings.Layout.Depth, _bybitTickSize)
            : null;
        var gateScale = SelectedProduct == MarketProduct.Perpetual &&
            SharedScaleBookSelection.IsEligible(_gateState, _latestGateSnapshot,
                _latestGateSnapshotAt, now, _freshness)
            ? SharedPriceScale.Build(_latestGateSnapshot, null,
                GateBookSettings.Layout.Depth, _gateTickSize)
            : null;
        var mexcScale =
            SharedScaleBookSelection.IsEligible(_mexcState, _latestMexcSnapshot,
                _latestMexcSnapshotAt, now, _freshness)
            ? SharedPriceScale.Build(_latestMexcSnapshot, null,
                MexcBookSettings.Layout.Depth, _mexcTickSize)
            : null;

        RebuildBybit(bybitScale);
        RebuildGate(gateScale);
        RebuildMexc(mexcScale);
        UpdateConsensus(VisibleConsensusDepth());
        SharedScaleLabel = bybitScale is null && gateScale is null && mexcScale is null
            ? "НЕЗАВИСИМЫЕ ШКАЛЫ: —"
            : "НЕЗАВИСИМЫЕ ШКАЛЫ · ГЛУБИНА И ОБЪЁМ В ⚙ КАЖДОГО СТАКАНА";
    }

    private int VisibleConsensusDepth() => Math.Max(
        MexcBookSettings.Layout.Depth,
        Math.Max(GateBookSettings.Layout.Depth, BybitBookSettings.Layout.Depth));

    private void RebuildBybit(SharedPriceScale? scale)
    {
        if (_latestSnapshot is null || scale is null)
        {
            Asks.Clear();
            Bids.Clear();
            return;
        }
        RebuildVenueBook(_latestSnapshot, scale, BybitBookSettings,
            _latestLiquidity, Asks, Bids);
    }

    private void RebuildGate(SharedPriceScale? scale)
    {
        if (_latestGateSnapshot is null || scale is null)
        {
            GateAsks.Clear();
            GateBids.Clear();
            return;
        }
        RebuildVenueBook(_latestGateSnapshot, scale, GateBookSettings,
            _latestGateLiquidity, GateAsks, GateBids);
    }

    private void RebuildMexc(SharedPriceScale? scale)
    {
        if (_latestMexcSnapshot is null || scale is null)
        {
            MexcAsks.Clear();
            MexcBids.Clear();
            return;
        }
        RebuildVenueBook(_latestMexcSnapshot, scale, MexcBookSettings,
            _latestMexcLiquidity, MexcAsks, MexcBids);
    }

    private static void RebuildVenueBook(
        OrderBookSnapshot snapshot,
        SharedPriceScale scale,
        VenueBookSettingsViewModel settings,
        IReadOnlyDictionary<(LiquiditySide Side, decimal Price), LiquidityLevelState> liquidity,
        ObservableCollection<BookLevelViewModel> asks,
        ObservableCollection<BookLevelViewModel> bids)
    {
        var layout = settings.Layout;
        var askPrices = scale.Asks.Reverse().ToArray();
        var bidPrices = scale.Bids.ToArray();
        var askPriceSet = askPrices.ToHashSet();
        var bidPriceSet = bidPrices.ToHashSet();
        var volumeScale = VisibleBookVolumeScale.ResolveSides(
            snapshot.Asks.Where(level => askPriceSet.Contains(level.Price))
                .Select(level => level.Quantity),
            snapshot.Bids.Where(level => bidPriceSet.Contains(level.Price))
                .Select(level => level.Quantity),
            settings.AutomaticVolumeScale,
            settings.ManualVolumeReference);
        var textSize = Math.Clamp(layout.RowHeight * 0.62, 9, 16);
        ReplaceBookLevels(asks, ToBookPresentations(snapshot.Asks, askPrices, LiquiditySide.Ask,
            liquidity, layout, textSize, volumeScale.AskLargest,
            volumeScale.AskReference, settings.Palette));
        ReplaceBookLevels(bids, ToBookPresentations(snapshot.Bids, bidPrices, LiquiditySide.Bid,
            liquidity, layout, textSize, volumeScale.BidLargest,
            volumeScale.BidReference, settings.Palette));
    }

    private void OnGateSnapshotReceived(OrderBookSnapshot snapshot, long expectedGeneration)
    {
        lock (_marketDataSync)
        {
            if (!_generation.IsCurrent(expectedGeneration)) return;
            var observedAt = DateTimeOffset.UtcNow;
            _latestGateSnapshot = snapshot;
            _latestGateSnapshotAt = observedAt;
            _latestGateLiquidity = _liquidityTrackers.Gate.Observe(snapshot, observedAt);
        }

        RequestRenderRefresh();
    }

    private void OnMexcSnapshotReceived(OrderBookSnapshot snapshot, long expectedGeneration)
    {
        lock (_marketDataSync)
        {
            if (!_generation.IsCurrent(expectedGeneration)) return;
            var observedAt = DateTimeOffset.UtcNow;
            _latestMexcSnapshot = snapshot;
            _latestMexcSnapshotAt = observedAt;
            _latestMexcLiquidity = _liquidityTrackers.Mexc.Observe(snapshot, observedAt);
        }
        RequestRenderRefresh();
    }

    private void OnMexcStateChanged(
        MarketDataConnectionState state,
        string? detail,
        long expectedGeneration)
    {
        lock (_marketDataSync)
        {
            if (!_generation.IsCurrent(expectedGeneration)) return;
            _mexcState = state;
            if (state is MarketDataConnectionState.WaitingForSnapshot or
                MarketDataConnectionState.Reconnecting or MarketDataConnectionState.Disconnected)
            {
                _liquidityTrackers.Mexc.Reset();
                _latestMexcSnapshot = null;
            }
        }
        RequestRenderRefresh();
    }

    private void OnGateTradesReceived(IReadOnlyList<PublicTrade> trades, long expectedGeneration)
    {
        lock (_marketDataSync)
        {
            if (_generation.IsCurrent(expectedGeneration))
                _liquidityTrackers.Gate.ObserveTrades(trades);
        }
    }

    private void OnGateStateChanged(
        MarketDataConnectionState state,
        string? detail,
        long expectedGeneration)
    {
        lock (_marketDataSync)
        {
            if (!_generation.IsCurrent(expectedGeneration)) return;
            _gateState = state;
            if (state is MarketDataConnectionState.WaitingForSnapshot or
                MarketDataConnectionState.Reconnecting or
                MarketDataConnectionState.Disconnected)
            {
                _liquidityTrackers.Gate.Reset();
                _latestGateSnapshot = null;
            }
        }

        RequestRenderRefresh();
    }

    private void OnTradesReceived(IReadOnlyList<PublicTrade> trades, long expectedGeneration)
    {
        lock (_marketDataSync)
        {
            if (_generation.IsCurrent(expectedGeneration))
                _liquidityTrackers.Bybit.ObserveTrades(trades);
        }
    }

    private void OnStateChanged(
        MarketDataConnectionState state,
        string? detail,
        long expectedGeneration)
    {
        lock (_marketDataSync)
        {
            if (!_generation.IsCurrent(expectedGeneration)) return;
            _bybitState = state;
            if (state is MarketDataConnectionState.WaitingForSnapshot or
                MarketDataConnectionState.Reconnecting or
                MarketDataConnectionState.Disconnected)
            {
                _liquidityTrackers.Bybit.Reset();
                _latestSnapshot = null;
            }
        }

        RequestRenderRefresh();
    }

    private string FormatConnectionState(
        MarketDataConnectionState state,
        DateTimeOffset? lastSnapshotAt = null) =>
        VenueCardStatus.Resolve(state, lastSnapshotAt, DateTimeOffset.UtcNow, _freshness);

    private static string MarketDataEmptyState(MarketDataConnectionState state) => state switch
    {
        MarketDataConnectionState.Connecting => "ПОДКЛЮЧЕНИЕ К ПУБЛИЧНОМУ СТАКАНУ…",
        MarketDataConnectionState.WaitingForSnapshot => "ОЖИДАНИЕ ПЕРВОГО СНИМКА…",
        MarketDataConnectionState.Reconnecting => "ПЕРЕПОДКЛЮЧЕНИЕ И НОВЫЙ СНИМОК…",
        MarketDataConnectionState.Live => "ОЖИДАНИЕ УРОВНЕЙ…",
        _ => "ПУБЛИЧНЫЙ СТАКАН НЕДОСТУПЕН"
    };

    private void UpdateConsensus(int depth)
    {
        var eligible = ConsensusBookSelection.Select(
            SelectedProduct,
            [
                new("MEXC", SelectedProduct, _mexcState,
                    _latestMexcSnapshot, _latestMexcSnapshotAt),
                new("GATE", MarketProduct.Perpetual, _gateState,
                    _latestGateSnapshot, _latestGateSnapshotAt),
                new("BYBIT", MarketProduct.Perpetual, _bybitState,
                    _latestSnapshot, _latestSnapshotAt)
            ],
            DateTimeOffset.UtcNow,
            _freshness);
        var signals = eligible.SelectMany(candidate => CollectSignals(
            candidate.Venue,
            candidate.Book,
            candidate.Venue switch
            {
                "MEXC" => _latestMexcLiquidity,
                "GATE" => _latestGateLiquidity,
                _ => _latestLiquidity
            },
            depth));
        var verdict = CrossVenueConsensus.Evaluate(signals);
        (ConsensusVerdict, ConsensusColor) = verdict.Consensus switch
        {
            MarketConsensus.Bullish => ("ЭВРИСТИКА: ПЕРЕВЕС BID", "#65DCA2"),
            MarketConsensus.Bearish => ("ЭВРИСТИКА: ПЕРЕВЕС ASK", "#FF7A86"),
            MarketConsensus.Mixed => ("ЭВРИСТИКА: СМЕШАННО", "#F5C96A"),
            _ => ("ЭВРИСТИКА: НЕТ ДАННЫХ", "#8B93A1")
        };
    }

    private void HealthTimerOnTick(object? sender, EventArgs eventArgs)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_marketDataSync)
        {
            ConnectionStatus = SelectedProduct == MarketProduct.Spot
                ? "UNAVAILABLE"
                : FormatConnectionState(_bybitState, _latestSnapshotAt);
            GateConnectionStatus = SelectedProduct == MarketProduct.Spot
                ? "UNAVAILABLE"
                : FormatConnectionState(_gateState, _latestGateSnapshotAt);
            MexcConnectionStatus = _mexcClient is null
                ? "UNAVAILABLE" : FormatConnectionState(_mexcState, _latestMexcSnapshotAt);
            if (_client is null) ConnectionStatus = "UNAVAILABLE";
            if (_gateClient is null) GateConnectionStatus = "UNAVAILABLE";
            var eligibilityMask = GetScaleEligibilityMask(now);
            if (eligibilityMask != _scaleEligibilityMask)
            {
                _scaleEligibilityMask = eligibilityMask;
                RequestRenderRefresh();
            }
            UpdateCrossVenueComparison();
        }
        CatalogState = CatalogPresentationPolicy.PreserveOrMarkStale(
            CatalogState, CatalogLoadedAt(), now, CatalogMaxAge);
    }

    private void RequestRenderRefresh() => _renderUpdateGate.Request();

    private void RenderTimerOnTick(object? sender, EventArgs eventArgs)
    {
        if (Volatile.Read(ref _memoryWarningActive) != 0 &&
            Interlocked.Increment(ref _warningRenderTick) % 5 != 0) return;
        if (!_renderUpdateGate.TryConsume()) return;

        lock (_marketDataSync)
        {
            RebuildBook();
            ConnectionStatus = _client is null
                ? "UNAVAILABLE" : FormatConnectionState(_bybitState, _latestSnapshotAt);
            GateConnectionStatus = _gateClient is null
                ? "UNAVAILABLE" : FormatConnectionState(_gateState, _latestGateSnapshotAt);
            MexcConnectionStatus = _mexcClient is null
                ? "UNAVAILABLE" : FormatConnectionState(_mexcState, _latestMexcSnapshotAt);

            ApplyLatestBookPresentation(
                _latestSnapshot, _bybitState,
                value => BybitEmptyState = value,
                value => LastPrice = value,
                value => Spread = value);
            ApplyLatestBookPresentation(
                _latestGateSnapshot, _gateState,
                value => GateEmptyState = value,
                value => GateLastPrice = value,
                value => GateSpread = value);
            ApplyLatestBookPresentation(
                _latestMexcSnapshot, _mexcState,
                value => MexcEmptyState = value,
                value => MexcLastPrice = value,
                value => MexcSpread = value);

            if (Interlocked.Exchange(ref _latestCluster, null) is { } cluster)
            {
                Replace(ClusterLevels, cluster.Levels.Take(14).Select(ToViewModel));
                ClusterDelta = $"Δ {cluster.Delta:+0.####;-0.####;0}";
                ClusterInterval = $"{cluster.Interval.TotalSeconds:0} СЕК";
            }
            UpdateCrossVenueComparison();
        }
    }

    private static void ApplyLatestBookPresentation(
        OrderBookSnapshot? snapshot,
        MarketDataConnectionState state,
        Action<string> setEmptyState,
        Action<string> setLastPrice,
        Action<string> setSpread)
    {
        if (snapshot is null)
        {
            setEmptyState(MarketDataEmptyState(state));
            setLastPrice("—");
            setSpread("—");
            return;
        }

        setEmptyState(string.Empty);
        setLastPrice(snapshot.BestBid is { } bid ? FormatPrice(bid) : "—");
        setSpread(snapshot.Spread is { } spread ? FormatPrice(spread) : "—");
    }

    private int GetScaleEligibilityMask(DateTimeOffset now)
    {
        var mask = 0;
        if (SharedScaleBookSelection.IsEligible(
                _mexcState, _latestMexcSnapshot, _latestMexcSnapshotAt, now, _freshness))
            mask |= 1;
        if (SelectedProduct == MarketProduct.Perpetual && SharedScaleBookSelection.IsEligible(
                _gateState, _latestGateSnapshot, _latestGateSnapshotAt, now, _freshness))
            mask |= 2;
        if (SelectedProduct == MarketProduct.Perpetual && SharedScaleBookSelection.IsEligible(
                _bybitState, _latestSnapshot, _latestSnapshotAt, now, _freshness))
            mask |= 4;
        return mask;
    }

    private Task LoadInstrumentMetadataAsync(
        string baseAsset,
        string quoteAsset,
        MarketProduct product,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var instrument = new CanonicalInstrument(baseAsset, quoteAsset, product);
        var bybitTickSize = product == MarketProduct.Perpetual
            ? _publicCatalog.TickSize(instrument, TradingVenue.Bybit) : null;
        var gateTickSize = product == MarketProduct.Perpetual
            ? _publicCatalog.TickSize(instrument, TradingVenue.Gate) : null;
        var mexcTickSize = _publicCatalog.TickSize(instrument, TradingVenue.Mexc);
        lock (_marketDataSync)
        {
            token.ThrowIfCancellationRequested();
            _bybitTickSize = bybitTickSize;
            _gateTickSize = gateTickSize;
            _mexcTickSize = mexcTickSize;
        }
        RequestRenderRefresh();
        return Task.CompletedTask;
    }

    private async Task InitializeAsync()
    {
        try
        {
            await LoadPublicCatalogsAsync(
                initialLoad: true,
                cancellationToken: _catalogLifetime.Token).ConfigureAwait(false);
            var initial = CatalogSelectionPolicy.ChooseInitial(_publicCatalog)
                ?? throw new InvalidDataException("Public catalog union is empty.");
            if (_selectionController.SelectedInstrument is null)
                await SelectMarketAsync(initial.BaseAsset, initial.QuoteAsset, initial.Product,
                    _catalogLifetime.Token, forceRefresh: false).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ConnectionStatus = GateConnectionStatus = "UNAVAILABLE";
                CatalogState = "КАТАЛОГ · ОШИБКА";
            });
        }
        finally
        {
            if (!_catalogLifetime.IsCancellationRequested)
                _catalogRefreshTask = RunCatalogRefreshLoopAsync(_catalogLifetime.Token);
        }
    }

    private async Task RunCatalogRefreshLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(CatalogRefreshCheckInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!PublicCatalogRefreshPolicy.ShouldRefresh(
                        CatalogLoadedAt(), DateTimeOffset.UtcNow, CatalogRefreshAfter))
                    continue;
                try
                {
                    await LoadPublicCatalogsAsync(
                        initialLoad: false,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (IsRecoverableCatalogFailure(exception))
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _catalogBaseState = PublicCatalogFreshness.IsFresh(
                            CatalogLoadedAt(), DateTimeOffset.UtcNow, CatalogMaxAge)
                            ? "КАТАЛОГ · ОШИБКА ОБНОВЛЕНИЯ"
                            : "КАТАЛОГ · УСТАРЕЛ · ОШИБКА ОБНОВЛЕНИЯ";
                        CatalogState = _catalogBaseState;
                    });
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task LoadPublicCatalogsAsync(
        bool initialLoad,
        CancellationToken cancellationToken)
    {
        var tasks = new[]
        {
            PublicCatalogLoadIsolation.LoadBatchAsync(TradingVenue.Mexc, async () =>
            {
                var result = await new MexcInstrumentMetadataClient(_catalogHttpClient)
                    .GetSpotCatalogResultAsync(cancellationToken).ConfigureAwait(false);
                return new PublicCatalogBatch(result.Entries, result.InvalidEligibleCount);
            }),
            PublicCatalogLoadIsolation.LoadBatchAsync(TradingVenue.Mexc, async () =>
            {
                var result = await new MexcContractInstrumentMetadataClient(_catalogHttpClient)
                    .GetUsdtPerpetualCatalogResultAsync(cancellationToken).ConfigureAwait(false);
                return new PublicCatalogBatch(result.Entries, result.InvalidEligibleCount);
            }),
            PublicCatalogLoadIsolation.LoadBatchAsync(TradingVenue.Gate, async () =>
            {
                var result = await new GateInstrumentMetadataClient(_catalogHttpClient)
                    .GetUsdtPerpetualCatalogResultAsync(cancellationToken).ConfigureAwait(false);
                return new PublicCatalogBatch(result.Entries, result.RejectedCount);
            }),
            PublicCatalogLoadIsolation.LoadAsync(TradingVenue.Bybit, () =>
                new BybitInstrumentMetadataClient(_catalogHttpClient)
                .GetLinearPerpetualCatalogAsync(cancellationToken))
        };
        var catalogs = await Task.WhenAll(tasks).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var entries = catalogs.SelectMany(result => result.Entries).ToArray();
        var now = DateTimeOffset.UtcNow;
        var previousLoadedAt = CatalogLoadedAt();
        var decision = initialLoad
            ? entries.Length == 0
                ? PublicCatalogRefreshDecision.RetainStale
                : PublicCatalogRefreshDecision.Replace
            : PublicCatalogRefreshPolicy.Decide(
                previousLoadedAt, now, CatalogMaxAge, catalogs, tasks.Length,
                entries.Length);
        if (decision != PublicCatalogRefreshDecision.Replace)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _catalogBaseState = decision == PublicCatalogRefreshDecision.RetainFresh
                    ? "КАТАЛОГ · ОБНОВЛЕНИЕ ОТЛОЖЕНО"
                    : previousLoadedAt is null
                        ? "КАТАЛОГ · ОШИБКА"
                        : "КАТАЛОГ · УСТАРЕЛ";
                CatalogState = _catalogBaseState;
            });
            return;
        }
        if (entries.Length == 0)
            throw new InvalidDataException("Public catalogs are unavailable.");
        var candidateCatalog = new PublicInstrumentCatalog();
        candidateCatalog.Replace(entries);

        await _selectionLifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var active = _selectionController.SelectedInstrument;
            var previousMapping = active is { } selected
                ? CatalogMapping(_publicCatalog, selected) : null;
            var nextMapping = active is { } current
                ? CatalogMapping(candidateCatalog, current) : null;
            var reconciliation = PublicCatalogReconciliationPolicy.Decide(
                initialLoad, active, previousMapping ?? EmptyCatalogMapping(),
                nextMapping ?? EmptyCatalogMapping());
            var previousEntries = _publicCatalog.Snapshot();
            var activeUnavailable = false;

            try
            {
                _publicCatalog.Replace(candidateCatalog.Snapshot());

                if (reconciliation == PublicCatalogReconciliationAction.Bootstrap)
                {
                    var bootstrap = CatalogSelectionPolicy.ChooseInitial(_publicCatalog)
                        ?? throw new InvalidDataException("Refreshed catalog union is empty.");
                    if (!await SelectMarketUnderLifecycleGateAsync(bootstrap.BaseAsset,
                            bootstrap.QuoteAsset, bootstrap.Product, requestId: null,
                            cancellationToken, forceRefresh: false).ConfigureAwait(false))
                        throw new InvalidOperationException(
                            "Refreshed catalog could not bootstrap a public market.");
                }
                else if (active is { } activeInstrument &&
                         reconciliation != PublicCatalogReconciliationAction.None)
                {
                    if (reconciliation ==
                        PublicCatalogReconciliationAction.FailClosedActive)
                    {
                        await _selectionController.ClearAsync(cancellationToken)
                            .ConfigureAwait(false);
                        activeUnavailable = true;
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            ApplySelectionLabels(activeInstrument);
                            ConnectionStatus = GateConnectionStatus =
                                MexcConnectionStatus = "UNAVAILABLE";
                        });
                    }
                    else if (!await SelectMarketUnderLifecycleGateAsync(
                                 activeInstrument.BaseAsset, activeInstrument.QuoteAsset,
                                 activeInstrument.Product, requestId: null,
                                 cancellationToken, forceRefresh: true).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException(
                            "Active public market could not be reconciled.");
                    }
                }

                SetCatalogLoadedAt(now);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _catalogBaseState = catalogs.All(result => result.Succeeded &&
                        result.Entries.Count != 0 && !result.HasRejections)
                        ? "КАТАЛОГ · ГОТОВ" : "КАТАЛОГ · ЧАСТИЧНО ДОСТУПЕН";
                    CatalogState = activeUnavailable
                        ? "КАТАЛОГ · АКТИВ БОЛЬШЕ НЕДОСТУПЕН"
                        : _catalogBaseState;
                    RefreshCatalogSearch(SelectedProduct);
                    if (activeUnavailable)
                        CatalogState = "КАТАЛОГ · АКТИВ БОЛЬШЕ НЕДОСТУПЕН";
                });
            }
            catch (Exception exception) when (IsRecoverableCatalogFailure(exception))
            {
                _publicCatalog.Replace(previousEntries);
                SetCatalogLoadedAt(previousLoadedAt);
                try { await _selectionController.ClearAsync(cancellationToken)
                    .ConfigureAwait(false); }
                catch (Exception clearException) when (
                    IsRecoverableCatalogFailure(clearException)) { }
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ConnectionStatus = GateConnectionStatus =
                        MexcConnectionStatus = "UNAVAILABLE";
                    _catalogBaseState = "КАТАЛОГ · RECONCILIATION ERROR";
                    CatalogState = _catalogBaseState;
                });
                throw;
            }
        }
        finally { _selectionLifecycleGate.Release(); }
    }

    private static PublicCatalogEntry?[] CatalogMapping(
        PublicInstrumentCatalog catalog,
        CanonicalInstrument instrument) =>
        Enum.GetValues<TradingVenue>()
            .Select(venue => catalog.Get(instrument, venue))
            .ToArray();

    private static PublicCatalogEntry?[] EmptyCatalogMapping() =>
        new PublicCatalogEntry?[Enum.GetValues<TradingVenue>().Length];

    private static bool IsRecoverableCatalogFailure(Exception exception) =>
        exception is not (OperationCanceledException or OutOfMemoryException or
            StackOverflowException or AccessViolationException);

    private void RefreshCatalogSearch(MarketProduct product)
    {
        InstrumentSearchResults.Clear();
        foreach (var instrument in _publicCatalog.Search(product, InstrumentSearchText))
            InstrumentSearchResults.Add(instrument.PairId);
        if (!PublicCatalogFreshness.IsFresh(
                CatalogLoadedAt(), DateTimeOffset.UtcNow, CatalogMaxAge))
        {
            CatalogState = CatalogLoadedAt() is null
                ? "КАТАЛОГ · ЗАГРУЗКА" : "КАТАЛОГ · УСТАРЕЛ";
            return;
        }
        CatalogState = InstrumentSearchResults.Count == 0 && !_catalogBaseState.Contains("ЗАГРУЗКА")
            ? "КАТАЛОГ · НИЧЕГО НЕ НАЙДЕНО" : _catalogBaseState;
    }

    private DateTimeOffset? CatalogLoadedAt()
    {
        lock (_catalogSync) return _catalogLoadedAt;
    }

    private void SetCatalogLoadedAt(DateTimeOffset? value)
    {
        lock (_catalogSync) _catalogLoadedAt = value;
    }

    private async Task RefreshCredentialStatusAsync()
    {
        try
        {
            var readOnly = await _readOnlyCredentials.RefreshAsync();
            var orderTest = await _orderTestCredentials.RefreshAsync();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ApplyCredentialPresentation(isReadOnly: true, readOnly);
                ApplyCredentialPresentation(isReadOnly: false, orderTest);
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ApplyCredentialPresentation(true,
                    new(CredentialPairAction.Error, "ОШИБКА KEYCHAIN", false, false, false));
                ApplyCredentialPresentation(false,
                    new(CredentialPairAction.Error, "ОШИБКА KEYCHAIN", false, false, false));
            });
        }
    }

    private async Task SaveCredentialPairAsync(CredentialPairController controller,
        bool isReadOnly, bool replace)
    {
        var input = new CredentialPairInput
        {
            ApiKey = isReadOnly ? ReadOnlyApiKey : OrderTestApiKey,
            Secret = isReadOnly ? ReadOnlySecret : OrderTestSecret
        };
        try
        {
            ApplyCredentialPresentation(isReadOnly,
                await controller.SaveAsync(input, replace));
        }
        finally
        {
            if (isReadOnly)
            {
                ReadOnlyApiKey = string.Empty;
                ReadOnlySecret = string.Empty;
                ReadOnlyReplaceConfirmed = false;
            }
            else
            {
                OrderTestApiKey = string.Empty;
                OrderTestSecret = string.Empty;
                OrderTestReplaceConfirmed = false;
            }
        }
    }

    private void ApplyCredentialPresentation(bool isReadOnly,
        CredentialPairPresentation presentation)
    {
        if (isReadOnly)
        {
            ReadOnlyCredentialStatus = presentation.Action == CredentialPairAction.Stored
                ? "СОХРАНЕНО В KEYCHAIN" : presentation.MaskedMessage;
            ReadOnlyCredentialAction = presentation.MaskedMessage;
            CanRevokeReadOnlyCredential = presentation.CanArmRevoke;
            CanConfirmRevokeReadOnlyCredential = presentation.CanConfirmRevoke;
            CanConfirmReplaceReadOnlyCredential = presentation.CanConfirmReplace;
        }
        else
        {
            OrderTestCredentialStatus = presentation.Action == CredentialPairAction.Stored
                ? "СОХРАНЕНО В KEYCHAIN" : presentation.MaskedMessage;
            OrderTestCredentialAction = presentation.MaskedMessage;
            CanRevokeOrderTestCredential = presentation.CanArmRevoke;
            CanConfirmRevokeOrderTestCredential = presentation.CanConfirmRevoke;
            CanConfirmReplaceOrderTestCredential = presentation.CanConfirmReplace;
        }
    }

    private async Task LoadInstrumentMetadataSafelyAsync(
        string baseAsset,
        string quoteAsset,
        MarketProduct product,
        CancellationToken token)
    {
        try { await LoadInstrumentMetadataAsync(baseAsset, quoteAsset, product, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (token.IsCancellationRequested) { }
    }

    private void UpdateCrossVenueComparison()
    {
        if (SelectedProduct == MarketProduct.Spot)
        {
            (CrossVenueDivergence, CrossVenueDivergenceColor) =
                ("СРАВНЕНИЕ: НУЖНЫ 2 ДОСТУПНЫЕ БИРЖИ", "#8B93A1");
            return;
        }
        var result = CrossVenueBookComparison.Evaluate(
            new VenueBookObservation(
                "BYBIT",
                _bybitState,
                _latestSnapshot,
                _latestSnapshotAt),
            new VenueBookObservation(
                "GATE",
                _gateState,
                _latestGateSnapshot,
                _latestGateSnapshotAt),
            DateTimeOffset.UtcNow,
            _freshness);

        (CrossVenueDivergence, CrossVenueDivergenceColor) = result.Status switch
        {
            CrossVenueComparisonStatus.Ready when result.HigherVenue is null =>
                ($"РАСХОЖДЕНИЕ {result.DivergenceBasisPoints:0.00} BP · ЦЕНЫ РАВНЫ",
                    "#65DCA2"),
            CrossVenueComparisonStatus.Ready =>
                ($"РАСХОЖДЕНИЕ {result.DivergenceBasisPoints:0.00} BP · " +
                    $"{result.HigherVenue} ВЫШЕ",
                    result.DivergenceBasisPoints >= 5 ? "#F5C96A" : "#D8DDE5"),
            CrossVenueComparisonStatus.Warning =>
                ($"ЗАДЕРЖКА > {_freshness.WarningAfter.TotalMilliseconds:0} MS · " +
                    $"РАСХОЖДЕНИЕ {result.DivergenceBasisPoints:0.00} BP",
                    "#F5C96A"),
            CrossVenueComparisonStatus.Stale =>
                ("РАСХОЖДЕНИЕ: УСТАРЕВШИЕ ДАННЫЕ", "#FF9CA6"),
            CrossVenueComparisonStatus.NotLive =>
                ("РАСХОЖДЕНИЕ: ОДНА ИЗ БИРЖ НЕ В LIVE", "#8B93A1"),
            _ => ("РАСХОЖДЕНИЕ: НЕТ ПОЛНОГО СТАКАНА", "#8B93A1")
        };
    }

    private void ApplySelectionLabels(CanonicalInstrument selected)
    {
        InvalidateDryRunConfirmation("MARKET SELECTION CHANGED");
        var product = selected.Product == MarketProduct.Spot ? "SPOT" : "PERPETUAL";
        var cards = VenueCardLayout.Build(selected, _publicCatalog.Find);
        var mexc = cards.Single(card => card.Venue == TradingVenue.Mexc);
        var gate = cards.Single(card => card.Venue == TradingVenue.Gate);
        var bybit = cards.Single(card => card.Venue == TradingVenue.Bybit);
        InstrumentTitle = $"{selected.BaseAsset} / {selected.QuoteAsset} · {product}";
        ProductLabel = product;
        MexcCardTitle = $"MEXC · {mexc.Symbol} · {product}";
        GateCardTitle = $"GATE · {gate.Symbol} · {product}";
        BybitCardTitle = $"BYBIT · {bybit.Symbol} · {product}";
        MexcEmptyState = mexc.EmptyState;
        GateEmptyState = gate.EmptyState;
        BybitEmptyState = bybit.EmptyState;
        if (selected.Product == MarketProduct.Perpetual)
        {
            ConnectionStatus = bybit.MarketDataAvailable ? "CONNECTING" : "UNAVAILABLE";
            GateConnectionStatus = gate.MarketDataAvailable ? "CONNECTING" : "UNAVAILABLE";
            MexcConnectionStatus = mexc.MarketDataAvailable ? "CONNECTING" : "UNAVAILABLE";
        }
        else
        {
            ConnectionStatus = GateConnectionStatus = "UNAVAILABLE";
            MexcConnectionStatus = mexc.MarketDataAvailable ? "CONNECTING" : "UNAVAILABLE";
        }
        var preferredVenue = selected.Product == MarketProduct.Spot
            ? TradingVenue.Mexc
            : TradingVenue.Gate;
        ActiveDryRunVenue = preferredVenue.ToString().ToUpperInvariant();
        DryRunUnit = _dryRunSide == OrderSide.Buy ? selected.QuoteAsset : selected.BaseAsset;
        RefreshDryRunPreview();
    }

    private IPublicMarketDataClient CreateClient(VenueInstrumentCapability capability)
    {
        var metadata = _publicCatalog.Get(capability.Instrument, capability.Venue)
            ?? throw new InvalidOperationException("Official catalog entry is missing.");
        return capability.Venue switch
        {
            TradingVenue.Bybit => new BybitPublicOrderBookClient(
                capability.VenueSymbol,
                clusterPriceStep: metadata.TickSize ??
                    throw new InvalidOperationException("Official Bybit tick is missing.")),
            TradingVenue.Gate => new GatePublicMarketDataClient(
                capability.VenueSymbol,
                contractMultiplier: metadata.QuantityMultiplier ??
                    throw new InvalidOperationException("Official Gate multiplier is missing."),
                clusterPriceStep: metadata.TickSize ??
                    throw new InvalidOperationException("Official Gate tick is missing.")),
            TradingVenue.Mexc when capability.Instrument.Product == MarketProduct.Spot =>
                new MexcPublicOrderBookClient(
                    _publicMarketDataHttpClient,
                    capability.VenueSymbol),
            TradingVenue.Mexc when capability.Instrument.Product == MarketProduct.Perpetual =>
                new MexcContractPublicOrderBookClient(
                    _publicMarketDataHttpClient,
                    capability.VenueSymbol,
                    metadata.QuantityMultiplier ??
                        throw new InvalidOperationException("Official MEXC contract multiplier is missing.")),
            _ => throw new InvalidOperationException(
                $"{capability.Venue} is not supported on the Perpetual screen.")
        };
    }

    private void AttachClients()
    {
        if (_client is not null)
        {
            var generation = _generation.Current;
            _bybitSnapshotHandler = value => OnSnapshotReceived(value, generation);
            _bybitClusterHandler = value => OnClusterReceived(value, generation);
            _bybitTradesHandler = value => OnTradesReceived(value, generation);
            _bybitStateHandler = (state, detail) => OnStateChanged(state, detail, generation);
            _client.SnapshotReceived += _bybitSnapshotHandler;
            _client.ClusterReceived += _bybitClusterHandler;
            _client.TradesReceived += _bybitTradesHandler;
            _client.StateChanged += _bybitStateHandler;
        }
        if (_gateClient is not null)
        {
            var generation = _generation.Current;
            _gateSnapshotHandler = value => OnGateSnapshotReceived(value, generation);
            _gateTradesHandler = value => OnGateTradesReceived(value, generation);
            _gateStateHandler = (state, detail) => OnGateStateChanged(state, detail, generation);
            _gateClient.SnapshotReceived += _gateSnapshotHandler;
            _gateClient.TradesReceived += _gateTradesHandler;
            _gateClient.StateChanged += _gateStateHandler;
        }
        if (_mexcClient is not null)
        {
            var generation = _generation.Current;
            _mexcSnapshotHandler = value => OnMexcSnapshotReceived(value, generation);
            _mexcStateHandler = (state, detail) => OnMexcStateChanged(state, detail, generation);
            _mexcClient.SnapshotReceived += _mexcSnapshotHandler;
            _mexcClient.StateChanged += _mexcStateHandler;
        }
    }

    private void DetachClients()
    {
        if (_client is not null)
        {
            if (_bybitSnapshotHandler is not null) _client.SnapshotReceived -= _bybitSnapshotHandler;
            if (_bybitClusterHandler is not null) _client.ClusterReceived -= _bybitClusterHandler;
            if (_bybitTradesHandler is not null) _client.TradesReceived -= _bybitTradesHandler;
            if (_bybitStateHandler is not null) _client.StateChanged -= _bybitStateHandler;
        }
        if (_gateClient is not null)
        {
            if (_gateSnapshotHandler is not null) _gateClient.SnapshotReceived -= _gateSnapshotHandler;
            if (_gateTradesHandler is not null) _gateClient.TradesReceived -= _gateTradesHandler;
            if (_gateStateHandler is not null) _gateClient.StateChanged -= _gateStateHandler;
        }
        if (_mexcClient is not null)
        {
            if (_mexcSnapshotHandler is not null) _mexcClient.SnapshotReceived -= _mexcSnapshotHandler;
            if (_mexcStateHandler is not null) _mexcClient.StateChanged -= _mexcStateHandler;
        }
        _client = null;
        _gateClient = null;
        _mexcClient = null;
        _bybitSnapshotHandler = null;
        _bybitClusterHandler = null;
        _bybitTradesHandler = null;
        _bybitStateHandler = null;
        _gateSnapshotHandler = null;
        _gateTradesHandler = null;
        _gateStateHandler = null;
        _mexcSnapshotHandler = null;
        _mexcStateHandler = null;
    }

    private void OnSelectionResetting()
    {
        long resetGeneration;
        lock (_marketDataSync)
        {
            resetGeneration = _generation.Next();
            _liquidityTrackers.ResetAll();
            _latestSnapshot = null;
            _latestGateSnapshot = null;
            _latestMexcSnapshot = null;
            _latestCluster = null;
            _latestSnapshotAt = default;
            _latestGateSnapshotAt = default;
            _latestMexcSnapshotAt = default;
            _latestLiquidity = new Dictionary<(LiquiditySide, decimal), LiquidityLevelState>();
            _latestGateLiquidity = new Dictionary<(LiquiditySide, decimal), LiquidityLevelState>();
            _latestMexcLiquidity = new Dictionary<(LiquiditySide, decimal), LiquidityLevelState>();
            _bybitTickSize = null;
            _gateTickSize = null;
            _mexcTickSize = null;
            _bybitState = MarketDataConnectionState.Connecting;
            _gateState = MarketDataConnectionState.Connecting;
            _mexcState = MarketDataConnectionState.Disconnected;
            _scaleEligibilityMask = -1;
        }
        DetachClients();
        _metadataLifetime.Cancel();
        _metadataLifetime.Dispose();

        Dispatcher.UIThread.Post(() =>
        {
            if (!_generation.IsCurrent(resetGeneration)) return;
            Asks.Clear();
            Bids.Clear();
            GateAsks.Clear();
            GateBids.Clear();
            MexcAsks.Clear();
            MexcBids.Clear();
            ClusterLevels.Clear();
            LastPrice = GateLastPrice = MexcLastPrice = Spread = GateSpread = MexcSpread = "—";
            ConnectionStatus = GateConnectionStatus = "CONNECTING";
            SharedScaleLabel = "НЕЗАВИСИМЫЕ ШКАЛЫ: —";
            ConsensusVerdict = "ЭВРИСТИКА: НЕТ ДАННЫХ";
            ConsensusColor = "#8B93A1";
            lock (_marketDataSync) UpdateCrossVenueComparison();
        });
    }

    private static IEnumerable<VenueLiquiditySignal> CollectSignals(
        string venue,
        OrderBookSnapshot? snapshot,
        IReadOnlyDictionary<(LiquiditySide Side, decimal Price), LiquidityLevelState>
            liquidity,
        int depth)
    {
        if (snapshot is null)
        {
            return [];
        }

        var result = new List<VenueLiquiditySignal>();
        Add(snapshot.Bids.Take(depth), LiquiditySide.Bid);
        Add(snapshot.Asks.Take(depth), LiquiditySide.Ask);
        return result;

        void Add(IEnumerable<OrderBookLevel> levels, LiquiditySide side)
        {
            var visible = levels.ToArray();
            if (visible.Length == 0)
            {
                return;
            }
            var ordered = visible.Select(level => level.Quantity).Order().ToArray();
            var median = ordered[ordered.Length / 2];
            var threshold = decimal.Max(median * 4, ordered[^1] * 0.35m);
            foreach (var level in visible.Where(level => level.Quantity >= threshold))
            {
                if (liquidity.TryGetValue((side, level.Price), out var state))
                {
                    result.Add(new VenueLiquiditySignal(
                        venue,
                        side,
                        state.Behavior,
                        level.Quantity));
                }
            }
        }
    }

    private void OnClusterReceived(TradeCluster cluster, long expectedGeneration)
    {
        lock (_marketDataSync)
        {
            if (!_generation.IsCurrent(expectedGeneration)) return;
            Interlocked.Exchange(ref _latestCluster, cluster);
        }
        RequestRenderRefresh();
    }

    private static IEnumerable<BookLevelPresentation> ToBookPresentations(
        IEnumerable<OrderBookLevel> source,
        IEnumerable<decimal> priceScale,
        LiquiditySide side,
        IReadOnlyDictionary<(LiquiditySide Side, decimal Price), LiquidityLevelState> liquidity,
        BookDisplayLayout layout,
        double textSize,
        decimal visibleLargest,
        decimal volumeReference,
        BookBarPalette palette)
    {
        var levelsByPrice = source.ToDictionary(
            static level => level.Price,
            static level => level);
        var prices = priceScale.ToArray();
        var levels = prices
            .Where(levelsByPrice.ContainsKey)
            .Select(price => levelsByPrice[price])
            .ToArray();
        if (prices.Length == 0)
        {
            return [];
        }

        var quantities = levels
            .Select(level => level.Quantity)
            .Order()
            .ToArray();
        var maximum = quantities.Length == 0 ? 0 : quantities[^1];
        var median = quantities.Length == 0
            ? 0
            : quantities[quantities.Length / 2];
        var significanceThreshold = decimal.Max(median * 4, maximum * 0.35m);

        return prices.Select(price =>
        {
            if (!levelsByPrice.TryGetValue(price, out var level))
            {
                return new BookLevelPresentation(
                    FormatPrice(price),
                    string.Empty,
                    0,
                    "#00000000",
                    "#5E6570",
                    string.Empty,
                    string.Empty,
                    "#8B93A1",
                    "#00000000",
                    layout.RowHeight,
                    textSize,
                    Math.Max(10, textSize - 2),
                    0.28);
            }

            var normalized = VisibleBookVolumeScale.Ratio(
                level.Quantity, volumeReference);
            var isLargest = visibleLargest > 0 && level.Quantity == visibleLargest;
            var barColor = side == LiquiditySide.Ask
                ? isLargest ? palette.LargestAsk : palette.Ask
                : isLargest ? palette.LargestBid : palette.Bid;
            var isSignificant =
                level.Quantity >= significanceThreshold &&
                level.Quantity > median;
            var behavior = liquidity.TryGetValue((side, level.Price), out var state)
                ? state.Behavior
                : LiquidityBehavior.Normal;
            var (behaviorText, behaviorColor, behaviorBackground) = behavior switch
            {
                LiquidityBehavior.Building =>
                    ("+ ДОБАВЛЯЮТ", "#FFD977", "#503C2F12"),
                LiquidityBehavior.Holding =>
                    ("ДЕРЖАТ", "#D8DDE5", "#403A414C"),
                LiquidityBehavior.Pulling =>
                    ("− СНИМАЮТ", "#FF9CA6", "#503F151B"),
                LiquidityBehavior.Absorbing =>
                    ("ПОГЛОЩАЮТ", "#85F0BA", "#50305522"),
                _ => (string.Empty, "#8B93A1", "#00000000")
            };

            return new BookLevelPresentation(
                FormatPrice(level.Price),
                level.Quantity.ToString("0.####", CultureInfo.InvariantCulture),
                normalized * layout.BarWidth,
                barColor,
                "#F7FAFF",
                isSignificant ? "◆" : string.Empty,
                isSignificant ? behaviorText : string.Empty,
                behaviorColor,
                isSignificant ? behaviorBackground : "#00000000",
                layout.RowHeight,
                textSize,
                Math.Max(10, textSize - 2),
                1);
        });
    }

    private static ClusterLevelViewModel ToViewModel(ClusterLevel level)
    {
        var total = level.TotalVolume;
        var imbalance = total == 0
            ? 0
            : decimal.Abs(level.Delta) / total * 100;

        return new ClusterLevelViewModel(
            FormatPrice(level.Price),
            level.BidVolume.ToString("0.####", CultureInfo.InvariantCulture),
            level.AskVolume.ToString("0.####", CultureInfo.InvariantCulture),
            $"{imbalance:0}%");
    }

    private static void Replace<T>(
        ObservableCollection<T> target,
        IEnumerable<T> source)
    {
        target.Clear();

        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private static void ReplaceBookLevels(
        ObservableCollection<BookLevelViewModel> target,
        IEnumerable<BookLevelPresentation> source)
    {
        var values = source.ToArray();
        var shared = Math.Min(target.Count, values.Length);
        for (var index = 0; index < shared; index++)
        {
            target[index].Apply(values[index]);
        }
        while (target.Count > values.Length)
        {
            target.RemoveAt(target.Count - 1);
        }
        for (var index = shared; index < values.Length; index++)
        {
            target.Add(new BookLevelViewModel(values[index]));
        }
    }

    private static string FormatPrice(decimal price) =>
        price < 10
            ? price.ToString("0.0000", CultureInfo.InvariantCulture)
            : price.ToString("N1", CultureInfo.InvariantCulture);
}
