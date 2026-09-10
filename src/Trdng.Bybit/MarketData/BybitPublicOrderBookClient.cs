using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Trdng.Core.Clusters;
using Trdng.Core.MarketData;

namespace Trdng.Bybit.MarketData;

public sealed class BybitPublicOrderBookClient : IPublicMarketDataClient
{
    private static readonly Uri LinearPublicEndpoint =
        new("wss://stream.bybit.com/v5/public/linear");

    private readonly CancellationTokenSource _lifetime = new();
    private readonly BybitOrderBookSession _session;
    private readonly TradeClusterAggregator _clusterAggregator;
    private readonly string _symbol;
    private readonly int _depth;
    private Task? _runTask;

    public BybitPublicOrderBookClient(
        string symbol = "BTCUSDT",
        int depth = 200,
        decimal clusterPriceStep = 0.5m)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        if (depth is not (1 or 50 or 200 or 1000))
        {
            throw new ArgumentOutOfRangeException(
                nameof(depth),
                "Bybit linear depth must be 1, 50, 200, or 1000.");
        }

        _symbol = symbol.ToUpperInvariant();
        _depth = depth;
        _session = new BybitOrderBookSession(new OrderBookEngine(
            new OrderBookCapacityPolicy(depth, checked(depth * 2))));
        _clusterAggregator = new TradeClusterAggregator(
            TimeSpan.FromSeconds(15), clusterPriceStep, maxCompletedClusters: 1);
    }

    public event Action<OrderBookSnapshot>? SnapshotReceived;

    public event Action<TradeCluster>? ClusterReceived;

    public event Action<IReadOnlyList<PublicTrade>>? TradesReceived;

    public event Action<MarketDataConnectionState, string?>? StateChanged;

    public string Venue => "BYBIT";

    public void Start()
    {
        if (_runTask is not null)
        {
            throw new InvalidOperationException("The market-data client is already running.");
        }

        _runTask = RunAsync(_lifetime.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();

        if (_runTask is not null)
        {
            try
            {
                await _runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
        }

        _lifetime.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var firstAttempt = true;

        while (!cancellationToken.IsCancellationRequested)
        {
            ChangeState(
                firstAttempt
                    ? MarketDataConnectionState.Connecting
                    : MarketDataConnectionState.Reconnecting);
            firstAttempt = false;

            try
            {
                using var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                await socket.ConnectAsync(LinearPublicEndpoint, cancellationToken)
                    .ConfigureAwait(false);

                await SubscribeAsync(socket, cancellationToken).ConfigureAwait(false);
                _session.OnDisconnected();
                _clusterAggregator.Reset();
                ChangeState(MarketDataConnectionState.WaitingForSnapshot);

                await ReceiveLoopAsync(socket, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (WebSocketMessageEnvelopeException exception)
            {
                ChangeState(MarketDataConnectionState.Reconnecting, exception.SafeCode);
            }
            catch (Exception exception) when (
                exception is WebSocketException or IOException or InvalidDataException)
            {
                ChangeState(MarketDataConnectionState.Reconnecting, exception.Message);
            }
            finally
            {
                _session.OnDisconnected();
                _clusterAggregator.Reset();
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken)
                .ConfigureAwait(false);
        }

        ChangeState(MarketDataConnectionState.Disconnected);
    }

    private async Task ReceiveLoopAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        using var messageReader = new BoundedWebSocketMessageReader();
        var renderClock = Stopwatch.StartNew();
        var lastBookPublishedAt = TimeSpan.Zero;
        var lastClusterPublishedAt = TimeSpan.Zero;

        try
        {
            while (socket.State == WebSocketState.Open &&
                   !cancellationToken.IsCancellationRequested)
            {
                var message = await messageReader.ReadAsync(socket, cancellationToken)
                    .ConfigureAwait(false);

                if (message.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                if (message.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                ProcessMessage(
                    message.Payload,
                    renderClock.Elapsed,
                    ref lastBookPublishedAt,
                    ref lastClusterPublishedAt);
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new InvalidDataException(
                "Bybit sent an invalid order-book message; resynchronization is required.",
                exception);
        }
    }

    private void ProcessMessage(
        ReadOnlyMemory<byte> messageBytes,
        TimeSpan now,
        ref TimeSpan lastBookPublishedAt,
        ref TimeSpan lastClusterPublishedAt)
    {
        if (BybitOrderBookMessageParser.TryParse(messageBytes, out var message) &&
            message is not null)
        {
            ProcessOrderBook(message, now, ref lastBookPublishedAt);
            return;
        }

        if (!BybitPublicTradeMessageParser.TryParse(messageBytes, out var trades))
        {
            return;
        }

        foreach (var trade in trades)
        {
            _clusterAggregator.Ingest(trade);
        }

        TradesReceived?.Invoke(trades);

        if (now - lastClusterPublishedAt < TimeSpan.FromMilliseconds(100) ||
            _clusterAggregator.CaptureCurrent() is not { } cluster)
        {
            return;
        }

        ClusterReceived?.Invoke(cluster);
        lastClusterPublishedAt = now;
    }

    private void ProcessOrderBook(
        BybitOrderBookMessage message,
        TimeSpan now,
        ref TimeSpan lastPublishedAt)
    {
        var result = _session.Apply(message);

        if (result == OrderBookApplyResult.ResyncRequired)
        {
            throw new InvalidDataException("ORDER_BOOK_RESYNC_REQUIRED");
        }

        if (result == OrderBookApplyResult.SnapshotApplied)
        {
            ChangeState(MarketDataConnectionState.Live);
        }

        if (_session.State != OrderBookSessionState.Live ||
            now - lastPublishedAt < TimeSpan.FromMilliseconds(75))
        {
            return;
        }

        SnapshotReceived?.Invoke(_session.Engine.Capture(Math.Min(200, _depth)));
        lastPublishedAt = now;
    }

    private async Task SubscribeAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(
            $$"""{"op":"subscribe","args":["orderbook.{{_depth}}.{{_symbol}}","publicTrade.{{_symbol}}"]}""");

        await socket.SendAsync(
            payload,
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken).ConfigureAwait(false);
    }

    private void ChangeState(
        MarketDataConnectionState state,
        string? detail = null) =>
        StateChanged?.Invoke(state, detail);
}
