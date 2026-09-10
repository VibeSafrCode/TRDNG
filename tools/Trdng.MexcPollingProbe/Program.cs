using System.Diagnostics;
using System.Text.Json;
using Trdng.Core.MarketData;
using Trdng.Mexc.MarketData;

const int polls = 20;
var interval = TimeSpan.FromMilliseconds(750);
using var httpClient = PublicHttpTransport.CreateClient(TimeSpan.FromSeconds(5));
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));

var catalog = await new MexcContractInstrumentMetadataClient(httpClient)
    .GetUsdtPerpetualCatalogResultAsync(timeout.Token);
var entry = catalog.Entries.Single(item =>
    item.Instrument.BaseAsset == "BTC" && item.Instrument.QuoteAsset == "USDT");
var multiplier = entry.QuantityMultiplier ??
    throw new InvalidDataException("BTC contract multiplier is missing.");

await using var client = new MexcContractPublicOrderBookClient(
    httpClient,
    entry.VenueSymbol,
    multiplier,
    interval,
    maximumPolls: polls);
var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var snapshots = 0;
var reconnects = 0;
client.SnapshotReceived += _ => Interlocked.Increment(ref snapshots);
client.StateChanged += (state, _) =>
{
    if (state == MarketDataConnectionState.Reconnecting)
        Interlocked.Increment(ref reconnects);
    if (state == MarketDataConnectionState.Disconnected)
        completed.TrySetResult();
};

var process = Process.GetCurrentProcess();
var stopwatch = Stopwatch.StartNew();
client.Start();
await completed.Task.WaitAsync(timeout.Token);
stopwatch.Stop();
process.Refresh();

var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
var pollsPerMinute = snapshots / elapsedSeconds * 60d;
var output = new
{
    result = snapshots == polls && reconnects == 0 ? "PASS" : "FAIL",
    symbol = entry.VenueSymbol,
    configuredPolls = polls,
    completedSnapshots = snapshots,
    reconnects,
    configuredIntervalMs = interval.TotalMilliseconds,
    elapsedMilliseconds = stopwatch.ElapsedMilliseconds,
    pollsPerMinute,
    workingSetBytes = process.WorkingSet64
};
Console.WriteLine(JsonSerializer.Serialize(output));
return output.result == "PASS" ? 0 : 2;
