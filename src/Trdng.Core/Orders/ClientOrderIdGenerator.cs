namespace Trdng.Core.Orders;

public sealed class ClientOrderIdGenerator(
    Func<DateTimeOffset>? clock = null,
    Func<long>? sequence = null)
{
    public const int MaximumLength = ClientOrderIdPolicy.MaximumLength;
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Func<long> _sequence = sequence ?? CreateSequence();

    public string Next()
    {
        var timestamp = _clock().ToUnixTimeMilliseconds();
        var value = $"trdng-{timestamp:x}-{_sequence():x}";
        if (!ClientOrderIdPolicy.IsValid(value))
            throw new InvalidOperationException("Generated client order id is invalid.");
        return value;
    }

    private static Func<long> CreateSequence()
    {
        long value = 0;
        return () => Interlocked.Increment(ref value);
    }
}
