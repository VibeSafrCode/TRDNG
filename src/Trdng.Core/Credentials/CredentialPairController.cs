using System.Security.Cryptography;
using System.Text;

namespace Trdng.Core.Credentials;

public enum CredentialPairAction
{
    NotConfigured, Stored, Error, StopRequired, IncompleteInput,
    ReplaceConfirmationRequired, RevokeConfirmationRequired
}

public sealed record CredentialPairPresentation(
    CredentialPairAction Action,
    string MaskedMessage,
    bool CanArmRevoke,
    bool CanConfirmRevoke,
    bool CanConfirmReplace);

public sealed class CredentialPairInput
{
    public string ApiKey { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;

    public void Clear()
    {
        ApiKey = string.Empty;
        Secret = string.Empty;
    }
}

/// <summary>Coordinates an inseparable credential pair without exposing secret values.</summary>
public sealed class CredentialPairController
{
    public const int MaxInputBytes = 4096;
    private readonly ICredentialVault _vault;
    private readonly CredentialIdentity _apiKeyIdentity;
    private readonly CredentialIdentity _secretIdentity;
    private readonly Func<bool> _stopEngaged;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _confirmationTtl;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateSync = new();
    private DateTimeOffset? _revokeArmedUntil;

    public CredentialPairController(ICredentialVault vault,
        CredentialIdentity apiKeyIdentity,
        CredentialIdentity secretIdentity,
        Func<bool> stopEngaged,
        TimeSpan confirmationTtl,
        Func<DateTimeOffset>? clock = null)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        if (!CredentialIdentityPolicy.IsValid(apiKeyIdentity) ||
            !CredentialIdentityPolicy.IsValid(secretIdentity) ||
            apiKeyIdentity == secretIdentity)
            throw new ArgumentException("Credential pair identities are invalid.");
        _apiKeyIdentity = apiKeyIdentity;
        _secretIdentity = secretIdentity;
        _stopEngaged = stopEngaged ?? throw new ArgumentNullException(nameof(stopEngaged));
        if (confirmationTtl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(confirmationTtl));
        _confirmationTtl = confirmationTtl;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public void Invalidate()
    {
        lock (_stateSync) _revokeArmedUntil = null;
    }

    public async ValueTask<CredentialPairPresentation> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await RefreshUnsafeAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async ValueTask<CredentialPairPresentation> SaveAsync(
        CredentialPairInput input,
        bool replaceConfirmed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        byte[]? apiKey = null;
        byte[]? secret = null;
        try
        {
            apiKey = Encoding.UTF8.GetBytes(input.ApiKey);
            secret = Encoding.UTF8.GetBytes(input.Secret);
            input.Clear();
            if (apiKey.Length is 0 or > MaxInputBytes || secret.Length is 0 or > MaxInputBytes)
                return Present(CredentialPairAction.IncompleteInput, "ЗАПОЛНИТЕ ОБА ПОЛЯ");
            if (!_stopEngaged())
                return Present(CredentialPairAction.StopRequired, "ВКЛЮЧИТЕ STOP ДЛЯ СОХРАНЕНИЯ");

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            var firstStored = false;
            try
            {
                if (!_stopEngaged())
                    return Present(CredentialPairAction.StopRequired, "ВКЛЮЧИТЕ STOP ДЛЯ СОХРАНЕНИЯ");
                Invalidate();
                var current = await ReadPairStateUnsafeAsync(cancellationToken).ConfigureAwait(false);
                if (current != CredentialPairAction.NotConfigured && !replaceConfirmed)
                    return Present(CredentialPairAction.ReplaceConfirmationRequired,
                        "ПОДТВЕРДИТЕ ЗАМЕНУ ПАРЫ", replace: true);
                if (current != CredentialPairAction.NotConfigured)
                {
                    if (!await RevokeBothUnsafeAsync(cancellationToken).ConfigureAwait(false))
                        return Present(CredentialPairAction.Error, "ОШИБКА KEYCHAIN");
                }

                var first = await _vault.StoreAsync(_apiKeyIdentity, apiKey,
                    overwrite: false, cancellationToken).ConfigureAwait(false);
                if (first.State != CredentialVaultState.Stored)
                    return Present(CredentialPairAction.Error, "ОШИБКА KEYCHAIN");
                firstStored = true;
                if (!_stopEngaged())
                {
                    await RevokeBothBestEffortUnsafeAsync(CancellationToken.None).ConfigureAwait(false);
                    return Present(CredentialPairAction.StopRequired, "СОХРАНЕНИЕ ОТМЕНЕНО · STOP");
                }

                var second = await _vault.StoreAsync(_secretIdentity, secret,
                    overwrite: false, cancellationToken).ConfigureAwait(false);
                if (second.State != CredentialVaultState.Stored)
                {
                    await RevokeBothBestEffortUnsafeAsync(CancellationToken.None).ConfigureAwait(false);
                    return Present(CredentialPairAction.Error, "ОШИБКА KEYCHAIN");
                }
                if (!_stopEngaged())
                {
                    await RevokeBothBestEffortUnsafeAsync(CancellationToken.None).ConfigureAwait(false);
                    return Present(CredentialPairAction.StopRequired, "СОХРАНЕНИЕ ОТМЕНЕНО · STOP");
                }

                return await ReadPairStateUnsafeAsync(cancellationToken).ConfigureAwait(false) ==
                    CredentialPairAction.Stored
                    ? Present(CredentialPairAction.Stored, "СОХРАНЕНО В KEYCHAIN", revoke: true)
                    : await CleanupErrorUnsafeAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (firstStored)
                    await RevokeBothBestEffortUnsafeAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (firstStored)
                    await RevokeBothBestEffortUnsafeAsync(CancellationToken.None).ConfigureAwait(false);
                return Present(CredentialPairAction.Error, "ОШИБКА KEYCHAIN");
            }
            finally { _gate.Release(); }
        }
        finally
        {
            input.Clear();
            if (apiKey is not null) CryptographicOperations.ZeroMemory(apiKey);
            if (secret is not null) CryptographicOperations.ZeroMemory(secret);
        }
    }

