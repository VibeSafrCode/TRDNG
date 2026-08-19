namespace Trdng.Core.Orders;

public enum SimulationScenario
{
    AcknowledgeAndFill,
    PartialAndFill,
    Reject,
    TimeoutBeforeAcknowledge,
    TimeoutAfterAcknowledge,
    DuplicateAndOutOfOrder
}

public sealed class DeterministicSimulationAdapter(SimulationOrderStore store)
{
    public SimulationOrderRecord Play(MarketOrderIntent intent, SimulationScenario scenario)
    {
        var submitted = store.Submit(intent);
        if (submitted.State != SimulationOrderState.Submitted) return submitted;
        return scenario switch
        {
            SimulationScenario.AcknowledgeAndFill => AckAndFill(intent),
            SimulationScenario.PartialAndFill => PartialAndFill(intent),
            SimulationScenario.Reject => store.ApplyCallback(
                intent.ClientOrderId, SimulationOrderState.Rejected, "SIMULATED REJECT"),
            SimulationScenario.TimeoutBeforeAcknowledge => store.ApplyCallback(
                intent.ClientOrderId, SimulationOrderState.Unknown, "TIMEOUT · NO AUTO-RETRY"),
            SimulationScenario.TimeoutAfterAcknowledge => TimeoutAfterAck(intent),
            SimulationScenario.DuplicateAndOutOfOrder => DuplicateAndOutOfOrder(intent),
            _ => submitted
        };
    }

    private SimulationOrderRecord AckAndFill(MarketOrderIntent intent)
    {
        store.ApplyCallback(intent.ClientOrderId, SimulationOrderState.Acknowledged, "ACK");
        return store.ApplyCallback(intent.ClientOrderId, SimulationOrderState.Filled, "FILLED");
    }

    private SimulationOrderRecord PartialAndFill(MarketOrderIntent intent)
    {
        store.ApplyCallback(intent.ClientOrderId, SimulationOrderState.Acknowledged, "ACK");
        store.ApplyCallback(intent.ClientOrderId, SimulationOrderState.PartiallyFilled, "PARTIAL");
        return store.ApplyCallback(intent.ClientOrderId, SimulationOrderState.Filled, "FILLED");
    }

    private SimulationOrderRecord TimeoutAfterAck(MarketOrderIntent intent)
    {
        store.ApplyCallback(intent.ClientOrderId, SimulationOrderState.Acknowledged, "ACK");
        return store.ApplyCallback(intent.ClientOrderId, SimulationOrderState.Unknown,
            "TRANSPORT LOST · NO AUTO-RETRY");
    }

    private SimulationOrderRecord DuplicateAndOutOfOrder(MarketOrderIntent intent)
    {
        store.ApplyCallback(intent.ClientOrderId, SimulationOrderState.Acknowledged, "ACK");
        store.ApplyCallback(intent.ClientOrderId, SimulationOrderState.Acknowledged, "DUPLICATE ACK");
        store.ApplyCallback(intent.ClientOrderId, SimulationOrderState.PartiallyFilled, "PARTIAL");
        store.ApplyCallback(intent.ClientOrderId, SimulationOrderState.Acknowledged, "OUT OF ORDER ACK");
        return store.ApplyCallback(intent.ClientOrderId, SimulationOrderState.Filled, "FILLED");
    }
}
