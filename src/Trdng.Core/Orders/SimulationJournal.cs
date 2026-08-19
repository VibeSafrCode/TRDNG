using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Trdng.Core.Orders;

public sealed record SimulationJournalEvent(
    long Sequence,
    DateTimeOffset Timestamp,
    string ClientOrderId,
    string Fingerprint,
    SimulationOrderState State,
    SimulationTransitionKind TransitionKind,
    string Reason,
    MarketOrderIntent? Intent = null);

public enum SimulationTransitionKind { Standard, Reconciliation, Recovery }

public interface ISimulationJournal
{
    IReadOnlyList<SimulationJournalEvent> ReadAll();
    void Append(SimulationJournalEvent value);
}

public sealed class InMemorySimulationJournal(int capacity) : ISimulationJournal
{
    private readonly List<SimulationJournalEvent> _events = [];
    public IReadOnlyList<SimulationJournalEvent> ReadAll() => _events.ToArray();
    public void Append(SimulationJournalEvent value)
    {
        if (_events.Count >= capacity)
            throw new InvalidOperationException("SIMULATION JOURNAL CAP REACHED");
        _events.Add(value);
    }
}

public sealed class FileSimulationJournal : ISimulationJournal
{
    private sealed record Envelope(string Payload, string Checksum);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _path;
    private readonly int _capacity;

    public FileSimulationJournal(string path, int capacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _path = path;
        _capacity = capacity;
    }

    public IReadOnlyList<SimulationJournalEvent> ReadAll()
    {
        if (!File.Exists(_path)) return [];
        var bytes = File.ReadAllBytes(_path);
        var endsWithNewline = bytes.Length == 0 || bytes[^1] == (byte)'\n';
        var lines = Encoding.UTF8.GetString(bytes).Split('\n');
        var committedCount = endsWithNewline ? lines.Length - 1 : lines.Length - 1;
        var result = new List<SimulationJournalEvent>();
        for (var index = 0; index < committedCount; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index])) continue;
            Envelope envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<Envelope>(lines[index], JsonOptions)
                    ?? throw Corrupt(index, null);
            }
            catch (JsonException exception) { throw Corrupt(index, exception); }
            if (string.IsNullOrWhiteSpace(envelope.Payload) ||
                string.IsNullOrWhiteSpace(envelope.Checksum))
                throw Corrupt(index, null);
            byte[] payloadBytes;
            try { payloadBytes = Convert.FromBase64String(envelope.Payload); }
            catch (FormatException exception) { throw Corrupt(index, exception); }
            var checksum = Convert.ToHexStringLower(SHA256.HashData(payloadBytes));
            if (!string.Equals(checksum, envelope.Checksum, StringComparison.Ordinal))
                throw Corrupt(index, null);
            try
            {
                result.Add(JsonSerializer.Deserialize<SimulationJournalEvent>(payloadBytes, JsonOptions)
                    ?? throw Corrupt(index, null));
            }
            catch (JsonException exception) { throw Corrupt(index, exception); }
        }
        if (result.Count > _capacity) throw new InvalidDataException("JOURNAL CAP EXCEEDED");
        return result;
    }

    public void Append(SimulationJournalEvent value)
    {
        var committed = ReadAll();
        if (committed.Count >= _capacity)
            throw new InvalidOperationException("SIMULATION JOURNAL CAP REACHED");
        TruncateUncommittedTail();
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        var envelope = new Envelope(
            Convert.ToBase64String(payloadBytes),
            Convert.ToHexStringLower(SHA256.HashData(payloadBytes)));
        var line = JsonSerializer.Serialize(envelope, JsonOptions) + "\n";
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write,
            FileShare.Read, 4096, FileOptions.WriteThrough);
        var bytes = Encoding.UTF8.GetBytes(line);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private void TruncateUncommittedTail()
    {
        if (!File.Exists(_path)) return;
        var bytes = File.ReadAllBytes(_path);
        if (bytes.Length == 0 || bytes[^1] == (byte)'\n') return;
        var lastNewline = Array.LastIndexOf(bytes, (byte)'\n');
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Write,
            FileShare.Read, 4096, FileOptions.WriteThrough);
        stream.SetLength(lastNewline + 1L);
        stream.Flush(flushToDisk: true);
    }

    private static InvalidDataException Corrupt(int index, Exception? inner) =>
        new($"CORRUPT COMMITTED JOURNAL ENTRY {index}", inner);
}
