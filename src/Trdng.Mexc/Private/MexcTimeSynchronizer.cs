using System.Text.Json;

namespace Trdng.Mexc.Private;

public sealed class MexcTimeSynchronizer
{
    private readonly object _sync = new();
    private readonly long _maxAgeMilliseconds;
    private readonly long _maxRoundTripMilliseconds;
    private long _offsetMilliseconds;
    private long _observedAtMilliseconds;
    private bool _ready;

    public MexcTimeSynchronizer(TimeSpan maxAge, TimeSpan maxRoundTrip)
    {
        if (maxAge < TimeSpan.FromMilliseconds(1))
            throw new ArgumentOutOfRangeException(nameof(maxAge));
        if (maxRoundTrip < TimeSpan.FromMilliseconds(1))
            throw new ArgumentOutOfRangeException(nameof(maxRoundTrip));
        _maxAgeMilliseconds = checked((long)maxAge.TotalMilliseconds);
        _maxRoundTripMilliseconds = checked((long)maxRoundTrip.TotalMilliseconds);
    }

    public bool Record(long serverTime, long sentAt, long receivedAt)
    {
        if (serverTime <= 0 || sentAt <= 0 || receivedAt < sentAt) return false;
        try
        {
            var roundTrip = checked(receivedAt - sentAt);
            if (roundTrip > _maxRoundTripMilliseconds) return false;
            var midpoint = checked(sentAt + (roundTrip / 2));
            var offset = checked(serverTime - midpoint);
            lock (_sync)
            {
                _offsetMilliseconds = offset;
                _observedAtMilliseconds = receivedAt;
                _ready = true;
            }
            return true;
        }
        catch (OverflowException) { return false; }
    }

    public bool TryTimestamp(long now, out long timestamp)
    {
        timestamp = 0;
        long offset;
        long observed;
        lock (_sync)
        {
            if (!_ready) return false;
            offset = _offsetMilliseconds;
            observed = _observedAtMilliseconds;
        }
        if (now < observed) return false;
        try
        {
            if (checked(now - observed) > _maxAgeMilliseconds) return false;
            timestamp = checked(now + offset);
            return timestamp > 0;
        }
        catch (OverflowException) { return false; }
    }

    public static long ParseServerTime(ReadOnlyMemory<byte> json)
    {
        using var doc = JsonDocument.Parse(json);
        var value = doc.RootElement.GetProperty("serverTime").GetInt64();
        return value > 0 ? value : throw new InvalidDataException("MEXC server time is invalid.");
    }
}
