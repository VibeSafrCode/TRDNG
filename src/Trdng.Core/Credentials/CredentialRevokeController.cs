namespace Trdng.Core.Credentials;

public enum CredentialRevokeStage { Hidden, Ready, Armed, Completed, Blocked }

public sealed record CredentialRevokePresentation(
    CredentialRevokeStage Stage,
    string MaskedMessage,
    bool CanArm,
    bool CanConfirm);

public sealed class CredentialRevokeController
{
    private readonly ICredentialVault _vault;
    private readonly CredentialIdentity _identity;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _ttl;
    private DateTimeOffset? _armedUntil;
    private CredentialVaultState _state = CredentialVaultState.NotConfigured;
    private bool _stopEngaged = true;

    public CredentialRevokeController(ICredentialVault vault, CredentialIdentity identity,
        TimeSpan ttl, Func<DateTimeOffset>? clock = null)
    {
        _vault = vault;
        _identity = identity;
        if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl));
        _ttl = ttl;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public CredentialRevokePresentation Presentation => Present(_stopEngaged);

    public void UpdateState(CredentialVaultState state, bool stopEngaged)
    {
        if (state != _state || !stopEngaged) _armedUntil = null;
        _state = state;
        _stopEngaged = stopEngaged;
    }

    public CredentialRevokePresentation Arm(bool stopEngaged)
    {
        _stopEngaged = stopEngaged;
        _armedUntil = _state == CredentialVaultState.Stored && stopEngaged
            ? _clock() + _ttl
            : null;
        return Present(stopEngaged);
    }

    public void Invalidate() => _armedUntil = null;

    public async ValueTask<CredentialRevokePresentation> ConfirmAsync(bool stopEngaged,
        CancellationToken cancellationToken = default)
    {
        _stopEngaged = stopEngaged;
        var now = _clock();
        if (!stopEngaged || _state != CredentialVaultState.Stored ||
            _armedUntil is null || now > _armedUntil)
        {
            _armedUntil = null;
            return Present(stopEngaged);
        }

        _armedUntil = null;
        CredentialVaultResult result;
        try { result = await _vault.RevokeAsync(_identity, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _state = CredentialVaultState.Error;
            return new(CredentialRevokeStage.Blocked, "ОШИБКА KEYCHAIN", false, false);
        }
        _state = result.State;
        return result.State == CredentialVaultState.NotConfigured
            ? new(CredentialRevokeStage.Completed, "УДАЛЕНО ИЗ KEYCHAIN", false, false)
            : Present(stopEngaged);
    }

    private CredentialRevokePresentation Present(bool stopEngaged = true)
    {
        if (_state != CredentialVaultState.Stored)
            return new(CredentialRevokeStage.Hidden,
                CredentialStatusPresentation.ToMaskedText(_state), false, false);
        if (!stopEngaged)
            return new(CredentialRevokeStage.Blocked, "ВКЛЮЧИТЕ STOP ДЛЯ УДАЛЕНИЯ", false, false);
        if (_armedUntil is { } until && _clock() <= until)
            return new(CredentialRevokeStage.Armed, "ПОДТВЕРДИТЕ УДАЛЕНИЕ ИЗ KEYCHAIN", false, true);
        _armedUntil = null;
        return new(CredentialRevokeStage.Ready, "СОХРАНЕНО В KEYCHAIN", true, false);
    }
}
