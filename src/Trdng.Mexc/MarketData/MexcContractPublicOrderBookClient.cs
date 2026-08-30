using System.Globalization;
using System.Text.Json;
using Trdng.Core.Clusters;
using Trdng.Core.MarketData;

namespace Trdng.Mexc.MarketData;

public sealed class MexcContractPublicOrderBookClient : IPublicMarketDataClient
{
    internal const int MaximumDepthResponseBytes = 4 * 1024 * 1024;
    private static readonly Uri BaseEndpoint =
        new("https://contract.mexc.com/api/v1/contract/depth/");
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HttpClient _httpClient;
    private readonly string _symbol;
    private readonly decimal _contractMultiplier;
    private readonly TimeSpan _pollInterval;
    private readonly int? _maximumPolls;
    private Task? _runTask;

    public MexcContractPublicOrderBookClient(
        HttpClient httpClient,
        string symbol,
        decimal contractMultiplier,
        TimeSpan? pollInterval = null,
        int? maximumPolls = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        if (symbol.Any(char.IsWhiteSpace)) throw new ArgumentException("MEXC contract symbol is invalid.", nameof(symbol));
        if (contractMultiplier <= 0) throw new ArgumentOutOfRangeException(nameof(contractMultiplier));
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(750);
        if (_pollInterval < TimeSpan.FromMilliseconds(250))
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        if (maximumPolls <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPolls));
        _symbol = symbol.ToUpperInvariant();
        _contractMultiplier = contractMultiplier;
        _maximumPolls = maximumPolls;
    }

    public string Venue => "MEXC";
    public event Action<OrderBookSnapshot>? SnapshotReceived;
    public event Action<TradeCluster>? ClusterReceived { add { } remove { } }
    public event Action<IReadOnlyList<PublicTrade>>? TradesReceived { add { } remove { } }
    public event Action<MarketDataConnectionState, string?>? StateChanged;

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
        ChangeState(MarketDataConnectionState.Connecting);
        ChangeState(MarketDataConnectionState.WaitingForSnapshot);
        var live = false;
        var completedPolls = 0;
        while (!cancellationToken.IsCancellationRequested &&
            (_maximumPolls is null || completedPolls < _maximumPolls))
        {
            try
            {
                var json = await BoundedHttpContentReader.GetJsonBytesAsync(
                    _httpClient,
                    new Uri(BaseEndpoint, Uri.EscapeDataString(_symbol)),
                    MaximumDepthResponseBytes,
                    cancellationToken).ConfigureAwait(false);
                var snapshot = ParseDepth(json, _symbol, _contractMultiplier, 200);
                completedPolls++;
                if (!live) ChangeState(MarketDataConnectionState.Live, "PUBLIC_REST_POLL");
                live = true;
                SnapshotReceived?.Invoke(snapshot);
                if (_maximumPolls is not null && completedPolls >= _maximumPolls) break;
                await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or
                JsonException or InvalidDataException or OverflowException)
            {
                live = false;
                ChangeState(MarketDataConnectionState.Reconnecting, exception.Message);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                ChangeState(MarketDataConnectionState.WaitingForSnapshot);
            }
        }
        ChangeState(MarketDataConnectionState.Disconnected);
    }

    public static OrderBookSnapshot ParseDepth(
        ReadOnlyMemory<byte> utf8Json,
        string expectedSymbol,
        decimal contractMultiplier,
        int maximumDepth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSymbol);
        if (contractMultiplier <= 0) throw new ArgumentOutOfRangeException(nameof(contractMultiplier));
        if (maximumDepth is < 1 or > 1_000) throw new ArgumentOutOfRangeException(nameof(maximumDepth));

        using var document = JsonDocument.Parse(utf8Json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True ||
            !root.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.Number ||
            !code.TryGetInt32(out var codeValue) || codeValue != 0 ||
            !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !TryPositiveInt64(data, "version", out var version) ||
            !data.TryGetProperty("bids", out var bids) || bids.ValueKind != JsonValueKind.Array ||
            !data.TryGetProperty("asks", out var asks) || asks.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("MEXC contract depth root is invalid.");

        var parsedBids = ParseSide(bids, contractMultiplier, descending: true, maximumDepth);
        var parsedAsks = ParseSide(asks, contractMultiplier, descending: false, maximumDepth);
        if (parsedBids.Count == 0 || parsedAsks.Count == 0 ||
            parsedBids[0].Price >= parsedAsks[0].Price)
            throw new InvalidDataException("MEXC contract depth is empty or crossed.");
        return new(expectedSymbol.ToUpperInvariant(), version, version, parsedBids, parsedAsks);
    }

    private static IReadOnlyList<OrderBookLevel> ParseSide(
        JsonElement source,
        decimal contractMultiplier,
        bool descending,
        int maximumDepth)
    {
        if (source.GetArrayLength() > 5_000)
            throw new InvalidDataException("MEXC contract depth side is too large.");
        var levels = new Dictionary<decimal, decimal>();
        foreach (var tuple in source.EnumerateArray())
        {
            if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() < 2)
                throw new InvalidDataException("MEXC contract depth level is invalid.");
            var fields = tuple.EnumerateArray().Take(2).ToArray();
            if (!TryPositiveDecimal(fields[0], out var price) ||
                !TryNonNegativeDecimal(fields[1], out var contracts))
                throw new InvalidDataException("MEXC contract depth values are invalid.");
            if (contracts == 0) continue;
            var quantity = checked(contracts * contractMultiplier);
            if (quantity <= 0 || !levels.TryAdd(price, quantity))
                throw new InvalidDataException("MEXC contract depth contains duplicate or invalid levels.");
        }
        var ordered = descending
            ? levels.OrderByDescending(item => item.Key)
            : levels.OrderBy(item => item.Key);
        return ordered.Take(maximumDepth)
            .Select(item => new OrderBookLevel(item.Key, item.Value)).ToArray();
    }

    private static bool TryPositiveInt64(JsonElement item, string name, out long value)
    {
        value = default;
        if (!item.TryGetProperty(name, out var property)) return false;
        return (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value) ||
                property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) && value > 0;
    }

    private static bool TryPositiveDecimal(JsonElement value, out decimal result) =>
        TryDecimal(value, out result) && result > 0;

    private static bool TryNonNegativeDecimal(JsonElement value, out decimal result) =>
        TryDecimal(value, out result) && result >= 0;

    private static bool TryDecimal(JsonElement value, out decimal result)
    {
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() :
            value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out result);
    }

    private void ChangeState(MarketDataConnectionState state, string? detail = null) =>
        StateChanged?.Invoke(state, detail);
}
