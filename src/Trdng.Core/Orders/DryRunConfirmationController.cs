namespace Trdng.Core.Orders;

public enum PrepareStatus { Prepared, Rejected }
public enum ConfirmationStatus { Confirmed, Rejected }

public sealed record PreparedDryRun(
    string Token,
    MarketOrderIntent Intent,
    RiskDecision RiskDecision,
    DateTimeOffset ExpiresAt);

public sealed record PrepareResult(PrepareStatus Status, string Reason, PreparedDryRun? Candidate);
public sealed record ConfirmationResult(ConfirmationStatus Status, string Reason)
{
    public MarketOrderIntent? ConfirmedIntent { get; internal init; }
    public RiskDecision? ConfirmedRiskDecision { get; internal init; }
}

public sealed class DryRunConfirmationController
{
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<string> _tokenGenerator;
    private readonly TimeSpan _ttl;
    private readonly DryRunAuditTrail _audit;
    private PreparedDryRun? _prepared;
    private bool _used;

    public DryRunConfirmationController(
        TimeSpan ttl,
        DryRunAuditTrail audit,
        Func<DateTimeOffset>? clock = null,
        Func<string>? tokenGenerator = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        _ttl = ttl;
        _audit = audit;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _tokenGenerator = tokenGenerator ?? (() => Guid.NewGuid().ToString("N"));
    }

    public bool KillSwitchEngaged { get; private set; } = true;
    public PreparedDryRun? Prepared => _prepared;

    public bool DisengageForSimulation(RiskProfile profile)
    {
        if (profile.Mode != RiskProfileMode.Simulation || !profile.IsConfigured) return false;
        KillSwitchEngaged = false;
        Audit(DryRunAuditAction.KillSwitchDisengaged, null, "SIMULATION ONLY");
        return true;
    }

    public void EngageKillSwitch(string reason = "LOCAL STOP")
    {
        KillSwitchEngaged = true;
        Invalidate();
        Audit(DryRunAuditAction.KillSwitchEngaged, null, reason);
    }

    public void InvalidateConfirmation(string reason = "INPUT CHANGED")
    {
        if (_prepared is not null) Audit(DryRunAuditAction.Reject, _prepared.Intent, reason);
        Invalidate();
    }

    public PrepareResult Prepare(
        MarketOrderIntent intent,
        OrderValidationResult filterValidation,
        RiskProfile? profile,
        ReferencePrice? referencePrice)
    {
        var now = _clock();
        Audit(DryRunAuditAction.Prepare, intent, "PREPARE REQUESTED");
        if (KillSwitchEngaged) return RejectPrepare(intent, "KILL SWITCH ENGAGED");
        var decision = DryRunRiskPolicy.Evaluate(intent, filterValidation, profile, referencePrice, now);
        if (!decision.IsAllowed) return RejectPrepare(intent, decision.Reason);
        _prepared = new(_tokenGenerator(), intent, decision, now + _ttl);
        _used = false;
        Audit(DryRunAuditAction.Allow, intent, decision.Reason);
        return new(PrepareStatus.Prepared, decision.Reason, _prepared);
    }

    public ConfirmationResult Confirm(string token, MarketOrderIntent exactIntent)
    {
        var now = _clock();
        if (KillSwitchEngaged) return RejectConfirm(exactIntent, "KILL SWITCH ENGAGED");
        if (_prepared is null || _used) return RejectConfirm(exactIntent, "NO UNUSED PREPARATION");
        if (now >= _prepared.ExpiresAt)
        {
            Invalidate();
            return RejectConfirm(exactIntent, "CONFIRMATION EXPIRED");
        }
        if (!string.Equals(token, _prepared.Token, StringComparison.Ordinal) ||
            exactIntent != _prepared.Intent)
        {
            Invalidate();
            return RejectConfirm(exactIntent, "INTENT OR TOKEN CHANGED");
        }
        _used = true;
        Audit(DryRunAuditAction.Confirm, exactIntent, "SIMULATION CONFIRMED");
        return new(ConfirmationStatus.Confirmed, "SIMULATION CONFIRMED · NOT SENT")
        {
            ConfirmedIntent = exactIntent,
            ConfirmedRiskDecision = _prepared.RiskDecision
        };
    }

    private PrepareResult RejectPrepare(MarketOrderIntent intent, string reason)
    {
        Invalidate();
        Audit(DryRunAuditAction.Block, intent, reason);
        return new(PrepareStatus.Rejected, reason, null);
    }

    private ConfirmationResult RejectConfirm(MarketOrderIntent intent, string reason)
    {
        Audit(DryRunAuditAction.Reject, intent, reason);
        return new(ConfirmationStatus.Rejected, reason);
    }

    private void Invalidate()
    {
        _prepared = null;
        _used = false;
    }

    private void Audit(DryRunAuditAction action, MarketOrderIntent? intent, string reason)
    {
        var value = intent ?? _prepared?.Intent;
        _audit.Add(new(_clock(), action, value?.ClientOrderId ?? string.Empty,
            value?.Venue, value?.Instrument.Product,
            value?.Side, value?.SizingMode,
            value?.SizingValue, reason));
    }
}
