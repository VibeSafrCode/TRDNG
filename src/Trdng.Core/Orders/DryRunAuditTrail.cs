using Trdng.Core.Instruments;

namespace Trdng.Core.Orders;

public enum DryRunAuditAction
{
    Prepare, Allow, Block, Confirm, Reject, KillSwitchEngaged, KillSwitchDisengaged
}

public sealed record DryRunAuditEvent(
    DateTimeOffset Timestamp,
    DryRunAuditAction Action,
    string ClientOrderId,
    TradingVenue? Venue,
    MarketProduct? Product,
    OrderSide? Side,
    OrderSizingMode? SizingMode,
    decimal? SizingValue,
    string Reason);

public sealed class DryRunAuditTrail
{
    private readonly int _capacity;
    private readonly Queue<DryRunAuditEvent> _events = new();

    public DryRunAuditTrail(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    public IReadOnlyList<DryRunAuditEvent> Events => _events.ToArray();

    public void Add(DryRunAuditEvent value)
    {
        while (_events.Count >= _capacity) _events.Dequeue();
        _events.Enqueue(value);
    }
}
