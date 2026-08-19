using Trdng.Core.MarketData;

namespace Trdng.Gate.MarketData;

public sealed class GateOrderBookSession
{
    public GateOrderBookSession(OrderBookEngine engine) =>
        Engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public OrderBookEngine Engine { get; }

    public bool IsLive => Engine.HasSnapshot;

    public bool Apply(GateOrderBookMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.IsSnapshot)
        {
            Engine.ApplySnapshot(message.Update);
            return true;
        }
        if (!Engine.HasSnapshot)
        {
            return false;
        }
        if (message.FirstUpdateId != Engine.LastUpdateId + 1)
        {
            throw new InvalidDataException(
                $"Gate book gap: expected {Engine.LastUpdateId + 1}, " +
                $"received {message.FirstUpdateId}.");
        }
        return Engine.TryApplyDelta(message.Update);
    }

    public void Reset() => Engine.Reset();
}
