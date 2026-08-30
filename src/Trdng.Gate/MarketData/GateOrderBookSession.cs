using Trdng.Core.MarketData;

namespace Trdng.Gate.MarketData;

public enum GateOrderBookSessionState
{
    WaitingForSnapshot,
    Live,
    ResyncRequired
}

public sealed class GateOrderBookSession
{
    public GateOrderBookSession(OrderBookEngine engine) =>
        Engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public OrderBookEngine Engine { get; }

    public GateOrderBookSessionState State { get; private set; } =
        GateOrderBookSessionState.WaitingForSnapshot;

    public bool IsLive => State == GateOrderBookSessionState.Live;

    public bool Apply(GateOrderBookMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            if (message.IsSnapshot)
            {
                Engine.ApplySnapshot(message.Update);
                State = GateOrderBookSessionState.Live;
                return true;
            }
            if (State == GateOrderBookSessionState.ResyncRequired)
            {
                throw new InvalidDataException("ORDER_BOOK_RESYNC_REQUIRED");
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
        catch (OrderBookPolicyViolationException exception)
        {
            Engine.Reset();
            State = GateOrderBookSessionState.ResyncRequired;
            throw new InvalidDataException(exception.SafeCode, exception);
        }
    }

    public void Reset()
    {
        Engine.Reset();
        State = GateOrderBookSessionState.WaitingForSnapshot;
    }
}
