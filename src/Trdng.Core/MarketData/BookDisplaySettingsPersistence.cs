using System.Text.Json;
using Trdng.Core.Instruments;

namespace Trdng.Core.MarketData;

public sealed record BookDisplaySettingsSnapshot(
    TradingVenue Venue,
    bool AutomaticDepth,
    int ManualDepth,
    int GestureStep,
    bool AutomaticVolumeScale,
    decimal ManualVolumeReference,
    string AskColor,
    string LargestAskColor,
    string BidColor,
    string LargestBidColor);

public enum BookDisplaySettingsLoadState
{
    Loaded,
    NotFound,
    Invalid,
    Unavailable
}

public enum BookDisplaySettingsSaveState
{
    Saved,
    Invalid,
    Unavailable
}

public sealed record BookDisplaySettingsLoadResult(
    BookDisplaySettingsLoadState State,
    IReadOnlyDictionary<TradingVenue, BookDisplaySettingsSnapshot> Settings);

public static class BookDisplaySettingsValidation
{
    public static bool IsValid(BookDisplaySettingsSnapshot settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(settings.Venue)) return false;
        var maximumDepth = settings.Venue == TradingVenue.Gate ? 50 : 200;
        return settings.ManualDepth >= BookDisplayPolicy.MinimumDepth &&
            settings.ManualDepth <= maximumDepth &&
            settings.GestureStep is >= 1 and <= 20 &&
            settings.ManualVolumeReference > 0 &&
            settings.ManualVolumeReference <= BookDisplayPolicy.MaximumManualVolumeReference &&
            BookBarPalette.TryCreate(settings.AskColor, settings.LargestAskColor,
                settings.BidColor, settings.LargestBidColor, out _);
    }
}

public sealed class FileBookDisplaySettingsStore
{
    private const int CurrentVersion = 1;
    public const int MaximumDocumentBytes = 32 * 1024;
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly object _sync = new();
    private readonly string _path;

    public FileBookDisplaySettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public BookDisplaySettingsLoadResult Load()
    {
        lock (_sync)
        {
            try
            {
                if (!File.Exists(_path))
                    return new(BookDisplaySettingsLoadState.NotFound,
                        EmptySettings());
                using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 4096, FileOptions.SequentialScan);
                if (stream.Length is <= 0 or > MaximumDocumentBytes)
                    return new(BookDisplaySettingsLoadState.Invalid, EmptySettings());
                var document = JsonSerializer.Deserialize<Document>(stream, JsonOptions);
                if (!TryValidateDocument(document, out var settings))
                    return new(BookDisplaySettingsLoadState.Invalid, EmptySettings());
                return new(BookDisplaySettingsLoadState.Loaded, settings);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException)
            {
                return new(BookDisplaySettingsLoadState.Invalid, EmptySettings());
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new(BookDisplaySettingsLoadState.Unavailable, EmptySettings());
            }
        }
    }

    public BookDisplaySettingsSaveState Save(
        IEnumerable<BookDisplaySettingsSnapshot> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var items = settings.ToArray();
        if (!TryValidateItems(items, out _))
            return BookDisplaySettingsSaveState.Invalid;
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new Document(CurrentVersion, items), JsonOptions);
        if (payload.Length > MaximumDocumentBytes)
            return BookDisplaySettingsSaveState.Invalid;

        lock (_sync)
        {
            var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                using (var stream = new FileStream(temporaryPath, FileMode.Create,
                           FileAccess.Write, FileShare.None, 4096,
                           FileOptions.WriteThrough))
                {
                    stream.Write(payload);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporaryPath, _path, overwrite: true);
                return BookDisplaySettingsSaveState.Saved;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return BookDisplaySettingsSaveState.Unavailable;
            }
            finally
            {
                try { File.Delete(temporaryPath); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private static bool TryValidateDocument(
        Document? document,
        out IReadOnlyDictionary<TradingVenue, BookDisplaySettingsSnapshot> settings)
    {
        settings = EmptySettings();
        if (document is null || document.Version != CurrentVersion ||
            document.Books is null || !TryValidateItems(document.Books, out var validated))
            return false;
        settings = validated;
        return true;
    }

    private static bool TryValidateItems(
        IReadOnlyList<BookDisplaySettingsSnapshot> items,
        out IReadOnlyDictionary<TradingVenue, BookDisplaySettingsSnapshot> settings)
    {
        settings = EmptySettings();
        if (items.Count != 3 || items.Any(item => item is null ||
            !BookDisplaySettingsValidation.IsValid(item))) return false;
        var mapped = new Dictionary<TradingVenue, BookDisplaySettingsSnapshot>();
        foreach (var item in items)
            if (!mapped.TryAdd(item.Venue, item)) return false;
        if (!Enum.GetValues<TradingVenue>().All(mapped.ContainsKey)) return false;
        settings = mapped;
        return true;
    }

    private static IReadOnlyDictionary<TradingVenue, BookDisplaySettingsSnapshot>
        EmptySettings() =>
        new Dictionary<TradingVenue, BookDisplaySettingsSnapshot>();

    private sealed record Document(
        int Version,
        IReadOnlyList<BookDisplaySettingsSnapshot> Books);
}

public sealed class LatestBookDisplaySettingsWriter
{
    private readonly Func<IReadOnlyList<BookDisplaySettingsSnapshot>,
        BookDisplaySettingsSaveState> _save;
    private readonly Action<BookDisplaySettingsSaveState>? _completed;
    private readonly object _sync = new();
    private BookDisplaySettingsSnapshot[]? _pending;
    private Task _activeTask = Task.CompletedTask;
    private bool _active;

    public LatestBookDisplaySettingsWriter(
        Func<IReadOnlyList<BookDisplaySettingsSnapshot>,
            BookDisplaySettingsSaveState> save,
        Action<BookDisplaySettingsSaveState>? completed = null)
    {
        _save = save ?? throw new ArgumentNullException(nameof(save));
        _completed = completed;
    }

    public void Queue(IEnumerable<BookDisplaySettingsSnapshot> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var snapshot = settings.ToArray();
        lock (_sync)
        {
            _pending = snapshot;
            if (_active) return;
            StartUnderLock();
        }
    }

    public async Task FlushAsync(IEnumerable<BookDisplaySettingsSnapshot> settings)
    {
        Queue(settings);
        while (true)
        {
            Task current;
            lock (_sync) current = _activeTask;
            await current.ConfigureAwait(false);
            lock (_sync)
            {
                if (!_active && _pending is null) return;
            }
        }
    }

    private void StartUnderLock()
    {
        _active = true;
        _activeTask = Task.Run(Drain);
    }

    private void Drain()
    {
        try
        {
            while (true)
            {
                BookDisplaySettingsSnapshot[] snapshot;
                lock (_sync)
                {
                    if (_pending is not { } pending) return;
                    snapshot = pending;
                    _pending = null;
                }

                BookDisplaySettingsSaveState result;
                try { result = _save(snapshot); }
                catch (Exception exception) when (IsRecoverable(exception))
                {
                    result = BookDisplaySettingsSaveState.Unavailable;
                }
                try { _completed?.Invoke(result); }
                catch (Exception exception) when (IsRecoverable(exception)) { }
            }
        }
        finally
        {
            lock (_sync)
            {
                _active = false;
                if (_pending is not null) StartUnderLock();
            }
        }
    }

    private static bool IsRecoverable(Exception exception) => exception is
        IOException or UnauthorizedAccessException or ArgumentException or
        InvalidOperationException or NotSupportedException or PathTooLongException;
}
