using Trdng.Core.Instruments;

namespace Trdng.Core.Credentials;

public enum CredentialAuditAction { Store, Read, Revoke, Status }

public sealed record CredentialAuditEvent(
    DateTimeOffset Timestamp,
    CredentialAuditAction Action,
    TradingVenue Venue,
    string Profile,
    CredentialVaultState State,
    CredentialFailureCode Code);

public sealed class CredentialAudit
{
    private readonly int _capacity;
    private readonly Queue<CredentialAuditEvent> _events = new();
    private readonly Func<DateTimeOffset> _clock;
    public CredentialAudit(int capacity, Func<DateTimeOffset>? clock = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }
    public IReadOnlyList<CredentialAuditEvent> Events => _events.ToArray();
    public void Add(CredentialAuditAction action, CredentialIdentity identity,
        CredentialVaultResult result)
    {
        while (_events.Count >= _capacity) _events.Dequeue();
        _events.Enqueue(new(_clock(), action, identity.Venue, identity.Profile,
            result.State, result.FailureCode));
    }
}

public sealed class AuditedCredentialVault : ICredentialVault
{
    private readonly ICredentialVault _inner;
    private readonly CredentialAudit _audit;

    public AuditedCredentialVault(ICredentialVault inner, CredentialAudit audit)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public async ValueTask<CredentialVaultResult> StoreAsync(CredentialIdentity identity,
        ReadOnlyMemory<byte> secret, bool overwrite,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _inner.StoreAsync(identity, secret, overwrite, cancellationToken);
            Record(CredentialAuditAction.Store, identity, result.State, result.FailureCode);
            return result;
        }
        catch
        {
            Record(CredentialAuditAction.Store, identity, CredentialVaultState.Error,
                CredentialFailureCode.UnexpectedBoundaryFailure);
            throw;
        }
    }

    public async ValueTask<CredentialReadResult> ReadAsync(CredentialIdentity identity,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _inner.ReadAsync(identity, cancellationToken);
            Record(CredentialAuditAction.Read, identity, result.State, result.FailureCode);
            return result;
        }
        catch
        {
            Record(CredentialAuditAction.Read, identity, CredentialVaultState.Error,
                CredentialFailureCode.UnexpectedBoundaryFailure);
            throw;
        }
    }

    public async ValueTask<CredentialVaultResult> RevokeAsync(CredentialIdentity identity,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _inner.RevokeAsync(identity, cancellationToken);
            Record(CredentialAuditAction.Revoke, identity, result.State, result.FailureCode);
            return result;
        }
        catch
        {
            Record(CredentialAuditAction.Revoke, identity, CredentialVaultState.Error,
                CredentialFailureCode.UnexpectedBoundaryFailure);
            throw;
        }
    }

    public async ValueTask<CredentialVaultResult> GetStatusAsync(CredentialIdentity identity,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _inner.GetStatusAsync(identity, cancellationToken);
            Record(CredentialAuditAction.Status, identity, result.State, result.FailureCode);
            return result;
        }
        catch
        {
            Record(CredentialAuditAction.Status, identity, CredentialVaultState.Error,
                CredentialFailureCode.UnexpectedBoundaryFailure);
            throw;
        }
    }

    private void Record(CredentialAuditAction action, CredentialIdentity identity,
        CredentialVaultState state, CredentialFailureCode code)
    {
        if (!CredentialIdentityPolicy.IsValid(identity)) return;
        _audit.Add(action, identity, new CredentialVaultResult(state, string.Empty, code));
    }
}
