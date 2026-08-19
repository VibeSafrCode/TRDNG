namespace Trdng.Core.Orders;

public sealed class SimulationPlaybackCoordinator(SimulationOrderStore store)
{
    private MarketOrderIntent? _activeIntent;

    public bool StopEngaged { get; private set; } = true;
    public bool HasActivePlayback => _activeIntent is not null;
    public IReadOnlyDictionary<string, SimulationOrderRecord> History => store.Orders;

    public void SetStop(bool engaged)
    {
        StopEngaged = engaged;
        if (engaged) _activeIntent = null;
    }

    public void ActivateConfirmed(MarketOrderIntent intent)
    {
        if (StopEngaged) throw new InvalidOperationException("STOP ENGAGED");
        store.RegisterConfirmed(intent);
        _activeIntent = intent;
    }

    public void InvalidateActive() => _activeIntent = null;

    public SimulationOrderRecord Play(SimulationScenario scenario)
    {
        if (StopEngaged) throw new InvalidOperationException("STOP ENGAGED");
        if (_activeIntent is null)
            throw new InvalidOperationException("PREPARE AND CONFIRM AGAIN");
        var result = new DeterministicSimulationAdapter(store).Play(_activeIntent, scenario);
        _activeIntent = null;
        return result;
    }
}
