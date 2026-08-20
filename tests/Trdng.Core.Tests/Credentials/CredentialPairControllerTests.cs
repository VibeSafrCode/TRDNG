using System.Text;
using Trdng.Core.Credentials;
using Trdng.Core.Instruments;

namespace Trdng.Core.Tests.Credentials;

public sealed class CredentialPairControllerTests
{
    private static readonly CredentialIdentity ReadKey = new(TradingVenue.Mexc, "readonly-api-key");
    private static readonly CredentialIdentity ReadSecret = new(TradingVenue.Mexc, "readonly-secret");
    private static readonly CredentialIdentity TestKey = new(TradingVenue.Mexc, "order-test-api-key");
    private static readonly CredentialIdentity TestSecret = new(TradingVenue.Mexc, "order-test-secret");

    [Fact]
    public async Task SavesPairClearsFieldsAndReportsStoredOnlyWhenBothExist()
    {
        var vault = new RecordingVault();
        var input = Input();
        var result = await Controller(vault).SaveAsync(input, false);

        Assert.Equal(CredentialPairAction.Stored, result.Action);
        Assert.Empty(input.ApiKey);
        Assert.Empty(input.Secret);
        Assert.Equal([ReadKey, ReadSecret], vault.StoredIdentities);
    }

    [Fact]
    public async Task FirstStoreFailureDoesNotAttemptSecondStore()
    {
        var vault = new RecordingVault { FailStoreAt = 1 };
        var result = await Controller(vault).SaveAsync(Input(), false);
        Assert.Equal(CredentialPairAction.Error, result.Action);
        Assert.Equal([ReadKey], vault.StoredIdentities);
    }

    [Fact]
    public async Task SecondStoreFailureRollsBackPair()
    {
        var vault = new RecordingVault { FailStoreAt = 2 };
        Assert.Equal(CredentialPairAction.Error,
            (await Controller(vault).SaveAsync(Input(), false)).Action);
        Assert.Equal(CredentialVaultState.NotConfigured,
            (await vault.GetStatusAsync(ReadKey)).State);
        Assert.Equal(CredentialVaultState.NotConfigured,
            (await vault.GetStatusAsync(ReadSecret)).State);
    }

    [Fact]
    public async Task ExistingPairRequiresExplicitReplaceAndProfilesStayIsolated()
    {
        var vault = new RecordingVault();
        await Controller(vault).SaveAsync(Input(), false);
        vault.StoredIdentities.Clear();

        Assert.Equal(CredentialPairAction.ReplaceConfirmationRequired,
            (await Controller(vault).SaveAsync(Input(), false)).Action);
        Assert.Empty(vault.StoredIdentities);
        Assert.Equal(CredentialPairAction.Stored,
            (await Controller(vault).SaveAsync(Input(), true)).Action);
        Assert.All(vault.TouchedIdentities, identity =>
            Assert.DoesNotContain(identity, new[] { TestKey, TestSecret }));
    }

    [Fact]
    public async Task MixedPairIsErrorAndNeverStored()
    {
        var vault = new RecordingVault();
        await vault.StoreAsync(ReadKey, new byte[] { 1 }, false);
        Assert.Equal(CredentialPairAction.Error,
            (await Controller(vault).RefreshAsync()).Action);
    }

    [Fact]
    public async Task StopIsRequiredForSaveAndTwoStepRevoke()
    {
        var stop = false;
        var vault = new RecordingVault();
        var controller = Controller(vault, () => stop);
        Assert.Equal(CredentialPairAction.StopRequired,
            (await controller.SaveAsync(Input(), false)).Action);
        Assert.Empty(vault.StoredIdentities);

        stop = true;
        await controller.SaveAsync(Input(), false);
        Assert.True((await controller.ArmRevokeAsync()).CanConfirmRevoke);
        stop = false;
        Assert.Equal(CredentialPairAction.StopRequired,
            (await controller.ConfirmRevokeAsync()).Action);
        Assert.Equal(CredentialPairAction.Stored,
            (await controller.RefreshAsync()).Action);
    }

    [Fact]
    public async Task StopDisengagedBySecondStoreRollsBackBothItems()
    {
        var stop = true;
        var vault = new RecordingVault { AfterStore = call => { if (call == 2) stop = false; } };
        var controller = Controller(vault, () => stop);

        var result = await controller.SaveAsync(Input(), false);

        Assert.Equal(CredentialPairAction.StopRequired, result.Action);
        Assert.Equal(CredentialPairAction.NotConfigured,
            (await controller.RefreshAsync()).Action);
        Assert.Equal(CredentialVaultState.NotConfigured,
            (await vault.GetStatusAsync(ReadKey)).State);
        Assert.Equal(CredentialVaultState.NotConfigured,
            (await vault.GetStatusAsync(ReadSecret)).State);
    }

