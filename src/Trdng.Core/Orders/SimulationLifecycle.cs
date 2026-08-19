namespace Trdng.Core.Orders;

public enum SimulationOrderState
{
    Confirmed, Submitted, Acknowledged, PartiallyFilled, Filled, Rejected, Unknown
}

public sealed record SimulationOrderRecord(
    MarketOrderIntent Intent,
    string Fingerprint,
    SimulationOrderState State,
    string Reason,
    DateTimeOffset UpdatedAt)
{
    public bool RequiresReconciliation => State == SimulationOrderState.Unknown;
}

public static class SimulationStateMachine
{
    public static bool CanTransition(
        SimulationOrderState from,
        SimulationOrderState to,
        bool reconciliationEvidence = false) =>
        (from, to) switch
        {
            (SimulationOrderState.Confirmed, SimulationOrderState.Submitted) => true,
            (SimulationOrderState.Submitted, SimulationOrderState.Acknowledged or
                SimulationOrderState.Rejected or SimulationOrderState.Unknown) => true,
            (SimulationOrderState.Acknowledged, SimulationOrderState.PartiallyFilled or
                SimulationOrderState.Filled or SimulationOrderState.Rejected or
                SimulationOrderState.Unknown) => true,
            (SimulationOrderState.PartiallyFilled, SimulationOrderState.Filled or
                SimulationOrderState.Unknown) => true,
            (SimulationOrderState.Unknown, SimulationOrderState.Acknowledged or
                SimulationOrderState.Filled or SimulationOrderState.Rejected) => reconciliationEvidence,
            _ => false
        };
}
