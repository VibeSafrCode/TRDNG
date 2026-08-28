using System.Security.Cryptography;
using Trdng.Core.Credentials;
using Trdng.Core.Instruments;

namespace Trdng.Core.Tests.Credentials;

public sealed class CredentialVaultTests
{
    private static readonly CredentialIdentity Identity =
        new(TradingVenue.Mexc, "synthetic-test");

    [Fact]
    public async Task FakeContractSupportsOverwriteReadStatusAndIdempotentRevoke()
    {
        var vault = new FakeCredentialVault();
        var first = new byte[] { 1, 2, 3 };
        var second = new byte[] { 4, 5, 6 };
        Assert.Equal(CredentialVaultState.Stored,
            (await vault.StoreAsync(Identity, first, overwrite: false)).State);
        Assert.Equal(CredentialVaultState.Denied,
            (await vault.StoreAsync(Identity, second, overwrite: false)).State);
        Assert.Equal(CredentialVaultState.Stored,
            (await vault.StoreAsync(Identity, second, overwrite: true)).State);
        Assert.Equal(CredentialVaultState.Stored,
            (await vault.GetStatusAsync(Identity)).State);
        using (var read = await vault.ReadAsync(Identity))
            Assert.Equal(second, read.Secret!.Bytes.ToArray());
        Assert.Equal(CredentialVaultState.NotConfigured,
            (await vault.RevokeAsync(Identity)).State);
        Assert.Equal(CredentialVaultState.NotConfigured,
            (await vault.RevokeAsync(Identity)).State);
    }