    [Fact]
    public async Task StoredPresentationTracksTrustedStopState()
    {
        var stop = true;
        var vault = new RecordingVault();
        var controller = Controller(vault, () => stop);
        await controller.SaveAsync(Input(), false);
        Assert.True((await controller.RefreshAsync()).CanArmRevoke);

        stop = false;
        var stopped = await controller.RefreshAsync();
        Assert.Equal(CredentialPairAction.Stored, stopped.Action);
        Assert.False(stopped.CanArmRevoke);

        stop = true;
        Assert.True((await controller.RefreshAsync()).CanArmRevoke);
    }

    [Fact]
    public async Task AuditedPairFlowDoesNotRetainSecretOrFailureReason()
    {
        const string sentinelReason = "SENTINEL-FAILURE-REASON";
        const string sentinelSecret = "SENTINEL-SECRET-VALUE";
        var inner = new RecordingVault { FailureReason = sentinelReason, FailStoreAt = 2 };
        var audit = new CredentialAudit(32);
        var controller = Controller(new AuditedCredentialVault(inner, audit));
        var input = new CredentialPairInput { ApiKey = "synthetic-key", Secret = sentinelSecret };

        await controller.SaveAsync(input, false);
        var text = string.Join('|', audit.Events.Select(item => item.ToString()));
        Assert.DoesNotContain(sentinelReason, text);
        Assert.DoesNotContain(sentinelSecret, text);
        Assert.Empty(input.ApiKey);
        Assert.Empty(input.Secret);
    }

    private static CredentialPairInput Input() =>
        new() { ApiKey = "synthetic-api-key", Secret = "synthetic-secret" };

    private static CredentialPairController Controller(ICredentialVault vault,
        Func<bool>? stop = null) => new(vault, ReadKey, ReadSecret, stop ?? (() => true),
        TimeSpan.FromSeconds(5), () => DateTimeOffset.UnixEpoch);

    private sealed class RecordingVault : ICredentialVault
    {
        private readonly Dictionary<CredentialIdentity, byte[]> _values = [];
        private int _storeCalls;
        public int? FailStoreAt { get; init; }
        public Action<int>? AfterStore { get; init; }
        public string FailureReason { get; init; } = "FAIL";
        public List<CredentialIdentity> StoredIdentities { get; } = [];
        public List<CredentialIdentity> TouchedIdentities { get; } = [];

        public ValueTask<CredentialVaultResult> StoreAsync(CredentialIdentity identity,
            ReadOnlyMemory<byte> secret, bool overwrite, CancellationToken cancellationToken = default)
        {
            TouchedIdentities.Add(identity);
            StoredIdentities.Add(identity);
            _storeCalls++;
            if (FailStoreAt == _storeCalls)
                return ValueTask.FromResult(new CredentialVaultResult(CredentialVaultState.Error, FailureReason));
            if (!overwrite && _values.ContainsKey(identity))
                return ValueTask.FromResult(new CredentialVaultResult(CredentialVaultState.Denied, "EXISTS"));
            _values[identity] = secret.ToArray();
            AfterStore?.Invoke(_storeCalls);
            return ValueTask.FromResult(new CredentialVaultResult(CredentialVaultState.Stored, "OK"));
        }

        public ValueTask<CredentialReadResult> ReadAsync(CredentialIdentity identity,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
            _values.TryGetValue(identity, out var value)
                ? new CredentialReadResult(CredentialVaultState.Stored, new SecretLease(value.ToArray()), "OK")
                : new CredentialReadResult(CredentialVaultState.NotConfigured, null, "MISSING"));

        public ValueTask<CredentialVaultResult> RevokeAsync(CredentialIdentity identity,
            CancellationToken cancellationToken = default)
        {
            TouchedIdentities.Add(identity);
            _values.Remove(identity);
            return ValueTask.FromResult(new CredentialVaultResult(CredentialVaultState.NotConfigured, "REMOVED"));
        }

        public ValueTask<CredentialVaultResult> GetStatusAsync(CredentialIdentity identity,
            CancellationToken cancellationToken = default)
        {
            TouchedIdentities.Add(identity);
            return ValueTask.FromResult(new CredentialVaultResult(
                _values.ContainsKey(identity) ? CredentialVaultState.Stored : CredentialVaultState.NotConfigured,
                "MASKED"));
        }
    }
}
