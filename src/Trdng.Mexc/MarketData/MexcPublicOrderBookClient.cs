using System.Net.WebSockets;
using System.Text;
using Trdng.Core.Clusters;
using Trdng.Core.MarketData;

namespace Trdng.Mexc.MarketData;

public sealed class MexcPublicOrderBookClient : IPublicMarketDataClient
{
    private static readonly Uri WebSocketEndpoint = new("wss://wbs-api.mexc.com/ws");
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HttpClient _httpClient;
    private readonly MexcOrderBookSession _session;
    private readonly string _symbol;
    private readonly int _depth;
    private readonly int? _maxConnectionAttempts;
    private Task? _runTask;

    public MexcPublicOrderBookClient(
        HttpClient httpClient,
        string symbol,
        int depth = 1000,
        int? maxConnectionAttempts = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        if (depth is not (1000 or 5000))
            throw new ArgumentOutOfRangeException(nameof(depth), "MEXC synchronized depth must be 1000 or 5000.");
        _symbol = symbol.ToUpperInvariant();
        _depth = depth;
        if (maxConnectionAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maxConnectionAttempts));
        _maxConnectionAttempts = maxConnectionAttempts;
        _session = new MexcOrderBookSession(
            new OrderBookEngine(new OrderBookCapacityPolicy(
                depth,
                checked(depth * 2))),
            _symbol);
    }

    public string Venue => "MEXC";
    public event Action<OrderBookSnapshot>? SnapshotReceived;
    public event Action<TradeCluster>? ClusterReceived { add { } remove { } }
    public event Action<IReadOnlyList<PublicTrade>>? TradesReceived { add { } remove { } }
    public event Action<MarketDataConnectionState, string?>? StateChanged;
    public event Action<string>? DiagnosticReceived;

    public void Start()
    {
        if (_runTask is not null) throw new InvalidOperationException("Client already started.");
        _runTask = RunAsync(_lifetime.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        if (_runTask is not null)
        {
            try { await _runTask.ConfigureAwait(false); }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        }
        _lifetime.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var first = true;
        var attempts = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_maxConnectionAttempts is { } maximum && attempts == maximum) break;
            attempts++;
            ChangeState(first ? MarketDataConnectionState.Connecting : MarketDataConnectionState.Reconnecting);
            first = false;
            try
            {
                using var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                await socket.ConnectAsync(WebSocketEndpoint, cancellationToken).ConfigureAwait(false);
                _session.OnDisconnected();
                await SubscribeAsync(socket, cancellationToken).ConfigureAwait(false);
                DiagnosticReceived?.Invoke($"connected endpoint={WebSocketEndpoint} symbol={_symbol}");
                ChangeState(MarketDataConnectionState.WaitingForSnapshot);

                using var syncLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var snapshotTask = FetchSnapshotAsync(syncLifetime.Token);
                await ReceiveLoopAsync(socket, snapshotTask, cancellationToken).ConfigureAwait(false);
                await syncLifetime.CancelAsync();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (WebSocketMessageEnvelopeException exception)
            {
                ChangeState(MarketDataConnectionState.Reconnecting, exception.SafeCode);
            }
            catch (Exception exception) when (exception is WebSocketException or HttpRequestException or InvalidDataException or IOException)
            {
                ChangeState(MarketDataConnectionState.Reconnecting, exception.Message);
            }
            finally { _session.OnDisconnected(); }

            if (_maxConnectionAttempts is { } limit && attempts == limit) break;
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
        ChangeState(MarketDataConnectionState.Disconnected);
    }

    private async Task ReceiveLoopAsync(
        ClientWebSocket socket,
        Task<OrderBookUpdate> snapshotTask,
        CancellationToken cancellationToken)
    {
        using var messageReader = new BoundedWebSocketMessageReader();
        var snapshotApplied = false;
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var message = await messageReader.ReadAsync(socket, cancellationToken)
                .ConfigureAwait(false);
            if (message.MessageType == WebSocketMessageType.Close) return;

            if (message.MessageType == WebSocketMessageType.Text)
            {
                DiagnosticReceived?.Invoke($"MEXC_WS_TEXT_ACK bytes={message.Payload.Length}");
            }
            else if (message.MessageType == WebSocketMessageType.Binary &&
                MexcProtobufDepthParser.TryParse(message.Payload, out var delta) && delta is not null)
            {
                DiagnosticReceived?.Invoke($"delta symbol={delta.Symbol} from={delta.FromVersion} to={delta.ToVersion} sendTime={delta.SendTime}");
                var applyResult = _session.BufferOrApply(delta);
                if (!snapshotApplied && snapshotTask.IsCompleted)
                {
                    applyResult = _session.ApplySnapshot(await snapshotTask.ConfigureAwait(false));
                    snapshotApplied = true;
                    DiagnosticReceived?.Invoke($"snapshot version={_session.Engine.LastUpdateId} decision={_session.LastDecision}");
                }
                DiagnosticReceived?.Invoke($"decision={_session.LastDecision} result={applyResult}");
                if (applyResult == MexcOrderBookApplyResult.ResyncRequired)
                    throw new InvalidDataException("MEXC order-book continuity lost; reconnect/resnapshot required.");
                if (_session.State == MexcOrderBookSessionState.Live)
                {
                    ChangeState(MarketDataConnectionState.Live);
                    SnapshotReceived?.Invoke(_session.Engine.Capture(Math.Min(30, _depth)));
                }
            }
        }
    }

    private async Task<OrderBookUpdate> FetchSnapshotAsync(CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://api.mexc.com/api/v3/depth?symbol={Uri.EscapeDataString(_symbol)}&limit={_depth}");
        var json = await _httpClient.GetByteArrayAsync(uri, cancellationToken).ConfigureAwait(false);
        var snapshot = MexcDepthSnapshotParser.Parse(json, _symbol);
        DiagnosticReceived?.Invoke($"rest-snapshot symbol={snapshot.Symbol} lastUpdateId={snapshot.UpdateId}");
        return snapshot;
    }

    private async Task SubscribeAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(
            $$"""{"method":"SUBSCRIPTION","params":["spot@public.aggre.depth.v3.api.pb@100ms@{{_symbol}}"]}""");
        await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    private void ChangeState(MarketDataConnectionState state, string? detail = null) =>
        StateChanged?.Invoke(state, detail);
}