    [Fact]
    public void MaskedProjectionContainsNoCredentialMaterial()
    {
        foreach (var state in Enum.GetValues<CredentialVaultState>())
        {
            var text = CredentialStatusPresentation.ToMaskedText(state);
            Assert.DoesNotContain("secret", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("api-key", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void CredentialAuditIsBoundedAndHasNoSecretKeyOrTokenFields()
    {
        var audit = new CredentialAudit(2, () => DateTimeOffset.UnixEpoch);
        var result = new CredentialVaultResult(CredentialVaultState.Stored, "STORE OK");
        audit.Add(CredentialAuditAction.Store, Identity, result);
        audit.Add(CredentialAuditAction.Status, Identity, result);
        audit.Add(CredentialAuditAction.Revoke, Identity, result);
        Assert.Equal(2, audit.Events.Count);
        var names = typeof(CredentialAuditEvent).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain(names, name =>
            name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Key", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        var sentinel = "SECRET-SENTINEL-DO-NOT-STORE";
        audit.Add(CredentialAuditAction.Read, Identity,
            new CredentialVaultResult(CredentialVaultState.Error, sentinel));
        Assert.DoesNotContain(sentinel, string.Join('|', audit.Events.Select(e => e.ToString())));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-25300)]
    public void RevokeSuccessAndMissingMapToNotConfigured(int osStatus) =>
        Assert.Equal(CredentialVaultState.NotConfigured,
            MacOsKeychainCredentialVault.MapRevokeStatus(osStatus).State);

    [Theory]
    [InlineData(-34018, CredentialFailureCode.MissingEntitlement)]
    [InlineData(-25308, CredentialFailureCode.InteractionNotAllowed)]
    [InlineData(-25315, CredentialFailureCode.InteractionRequired)]
    [InlineData(-50, CredentialFailureCode.InvalidParameter)]
    [InlineData(-25293, CredentialFailureCode.AuthenticationFailed)]
    [InlineData(-128, CredentialFailureCode.UserCanceled)]
    [InlineData(-25291, CredentialFailureCode.KeychainUnavailable)]
    [InlineData(-25294, CredentialFailureCode.KeychainNotFound)]
    [InlineData(-26275, CredentialFailureCode.DecodeFailure)]
    [InlineData(-777777, CredentialFailureCode.OtherOsStatus)]
    public void OsStatusMapsToAllowlistedFailureCodeWithoutRawValue(
        int osStatus, CredentialFailureCode expected)
    {
        var result = MacOsKeychainCredentialVault.MapRevokeStatus(osStatus);
        Assert.Equal(expected, result.FailureCode);
        Assert.DoesNotContain(osStatus.ToString(), result.Reason, StringComparison.Ordinal);
        Assert.Equal(CredentialDiagnosticStage.SecItem, result.DiagnosticStage);
    }

    [Fact]
    public async Task RevokeRequiresStoredStopArmAndExactConfirmWithinTtl()
    {
        var now = DateTimeOffset.UnixEpoch;
        var vault = new FakeCredentialVault();
        await vault.StoreAsync(Identity, new byte[] { 1 }, overwrite: true);
        var controller = new CredentialRevokeController(vault, Identity,
            TimeSpan.FromSeconds(5), () => now);
        controller.UpdateState(CredentialVaultState.Stored, stopEngaged: true);

        Assert.False((await controller.ConfirmAsync(stopEngaged: true)).CanConfirm);
        Assert.True(controller.Arm(stopEngaged: true).CanConfirm);
        controller.UpdateState(CredentialVaultState.Stored, stopEngaged: false);
        Assert.Equal(CredentialVaultState.Stored, (await vault.GetStatusAsync(Identity)).State);

        controller.UpdateState(CredentialVaultState.Stored, stopEngaged: true);
        controller.Arm(stopEngaged: true);
        now += TimeSpan.FromSeconds(6);
        await controller.ConfirmAsync(stopEngaged: true);
        Assert.Equal(CredentialVaultState.Stored, (await vault.GetStatusAsync(Identity)).State);

        controller.Arm(stopEngaged: true);
        var completed = await controller.ConfirmAsync(stopEngaged: true);
        Assert.Equal(CredentialRevokeStage.Completed, completed.Stage);
        Assert.Equal(CredentialVaultState.NotConfigured,
            (await vault.GetStatusAsync(Identity)).State);
    }

    [Fact]
    public async Task RevokeStateChangeInvalidatesArmedConfirmation()
    {
        var vault = new FakeCredentialVault();
        await vault.StoreAsync(Identity, new byte[] { 1 }, overwrite: true);
        var controller = new CredentialRevokeController(vault, Identity,
            TimeSpan.FromSeconds(5));
        controller.UpdateState(CredentialVaultState.Stored, stopEngaged: true);
        controller.Arm(stopEngaged: true);
        controller.UpdateState(CredentialVaultState.Error, stopEngaged: true);
        await controller.ConfirmAsync(stopEngaged: true);
        Assert.Equal(CredentialVaultState.Stored, (await vault.GetStatusAsync(Identity)).State);
    }

    [Fact]
    public async Task StoreRejectsEmptyAndOversizedBeforeNativeBoundary()
    {
        var vault = new MacOsKeychainCredentialVault();
        Assert.Equal(CredentialVaultState.Denied,
            (await vault.StoreAsync(Identity, ReadOnlyMemory<byte>.Empty, false)).State);
        Assert.Equal(CredentialVaultState.Denied,
            (await vault.StoreAsync(Identity,
                new byte[MacOsKeychainCredentialVault.MaxSecretBytes + 1], false)).State);
    }

    [Fact]
    public async Task UnsupportedVaultFailsClosed()
    {
        ICredentialVault vault = new UnavailableCredentialVault();
        Assert.Equal(CredentialVaultState.Unavailable,
            (await vault.GetStatusAsync(Identity)).State);
        using var read = await vault.ReadAsync(Identity);
        Assert.Null(read.Secret);
        Assert.Equal(CredentialVaultState.Unavailable, read.State);
    }

    [Fact]
    public void IdentityRejectsUndefinedVenue() => Assert.False(
        CredentialIdentityPolicy.IsValid(new CredentialIdentity(
            (TradingVenue)999, "valid-profile")));

    [Fact]
    public async Task AuditedDecoratorRecordsAllActionsWithoutReasonOrSecret()
    {
        const string sentinel = "SECRET-SENTINEL-REASON";
        var audit = new CredentialAudit(8, () => DateTimeOffset.UnixEpoch);
        var inner = new SentinelCredentialVault(sentinel);
        var vault = new AuditedCredentialVault(inner, audit);
        var secret = new byte[] { 83, 69, 67, 82, 69, 84 };

        await vault.StoreAsync(Identity, secret, overwrite: true);
        using (await vault.ReadAsync(Identity)) { }
        await vault.GetStatusAsync(Identity);
        await vault.RevokeAsync(Identity);

        Assert.Equal(
            [CredentialAuditAction.Store, CredentialAuditAction.Read,
             CredentialAuditAction.Status, CredentialAuditAction.Revoke],
            audit.Events.Select(item => item.Action).ToArray());
        var serialized = string.Join('|', audit.Events.Select(item => item.ToString()));
        Assert.DoesNotContain(sentinel, serialized);
        Assert.DoesNotContain("SECRET", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.All(audit.Events, item => Assert.Equal(Identity, new(
            item.Venue, item.Profile)));
        Assert.All(audit.Events, item => Assert.True(Enum.IsDefined(item.Code)));
    }

    [Fact]
    public void SecretLeaseCannotBeReadAfterDispose()
    {
        var lease = new SecretLease([1, 2, 3]);
        lease.Dispose();
        Assert.Throws<ObjectDisposedException>(() => lease.Bytes.ToArray());
    }

    [Fact]
    public async Task NativeMacKeychainSyntheticCreateReadDeleteWhenExplicitlyEnabled()
    {
        if (!OperatingSystem.IsMacOS() ||
            Environment.GetEnvironmentVariable("TRDNG_RUN_KEYCHAIN_INTEGRATION") != "1")
            return;

        var vault = new MacOsKeychainCredentialVault();
        var identity = new CredentialIdentity(TradingVenue.Mexc,
            $"native-{Guid.NewGuid():N}");
        var secret = RandomNumberGenerator.GetBytes(32);
        try
        {
            Assert.Equal(CredentialVaultState.Stored,
                (await vault.StoreAsync(identity, secret, overwrite: true)).State);
            using var read = await vault.ReadAsync(identity);
            Assert.Equal(CredentialVaultState.Stored, read.State);
            Assert.Equal(secret, read.Secret!.Bytes.ToArray());
        }
        finally
        {
            await vault.RevokeAsync(identity);
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private sealed class FakeCredentialVault : ICredentialVault
    {
        private readonly Dictionary<CredentialIdentity, byte[]> _values = [];
        public ValueTask<CredentialVaultResult> StoreAsync(CredentialIdentity identity,
            ReadOnlyMemory<byte> secret, bool overwrite,
            CancellationToken cancellationToken = default)
        {
            if (_values.TryGetValue(identity, out var previous) && !overwrite)
                return ValueTask.FromResult(new CredentialVaultResult(
                    CredentialVaultState.Denied, "OVERWRITE REQUIRED"));
            if (previous is not null) CryptographicOperations.ZeroMemory(previous);
            _values[identity] = secret.ToArray();
            return ValueTask.FromResult(new CredentialVaultResult(
                CredentialVaultState.Stored, "STORED"));
        }
        public ValueTask<CredentialReadResult> ReadAsync(CredentialIdentity identity,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_values.TryGetValue(identity, out var value)
                ? new CredentialReadResult(CredentialVaultState.Stored,
                    new SecretLease(value.ToArray()), "STORED")
                : new CredentialReadResult(CredentialVaultState.NotConfigured,
                    null, "NOT CONFIGURED"));
        public ValueTask<CredentialVaultResult> RevokeAsync(CredentialIdentity identity,
            CancellationToken cancellationToken = default)
        {
            if (_values.Remove(identity, out var value))
                CryptographicOperations.ZeroMemory(value);
            return ValueTask.FromResult(new CredentialVaultResult(
                CredentialVaultState.NotConfigured, "NOT CONFIGURED"));
        }
        public ValueTask<CredentialVaultResult> GetStatusAsync(CredentialIdentity identity,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                new CredentialVaultResult(_values.ContainsKey(identity)
                    ? CredentialVaultState.Stored
                    : CredentialVaultState.NotConfigured, "MASKED"));
    }

    private sealed class SentinelCredentialVault(string reason) : ICredentialVault
    {
        public ValueTask<CredentialVaultResult> StoreAsync(CredentialIdentity identity,
            ReadOnlyMemory<byte> secret, bool overwrite,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                new CredentialVaultResult(CredentialVaultState.Stored, reason));
        public ValueTask<CredentialReadResult> ReadAsync(CredentialIdentity identity,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                new CredentialReadResult(CredentialVaultState.Stored,
                    new SecretLease([1, 2, 3]), reason));
        public ValueTask<CredentialVaultResult> RevokeAsync(CredentialIdentity identity,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                new CredentialVaultResult(CredentialVaultState.NotConfigured, reason));
        public ValueTask<CredentialVaultResult> GetStatusAsync(CredentialIdentity identity,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                new CredentialVaultResult(CredentialVaultState.Stored, reason));
    }
}