    public async ValueTask<CredentialPairPresentation> ArmRevokeAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_stopEngaged())
                return Present(CredentialPairAction.StopRequired, "ВКЛЮЧИТЕ STOP ДЛЯ УДАЛЕНИЯ");
            if (await ReadPairStateUnsafeAsync(cancellationToken).ConfigureAwait(false) !=
                CredentialPairAction.Stored)
                return Present(CredentialPairAction.NotConfigured, "НЕ НАСТРОЕН");
            lock (_stateSync) _revokeArmedUntil = checked(_clock() + _confirmationTtl);
            return Present(CredentialPairAction.RevokeConfirmationRequired,
                "ПОДТВЕРДИТЕ УДАЛЕНИЕ ПАРЫ", confirmRevoke: true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Invalidate();
            return Present(CredentialPairAction.Error, "ОШИБКА KEYCHAIN");
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<CredentialPairPresentation> ConfirmRevokeAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset? armedUntil;
            lock (_stateSync)
            {
                armedUntil = _revokeArmedUntil;
                _revokeArmedUntil = null;
            }
            if (!_stopEngaged() || armedUntil is null || _clock() > armedUntil)
                return Present(CredentialPairAction.StopRequired, "УДАЛЕНИЕ НЕ ПОДТВЕРЖДЕНО");
            return await RevokeBothUnsafeAsync(cancellationToken).ConfigureAwait(false)
                ? Present(CredentialPairAction.NotConfigured, "УДАЛЕНО ИЗ KEYCHAIN")
                : Present(CredentialPairAction.Error, "ОШИБКА KEYCHAIN");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Present(CredentialPairAction.Error, "ОШИБКА KEYCHAIN");
        }
        finally { _gate.Release(); }
    }

    private async ValueTask<CredentialPairPresentation> RefreshUnsafeAsync(
        CancellationToken cancellationToken)
    {
        var state = await ReadPairStateUnsafeAsync(cancellationToken).ConfigureAwait(false);
        return state switch
        {
            CredentialPairAction.Stored => Present(state, "СОХРАНЕНО В KEYCHAIN", revoke: _stopEngaged()),
            CredentialPairAction.NotConfigured => Present(state, "НЕ НАСТРОЕН"),
            _ => Present(CredentialPairAction.Error, "ОШИБКА KEYCHAIN")
        };
    }

    private async ValueTask<CredentialPairAction> ReadPairStateUnsafeAsync(
        CancellationToken cancellationToken)
    {
        var key = await _vault.GetStatusAsync(_apiKeyIdentity, cancellationToken).ConfigureAwait(false);
        var secret = await _vault.GetStatusAsync(_secretIdentity, cancellationToken).ConfigureAwait(false);
        if (key.State == CredentialVaultState.Stored && secret.State == CredentialVaultState.Stored)
            return CredentialPairAction.Stored;
        if (key.State == CredentialVaultState.NotConfigured &&
            secret.State == CredentialVaultState.NotConfigured)
            return CredentialPairAction.NotConfigured;
        return CredentialPairAction.Error;
    }

    private async ValueTask<bool> RevokeBothUnsafeAsync(CancellationToken cancellationToken)
    {
        var key = await _vault.RevokeAsync(_apiKeyIdentity, cancellationToken).ConfigureAwait(false);
        var secret = await _vault.RevokeAsync(_secretIdentity, cancellationToken).ConfigureAwait(false);
        return key.State == CredentialVaultState.NotConfigured &&
            secret.State == CredentialVaultState.NotConfigured;
    }

    private async ValueTask RevokeBothBestEffortUnsafeAsync(CancellationToken cancellationToken)
    {
        try { await _vault.RevokeAsync(_apiKeyIdentity, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException) { }
        try { await _vault.RevokeAsync(_secretIdentity, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException) { }
    }

    private async ValueTask<CredentialPairPresentation> CleanupErrorUnsafeAsync(
        CancellationToken cancellationToken)
    {
        await RevokeBothBestEffortUnsafeAsync(cancellationToken).ConfigureAwait(false);
        return Present(CredentialPairAction.Error, "ОШИБКА KEYCHAIN");
    }

    private static CredentialPairPresentation Present(CredentialPairAction action, string message,
        bool revoke = false, bool confirmRevoke = false, bool replace = false) =>
        new(action, message, revoke, confirmRevoke, replace);
}
