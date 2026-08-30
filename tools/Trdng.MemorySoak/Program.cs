using System.Globalization;
using System.Text.Json;
using Trdng.Core.Diagnostics;

var cycles = ReadInt(args, "--cycles", 200_000);
var switchEvery = ReadInt(args, "--switch-every", 2_000);
var sampleEvery = ReadInt(args, "--sample-every", 2_000);
var bookDepth = ReadInt(args, "--book-depth", 256);
var recorder = new RuntimeMemoryRecorder(capacity: 256);
var options = new DeterministicReplayOptions(
    cycles,
    switchEvery,
    sampleEvery,
    bookDepth);
var result = await DeterministicMarketDataReplay.RunAsync(options, recorder);
var samples = recorder.Samples;
var budget = new MemorySoakBudget(
    maximumManagedHeapBytes: 256L * 1024 * 1024,
    maximumWorkingSetBytes: 768L * 1024 * 1024,
    maximumPrivateMemoryBytes: 2L * 1024 * 1024 * 1024,
    maximumRetainedManagedGrowthBytes: 64L * 1024 * 1024,
    maximumAllocatedBytesPerOperation: 32L * 1024);
var evaluation = budget.Evaluate(samples, result.AppliedBookUpdates);

foreach (var sample in samples)
    Console.WriteLine(JsonSerializer.Serialize(new { kind = "memory-sample", sample }));
Console.WriteLine(JsonSerializer.Serialize(new
{
    kind = "summary",
    result,
    evaluation
}));
Environment.ExitCode = evaluation.Passed ? 0 : 2;

static int ReadInt(string[] values, string name, int fallback)
{
    var index = Array.IndexOf(values, name);
    if (index < 0) return fallback;
    if (index == values.Length - 1 ||
        !int.TryParse(values[index + 1], NumberStyles.None,
            CultureInfo.InvariantCulture, out var parsed))
        throw new ArgumentException($"{name} requires a positive integer.");
    return parsed;
}
