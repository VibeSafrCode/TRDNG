using System.Security.Cryptography;
using Trdng.Core.Instruments;

namespace Trdng.Core.Credentials;

public readonly record struct CredentialIdentity(TradingVenue Venue, string Profile)
{
    public string Account => $"{Venue.ToString().ToLowerInvariant()}:{Profile}";
}

public static class CredentialIdentityPolicy
{
    public static bool IsValid(CredentialIdentity identity) =>
        Enum.IsDefined(identity.Venue) &&
        !string.IsNullOrWhiteSpace(identity.Profile) &&
        identity.Profile.Length <= 64 &&
        identity.Profile.All(character => char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_');
}

public enum CredentialVaultState
{
    NotConfigured, Stored, Unavailable, Denied, Error
}

public enum CredentialFailureCode
{
    None,
    InvalidIdentity,
    UnsupportedPlatform,
    NativeLibraryFailure,
    NativeSymbolFailure,
    CoreFoundationStringFailure,
    CoreFoundationDataFailure,
    CoreFoundationDictionaryFailure,
    MissingEntitlement,
    InteractionNotAllowed,
    InvalidParameter,
    AuthenticationFailed,
    DuplicateItem,
    ItemNotFound,
    OtherOsStatus,
    UnexpectedBoundaryFailure
}

public enum CredentialDiagnosticStage
{
    None, NativeLibrary, NativeSymbols, CoreFoundationString,
    CoreFoundationData, CoreFoundationDictionary, SecItem
}

public sealed record CredentialVaultResult(
    CredentialVaultState State,
    string Reason,
    CredentialFailureCode FailureCode = CredentialFailureCode.None,
    CredentialDiagnosticStage DiagnosticStage = CredentialDiagnosticStage.None);

public sealed class SecretLease : IDisposable
{
    private byte[]? _bytes;
    public SecretLease(byte[] bytes) => _bytes = bytes;
    public ReadOnlySpan<byte> Bytes => _bytes ?? throw new ObjectDisposedException(nameof(SecretLease));
    public void Dispose()
    {
        if (_bytes is null) return;
        CryptographicOperations.ZeroMemory(_bytes);
        _bytes = null;
    }
}

public sealed record CredentialReadResult(
    CredentialVaultState State,
    SecretLease? Secret,
    string Reason,
    CredentialFailureCode FailureCode = CredentialFailureCode.None,
    CredentialDiagnosticStage DiagnosticStage = CredentialDiagnosticStage.None) : IDisposable
{
    public void Dispose() => Secret?.Dispose();
}

public interface ICredentialVault
{
    ValueTask<CredentialVaultResult> StoreAsync(
        CredentialIdentity identity,
        ReadOnlyMemory<byte> secret,
        bool overwrite,
        CancellationToken cancellationToken = default);
    ValueTask<CredentialReadResult> ReadAsync(
        CredentialIdentity identity,
        CancellationToken cancellationToken = default);
    ValueTask<CredentialVaultResult> RevokeAsync(
        CredentialIdentity identity,
        CancellationToken cancellationToken = default);
    ValueTask<CredentialVaultResult> GetStatusAsync(
        CredentialIdentity identity,
        CancellationToken cancellationToken = default);
}

public static class CredentialStatusPresentation
{
    public static string ToMaskedText(CredentialVaultState state) => state switch
    {
        CredentialVaultState.Stored => "СОХРАНЕНО В KEYCHAIN",
        CredentialVaultState.Unavailable => "НЕДОСТУПНО",
        CredentialVaultState.Denied => "ДОСТУП ЗАПРЕЩЁН",
        CredentialVaultState.Error => "ОШИБКА KEYCHAIN",
        _ => "НЕ НАСТРОЕН"
    };
}

public sealed class UnavailableCredentialVault : ICredentialVault
{
    private static readonly CredentialVaultResult Unavailable =
        new(CredentialVaultState.Unavailable, "CREDENTIAL VAULT IS UNAVAILABLE");
    public ValueTask<CredentialVaultResult> StoreAsync(CredentialIdentity identity,
        ReadOnlyMemory<byte> secret, bool overwrite, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Unavailable);
    public ValueTask<CredentialReadResult> ReadAsync(CredentialIdentity identity,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(
        new CredentialReadResult(Unavailable.State, null, Unavailable.Reason));
    public ValueTask<CredentialVaultResult> RevokeAsync(CredentialIdentity identity,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(Unavailable);
    public ValueTask<CredentialVaultResult> GetStatusAsync(CredentialIdentity identity,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(Unavailable);
}
