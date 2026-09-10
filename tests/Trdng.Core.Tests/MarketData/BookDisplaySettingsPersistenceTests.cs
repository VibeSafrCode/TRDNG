using Trdng.Core.Instruments;
using Trdng.Core.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class BookDisplaySettingsPersistenceTests
{
    [Fact]
    public void RoundTripIsAtomicBoundedAndExact()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "book-settings.v1.json");
            var store = new FileBookDisplaySettingsStore(path);
            var settings = AllSettings();

            Assert.Equal(BookDisplaySettingsLoadState.NotFound, store.Load().State);
            Assert.Equal(BookDisplaySettingsSaveState.Saved, store.Save(settings));
            var loaded = store.Load();

            Assert.Equal(BookDisplaySettingsLoadState.Loaded, loaded.State);
            Assert.Equal(3, loaded.Settings.Count);
            Assert.Equal(settings[0], loaded.Settings[TradingVenue.Mexc]);
            Assert.Equal(settings[1], loaded.Settings[TradingVenue.Gate]);
            Assert.Equal(settings[2], loaded.Settings[TradingVenue.Bybit]);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
            Assert.InRange(new FileInfo(path).Length, 1,
                FileBookDisplaySettingsStore.MaximumDocumentBytes);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void CorruptOversizedOrIncompleteDocumentsFailClosedToDefaults()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "book-settings.v1.json");
            var store = new FileBookDisplaySettingsStore(path);
            File.WriteAllText(path, "{truncated");
            Assert.Equal(BookDisplaySettingsLoadState.Invalid, store.Load().State);

            File.WriteAllBytes(path,
                new byte[FileBookDisplaySettingsStore.MaximumDocumentBytes + 1]);
            Assert.Equal(BookDisplaySettingsLoadState.Invalid, store.Load().State);

            Assert.Equal(BookDisplaySettingsSaveState.Invalid,
                store.Save(AllSettings().Take(2)));
            Assert.Equal(BookDisplaySettingsSaveState.Invalid,
                store.Save([AllSettings()[0], AllSettings()[0], AllSettings()[2]]));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void InvalidDepthVolumeOrPaletteIsNeverPersisted()
    {
        var directory = TemporaryDirectory();
        try
        {
            var store = new FileBookDisplaySettingsStore(
                Path.Combine(directory, "book-settings.v1.json"));
            var valid = AllSettings();
            var invalidDepth = valid.ToArray();
            invalidDepth[0] = invalidDepth[0] with { ManualDepth = 201 };
            var invalidVolume = valid.ToArray();
            invalidVolume[1] = invalidVolume[1] with { ManualVolumeReference = 0 };
            var invalidPalette = valid.ToArray();
            invalidPalette[2] = invalidPalette[2] with { AskColor = "red" };
            var oversizedVolume = valid.ToArray();
            oversizedVolume[1] = oversizedVolume[1] with
            {
                ManualVolumeReference = BookDisplayPolicy.MaximumManualVolumeReference + 1
            };
            Assert.Equal(BookDisplaySettingsSaveState.Invalid,
                store.Save(invalidDepth));
            Assert.Equal(BookDisplaySettingsSaveState.Invalid,
                store.Save(invalidVolume));
            Assert.Equal(BookDisplaySettingsSaveState.Invalid,
                store.Save(invalidPalette));
            Assert.Equal(BookDisplaySettingsSaveState.Invalid,
                store.Save(oversizedVolume));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task FlushOnClosePersistsLatestQueuedSnapshot()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new List<IReadOnlyList<BookDisplaySettingsSnapshot>>();
        var writer = new LatestBookDisplaySettingsWriter(settings =>
        {
            lock (writes) writes.Add(settings.ToArray());
            if (writes.Count == 1)
            {
                entered.TrySetResult();
                release.Task.GetAwaiter().GetResult();
            }
            return BookDisplaySettingsSaveState.Saved;
        });
        var first = AllSettings();
        var intermediate = AllSettings();
        intermediate[0] = intermediate[0] with { ManualDepth = 41 };
        var final = AllSettings();
        final[0] = final[0] with { ManualDepth = 42 };

        writer.Queue(first);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        writer.Queue(intermediate);
        var flush = writer.FlushAsync(final);
        release.TrySetResult();
        await flush.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, writes.Count);
        Assert.Equal(42, writes[^1][0].ManualDepth);
    }

    [Fact]
    public async Task RecoverableWriterFailureDoesNotWedgeLaterFlush()
    {
        var calls = 0;
        var observed = new List<BookDisplaySettingsSaveState>();
        var writer = new LatestBookDisplaySettingsWriter(settings =>
        {
            _ = settings;
            if (Interlocked.Increment(ref calls) == 1)
                throw new InvalidOperationException("synthetic storage boundary");
            return BookDisplaySettingsSaveState.Saved;
        }, observed.Add);

        await writer.FlushAsync(AllSettings());
        await writer.FlushAsync(AllSettings());

        Assert.Equal(2, calls);
        Assert.Equal([
            BookDisplaySettingsSaveState.Unavailable,
            BookDisplaySettingsSaveState.Saved
        ], observed);
    }

    private static BookDisplaySettingsSnapshot[] AllSettings() =>
    [
        Snapshot(TradingVenue.Mexc, 40),
        Snapshot(TradingVenue.Gate, 32),
        Snapshot(TradingVenue.Bybit, 80)
    ];

    private static BookDisplaySettingsSnapshot Snapshot(
        TradingVenue venue,
        int depth) => new(
        venue,
        AutomaticDepth: false,
        ManualDepth: depth,
        GestureStep: 2,
        AutomaticVolumeScale: false,
        ManualVolumeReference: 25_000,
        AskColor: "#B3FFD60A",
        LargestAskColor: "#D9FF453A",
        BidColor: "#B30A84FF",
        LargestBidColor: "#D930D158");

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(),
            "trdng-book-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
