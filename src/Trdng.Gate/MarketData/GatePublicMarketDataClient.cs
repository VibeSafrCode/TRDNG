using System.Buffers;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Trdng.Core.Clusters;
using Trdng.Core.MarketData;

namespace Trdng.Gate.MarketData;

public sealed class GatePublicMarketDataClient : IPublicMarketDataClient
{
    private static readonly Uri Endpoint =
        new("wss://fx-ws.gateio.ws/v4/ws/usdt");
    private readonly CancellationTokenSource _lifetime = new();
    private readonly GateOrderBookSession _session = new(new OrderBookEngine());
    private readonly TradeClusterAggregator _clusters;
    private readonly string _contract;
    private readonly decimal _contractMultiplier;
    private Task? _runTask;

    public GatePublicMarketDataClient(
        string contract = "BTC_USDT",
        decimal contractMultiplier = 0.0001m,
        decimal clusterPriceStep = 0.5m)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contract);
        if (contractMultiplier <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contractMultiplier));
        }

        _contract = contract.ToUpperInvariant();
        _contractMultiplier = contractMultiplier;
        _clusters =
            new TradeClusterAggregator(TimeSpan.FromSeconds(15), clusterPriceStep);
    }

    public string Venue => "GATE";
    public event Action<OrderBookSnapshot>? SnapshotReceived;
    public event Action<TradeCluster>? ClusterReceived;
    public event Action<IReadOnlyList<PublicTrade>>? TradesReceived;
    public event Action<MarketDataConnectionState, string?>? StateChanged;

    public void Start()
    {
        if (_runTask is not null)
        {
            throw new InvalidOperationException("Gate client is already running.");
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

    private async Task RunAsync(CancellationToken token)
    {
        var first = true;
        while (!token.IsCancellationRequested)
        {
            ChangeState(first
                ? MarketDataConnectionState.Connecting
                : MarketDataConnectionState.Reconnecting);
            first = false;
            try
            {
                using var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                socket.Options.SetRequestHeader("X-Gate-Size-Decimal", "1");
                await socket.ConnectAsync(Endpoint, token).ConfigureAwait(false);
                await SubscribeAsync(socket, token).ConfigureAwait(false);
                _session.Reset();
                _clusters.Reset();
                ChangeState(MarketDataConnectionState.WaitingForSnapshot);
                await ReceiveLoopAsync(socket, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (
                exception is WebSocketException or IOException or
                InvalidDataException or JsonException)
            {
                ChangeState(MarketDataConnectionState.Reconnecting, exception.Message);
            }
            finally
            {
                _session.Reset();
                _clusters.Reset();
            }
            await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
        }
        ChangeState(MarketDataConnectionState.Disconnected);
    }

    private async Task SubscribeAsync(
        ClientWebSocket socket,
        CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string[] payloads =
        [
            $$"""{"time":{{now}},"channel":"futures.obu","event":"subscribe","payload":["ob.{{_contract}}.50"]}""",
            $$"""{"time":{{now}},"channel":"futures.trades","event":"subscribe","payload":["{{_contract}}"]}"""
        ];
        foreach (var payload in payloads)
        {
            await socket.SendAsync(
                Encoding.UTF8.GetBytes(payload),
                WebSocketMessageType.Text,
                true,
                token).ConfigureAwait(false);
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
    {
        var rented = ArrayPool<byte>.Shared.Rent(64 * 1024);
        var buffer = new ArrayBufferWriter<byte>(64 * 1024);
        var clock = Stopwatch.StartNew();
        var lastBook = TimeSpan.Zero;
        var lastCluster = TimeSpan.Zero;
        try
        {
            while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(rented.AsMemory(), token)
                    .ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }
                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }
                buffer.Write(rented.AsSpan(0, result.Count));
                if (!result.EndOfMessage)
                {
                    continue;
                }
                Process(buffer.WrittenMemory, clock.Elapsed, ref lastBook, ref lastCluster);
                buffer.Clear();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private void Process(
        ReadOnlyMemory<byte> bytes,
        TimeSpan now,
        ref TimeSpan lastBook,
        ref TimeSpan lastCluster)
    {
        if (GateOrderBookMessageParser.TryParse(
                bytes,
                out var book,
                _contractMultiplier) &&
            book is not null)
        {
            var wasLive = _session.IsLive;
            if (!_session.Apply(book))
            {
                return;
            }
            if (!wasLive && _session.IsLive)
            {
                ChangeState(MarketDataConnectionState.Live);
            }
            if (now - lastBook >= TimeSpan.FromMilliseconds(75))
            {
                SnapshotReceived?.Invoke(_session.Engine.Capture(30));
                lastBook = now;
            }
            return;
        }

        if (!GateTradeMessageParser.TryParse(
                bytes,
                out var trades,
                _contractMultiplier))
        {
            return;
        }
        foreach (var trade in trades)
        {
            _clusters.Ingest(trade);
        }
        TradesReceived?.Invoke(trades);
        if (now - lastCluster >= TimeSpan.FromMilliseconds(100) &&
            _clusters.CaptureCurrent() is { } cluster)
        {
            ClusterReceived?.Invoke(cluster);
            lastCluster = now;
        }
    }

    private void ChangeState(MarketDataConnectionState state, string? detail = null) =>
        StateChanged?.Invoke(state, detail);
}
