using Trdng.Core.Credentials;
using Trdng.Core.Instruments;

namespace Trdng.Mexc.Private;

public sealed class MexcCredentialLease(SecretLease apiKey, SecretLease secret) : IDisposable
{
    public ReadOnlySpan<byte> ApiKey => apiKey.Bytes;
    public ReadOnlySpan<byte> Secret => secret.Bytes;
    public void Dispose() { secret.Dispose(); apiKey.Dispose(); }
}

public sealed record MexcCredentialResult(MexcPrivateState State, MexcCredentialLease? Lease);

public sealed class MexcCredentialProvider(ICredentialVault vault)
{
    public static readonly CredentialIdentity ApiKeyIdentity =
        new(TradingVenue.Mexc, "readonly-api-key");
    public static readonly CredentialIdentity SecretIdentity =
        new(TradingVenue.Mexc, "readonly-secret");

    public async ValueTask<MexcCredentialResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        using var keyRead = await vault.ReadAsync(ApiKeyIdentity, cancellationToken);
        if (keyRead.State != CredentialVaultState.Stored || keyRead.Secret is null)
            return new(Map(keyRead.State), null);
        var keyLease = keyRead.Secret;
        using var secretRead = await vault.ReadAsync(SecretIdentity, cancellationToken);
        if (secretRead.State != CredentialVaultState.Stored || secretRead.Secret is null)
            return new(Map(secretRead.State), null);
        // Transfer independent copies into one bounded lifetime; source leases are cleared by using.
        var keyCopy = new SecretLease(keyLease.Bytes.ToArray());
        var secretCopy = new SecretLease(secretRead.Secret.Bytes.ToArray());
        return new(MexcPrivateState.Ready, new MexcCredentialLease(keyCopy, secretCopy));
    }

    private static MexcPrivateState Map(CredentialVaultState state) => state switch
    {
        CredentialVaultState.Denied => MexcPrivateState.KeychainDenied,
        CredentialVaultState.Unavailable => MexcPrivateState.Unavailable,
        CredentialVaultState.Error => MexcPrivateState.Error,
        _ => MexcPrivateState.NotConfigured
    };
}

public sealed class MexcOrderTestCredentialProvider(ICredentialVault vault)
{
    public static readonly CredentialIdentity ApiKeyIdentity =
        new(TradingVenue.Mexc, "order-test-api-key");
    public static readonly CredentialIdentity SecretIdentity =
        new(TradingVenue.Mexc, "order-test-secret");

    public async ValueTask<MexcCredentialResult> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        using var keyRead = await vault.ReadAsync(ApiKeyIdentity, cancellationToken);
        if (keyRead.State != CredentialVaultState.Stored || keyRead.Secret is null)
            return new(Map(keyRead.State), null);
        using var secretRead = await vault.ReadAsync(SecretIdentity, cancellationToken);
        if (secretRead.State != CredentialVaultState.Stored || secretRead.Secret is null)
            return new(Map(secretRead.State), null);
        return new(MexcPrivateState.Ready, new MexcCredentialLease(
            new SecretLease(keyRead.Secret.Bytes.ToArray()),
            new SecretLease(secretRead.Secret.Bytes.ToArray())));
    }

    private static MexcPrivateState Map(CredentialVaultState state) => state switch
    {
        CredentialVaultState.Denied => MexcPrivateState.KeychainDenied,
        CredentialVaultState.Unavailable => MexcPrivateState.Unavailable,
        CredentialVaultState.Error => MexcPrivateState.Error,
        _ => MexcPrivateState.NotConfigured
    };
}
