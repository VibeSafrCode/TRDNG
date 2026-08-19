namespace Trdng.Core.Orders;

public enum SimulationLifecycleAuditAction
{
    Transition, Replay, Conflict, Reconciliation, RejectedTransition, DuplicateIgnored
}

public sealed record ReconciliationEvidence(
    string ClientOrderId,
    string Fingerprint,
    SimulationOrderState TargetState,
    DateTimeOffset ObservedAt,
    string Source,
    string Reference);

public sealed record SimulationLifecycleAuditEvent(
    DateTimeOffset Timestamp,
    SimulationLifecycleAuditAction Action,
    string ClientOrderId,
    SimulationOrderState? From,
    SimulationOrderState? To,
    string Reason);

public sealed class SimulationOrderStore
{
    private readonly ISimulationJournal _journal;
    private readonly Func<DateTimeOffset> _clock;
    private readonly int _auditCapacity;
    private readonly Dictionary<string, SimulationOrderRecord> _orders = [];
    private readonly Queue<SimulationLifecycleAuditEvent> _audit = new();
    private long _sequence;
    private readonly TimeSpan _reconciliationEvidenceMaxAge;

    public SimulationOrderStore(
        ISimulationJournal journal,
        int auditCapacity,
        Func<DateTimeOffset>? clock = null,
        bool recoverPendingAsUnknown = true,
        TimeSpan? reconciliationEvidenceMaxAge = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(auditCapacity);
        _journal = journal;
        _auditCapacity = auditCapacity;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _reconciliationEvidenceMaxAge = reconciliationEvidenceMaxAge ?? TimeSpan.FromMinutes(5);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            _reconciliationEvidenceMaxAge, TimeSpan.Zero);
        Replay(recoverPendingAsUnknown);
    }

    public IReadOnlyDictionary<string, SimulationOrderRecord> Orders => _orders;
    public IReadOnlyList<SimulationLifecycleAuditEvent> Audit => _audit.ToArray();
    public bool StopEngagedOnStartup { get; } = true;

    public SimulationOrderRecord RegisterConfirmed(MarketOrderIntent intent)
    {
        var fingerprint = IntentFingerprint.Create(intent);
        if (_orders.TryGetValue(intent.ClientOrderId, out var existing))
        {
            if (existing.Fingerprint != fingerprint)
                throw Conflict(intent.ClientOrderId, "CLIENT ORDER ID FINGERPRINT CONFLICT");
            return existing;
        }
        return Create(intent, fingerprint, SimulationOrderState.Confirmed, "SIMULATION CONFIRMED");
    }

    public SimulationOrderRecord Submit(MarketOrderIntent intent)
    {
        var fingerprint = IntentFingerprint.Create(intent);
        if (!_orders.TryGetValue(intent.ClientOrderId, out var existing))
            throw Conflict(intent.ClientOrderId, "EXACT CONFIRMED INTENT REQUIRED");
        if (existing.Fingerprint != fingerprint)
            throw Conflict(intent.ClientOrderId, "CLIENT ORDER ID FINGERPRINT CONFLICT");
        if (existing.State != SimulationOrderState.Confirmed) return existing;
        return Transition(existing, SimulationOrderState.Submitted, "SIMULATED SUBMIT",
            SimulationTransitionKind.Standard);
    }

    public SimulationOrderRecord ApplyCallback(
        string clientOrderId,
        SimulationOrderState state,
        string reason)
    {
        if (!_orders.TryGetValue(clientOrderId, out var existing))
            throw Conflict(clientOrderId, "UNKNOWN CLIENT ORDER ID");
        if (existing.State == state)
        {
            AddAudit(SimulationLifecycleAuditAction.DuplicateIgnored,
                clientOrderId, existing.State, state, reason);
            return existing;
        }
        if (!SimulationStateMachine.CanTransition(existing.State, state))
        {
            AddAudit(SimulationLifecycleAuditAction.RejectedTransition,
                clientOrderId, existing.State, state, reason);
            return existing;
        }
        return Transition(existing, state, reason, SimulationTransitionKind.Standard);
    }

    public SimulationOrderRecord Reconcile(
        string clientOrderId,
        SimulationOrderState state,
        ReconciliationEvidence? evidence,
        string reason)
    {
        if (!_orders.TryGetValue(clientOrderId, out var existing))
            throw Conflict(clientOrderId, "UNKNOWN CLIENT ORDER ID");
        var now = _clock();
        var validEvidence = evidence is not null &&
            evidence.ClientOrderId == clientOrderId &&
            evidence.Fingerprint == existing.Fingerprint &&
            evidence.TargetState == state &&
            evidence.ObservedAt != default && evidence.ObservedAt <= now &&
            now - evidence.ObservedAt <= _reconciliationEvidenceMaxAge &&
            !string.IsNullOrWhiteSpace(evidence.Source) &&
            !string.IsNullOrWhiteSpace(evidence.Reference);
        if (!SimulationStateMachine.CanTransition(existing.State, state, validEvidence))
        {
            AddAudit(SimulationLifecycleAuditAction.RejectedTransition,
                clientOrderId, existing.State, state, "RECONCILIATION REJECTED");
            return existing;
        }
        AddAudit(SimulationLifecycleAuditAction.Reconciliation,
            clientOrderId, existing.State, state, reason);
        return Transition(existing, state, $"RECONCILIATION · {reason}",
            SimulationTransitionKind.Reconciliation);
    }

    private void Replay(bool recoverPending)
    {
        foreach (var value in _journal.ReadAll())
        {
            if (value.Sequence != _sequence + 1)
                throw new InvalidDataException("INVALID JOURNAL SEQUENCE");
            _sequence = value.Sequence;
            if (!_orders.TryGetValue(value.ClientOrderId, out var existing))
            {
                if (value.Intent is null || value.State != SimulationOrderState.Confirmed ||
                    value.TransitionKind != SimulationTransitionKind.Standard ||
                    IntentFingerprint.Create(value.Intent) != value.Fingerprint)
                    throw new InvalidDataException("INVALID FIRST JOURNAL EVENT");
                _orders.Add(value.ClientOrderId,
                    new(value.Intent, value.Fingerprint, value.State, value.Reason, value.Timestamp));
            }
            else
            {
                if (existing.Fingerprint != value.Fingerprint ||
                    !SimulationStateMachine.CanTransition(existing.State, value.State,
                        value.TransitionKind == SimulationTransitionKind.Reconciliation))
                    throw new InvalidDataException("INVALID COMMITTED JOURNAL TRANSITION");
                _orders[value.ClientOrderId] = existing with
                {
                    State = value.State, Reason = value.Reason, UpdatedAt = value.Timestamp
                };
            }
            AddAudit(SimulationLifecycleAuditAction.Replay,
                value.ClientOrderId, null, value.State, value.Reason);
        }

        if (!recoverPending) return;
        foreach (var pending in _orders.Values
                     .Where(order => order.State is SimulationOrderState.Submitted or
                         SimulationOrderState.Acknowledged or SimulationOrderState.PartiallyFilled)
                     .ToArray())
            Transition(pending, SimulationOrderState.Unknown,
                "RESTART · REQUIRES RECONCILIATION", SimulationTransitionKind.Recovery);
    }

    private SimulationOrderRecord Create(
        MarketOrderIntent intent, string fingerprint,
        SimulationOrderState state, string reason)
    {
        var now = _clock();
        var record = new SimulationOrderRecord(intent, fingerprint, state, reason, now);
        Append(record, includeIntent: true, SimulationTransitionKind.Standard);
        _orders.Add(intent.ClientOrderId, record);
        AddAudit(SimulationLifecycleAuditAction.Transition,
            intent.ClientOrderId, null, state, reason);
        return record;
    }

    private SimulationOrderRecord Transition(
        SimulationOrderRecord existing,
        SimulationOrderState state,
        string reason,
        SimulationTransitionKind transitionKind)
    {
        if (!SimulationStateMachine.CanTransition(existing.State, state,
                transitionKind == SimulationTransitionKind.Reconciliation))
            throw new InvalidOperationException("ILLEGAL SIMULATION TRANSITION");
        var updated = existing with { State = state, Reason = reason, UpdatedAt = _clock() };
        Append(updated, includeIntent: false, transitionKind);
        _orders[existing.Intent.ClientOrderId] = updated;
        AddAudit(SimulationLifecycleAuditAction.Transition,
            existing.Intent.ClientOrderId, existing.State, state, reason);
        return updated;
    }

    private void Append(
        SimulationOrderRecord value,
        bool includeIntent,
        SimulationTransitionKind transitionKind)
    {
        var nextSequence = _sequence + 1;
        _journal.Append(new(nextSequence, value.UpdatedAt, value.Intent.ClientOrderId,
            value.Fingerprint, value.State, transitionKind, value.Reason,
            includeIntent ? value.Intent : null));
        _sequence = nextSequence;
    }

    private InvalidOperationException Conflict(string clientOrderId, string reason)
    {
        AddAudit(SimulationLifecycleAuditAction.Conflict,
            clientOrderId, null, null, reason);
        return new(reason);
    }

    private void AddAudit(
        SimulationLifecycleAuditAction action,
        string clientOrderId,
        SimulationOrderState? from,
        SimulationOrderState? to,
        string reason)
    {
        while (_audit.Count >= _auditCapacity) _audit.Dequeue();
        _audit.Enqueue(new(_clock(), action, clientOrderId, from, to, reason));
    }
}
