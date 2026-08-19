using Trdng.Core.Instruments;
using Trdng.Core.Orders;

namespace Trdng.Core.Tests.Orders;

public sealed class SimulationLifecycleTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(SimulationOrderState.Confirmed, SimulationOrderState.Submitted)]
    [InlineData(SimulationOrderState.Submitted, SimulationOrderState.Acknowledged)]
    [InlineData(SimulationOrderState.Acknowledged, SimulationOrderState.PartiallyFilled)]
    [InlineData(SimulationOrderState.PartiallyFilled, SimulationOrderState.Filled)]
    [InlineData(SimulationOrderState.Submitted, SimulationOrderState.Unknown)]
    public void AllowsLegalTransitions(SimulationOrderState from, SimulationOrderState to) =>
        Assert.True(SimulationStateMachine.CanTransition(from, to));

    [Theory]
    [InlineData(SimulationOrderState.Confirmed, SimulationOrderState.Filled)]
    [InlineData(SimulationOrderState.Filled, SimulationOrderState.Submitted)]
    [InlineData(SimulationOrderState.Unknown, SimulationOrderState.Acknowledged)]
    [InlineData(SimulationOrderState.Rejected, SimulationOrderState.Submitted)]
    public void RejectsIllegalTransitions(SimulationOrderState from, SimulationOrderState to) =>
        Assert.False(SimulationStateMachine.CanTransition(from, to));

    [Fact]
    public void SubmitIsIdempotentForSameIntentAndConflictsOnMutation()
    {
        var journal = new InMemorySimulationJournal(32);
        var store = Store(journal);
        var intent = Intent();
        store.RegisterConfirmed(intent);
        var first = store.Submit(intent);
        var count = journal.ReadAll().Count;
        var second = store.Submit(intent);
        Assert.Same(first, second);
        Assert.Equal(count, journal.ReadAll().Count);
        Assert.Throws<InvalidOperationException>(() =>
            store.Submit(intent with { SizingValue = 6 }));
    }

    [Theory]
    [InlineData(SimulationScenario.AcknowledgeAndFill, SimulationOrderState.Filled)]
    [InlineData(SimulationScenario.PartialAndFill, SimulationOrderState.Filled)]
    [InlineData(SimulationScenario.Reject, SimulationOrderState.Rejected)]
    [InlineData(SimulationScenario.TimeoutBeforeAcknowledge, SimulationOrderState.Unknown)]
    [InlineData(SimulationScenario.TimeoutAfterAcknowledge, SimulationOrderState.Unknown)]
    [InlineData(SimulationScenario.DuplicateAndOutOfOrder, SimulationOrderState.Filled)]
    public void DeterministicScenariosEndInExpectedState(
        SimulationScenario scenario, SimulationOrderState expected)
    {
        var store = Store(new InMemorySimulationJournal(64));
        var intent = Intent();
        store.RegisterConfirmed(intent);
        var result = new DeterministicSimulationAdapter(store).Play(intent, scenario);
        Assert.Equal(expected, result.State);
    }

    [Fact]
    public void UnknownNeverRetriesAndRequiresExplicitEvidence()
    {
        var journal = new InMemorySimulationJournal(32);
        var store = Store(journal);
        var intent = Intent();
        store.RegisterConfirmed(intent);
        var adapter = new DeterministicSimulationAdapter(store);
        Assert.Equal(SimulationOrderState.Unknown,
            adapter.Play(intent, SimulationScenario.TimeoutBeforeAcknowledge).State);
        var count = journal.ReadAll().Count;
        Assert.Equal(SimulationOrderState.Unknown, adapter.Play(intent,
            SimulationScenario.AcknowledgeAndFill).State);
        Assert.Equal(count, journal.ReadAll().Count);
        Assert.Equal(SimulationOrderState.Unknown,
            store.Reconcile(intent.ClientOrderId, SimulationOrderState.Filled,
                null, "NO EVIDENCE").State);
        var evidence = new ReconciliationEvidence(
            intent.ClientOrderId, IntentFingerprint.Create(intent),
            SimulationOrderState.Filled, Now, "SIMULATED EXCHANGE", "execution-1");
        Assert.Equal(SimulationOrderState.Filled,
            store.Reconcile(intent.ClientOrderId, SimulationOrderState.Filled,
                evidence, "MATCHED EXECUTION").State);
    }

    [Fact]
    public void ReconciliationRequiresExactFreshStructuredEvidence()
    {
        var store = Store(new InMemorySimulationJournal(32));
        var intent = Intent();
        store.RegisterConfirmed(intent);
        new DeterministicSimulationAdapter(store).Play(
            intent, SimulationScenario.TimeoutBeforeAcknowledge);
        var valid = new ReconciliationEvidence(
            intent.ClientOrderId, IntentFingerprint.Create(intent),
            SimulationOrderState.Filled, Now, "SIMULATED EXCHANGE", "execution-1");
        foreach (var invalid in new ReconciliationEvidence?[]
        {
            null,
            valid with { ClientOrderId = "other" },
            valid with { Fingerprint = "wrong" },
            valid with { TargetState = SimulationOrderState.Rejected },
            valid with { ObservedAt = Now - TimeSpan.FromMinutes(6) },
            valid with { Source = "" },
            valid with { Reference = " " }
        })
            Assert.Equal(SimulationOrderState.Unknown,
                store.Reconcile(intent.ClientOrderId, SimulationOrderState.Filled,
                    invalid, "EVIDENCE CHECK").State);
        Assert.Equal(SimulationOrderState.Filled,
            store.Reconcile(intent.ClientOrderId, SimulationOrderState.Filled,
                valid, "MATCHED").State);
    }

    [Fact]
    public void RestartReplaysOneRecordAndPendingBecomesUnknownWithStopEngaged()
    {
        var journal = new InMemorySimulationJournal(32);
        var first = Store(journal);
        var intent = Intent();
        first.RegisterConfirmed(intent);
        first.Submit(intent);
        var recovered = Store(journal);
        Assert.True(recovered.StopEngagedOnStartup);
        Assert.Single(recovered.Orders);
        Assert.Equal(SimulationOrderState.Unknown,
            recovered.Orders[intent.ClientOrderId].State);
        Assert.Contains("REQUIRES RECONCILIATION",
            recovered.Orders[intent.ClientOrderId].Reason);
    }

    [Fact]
    public void FileJournalToleratesOneTruncatedTailButRejectsCommittedCorruption()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trdng-{Guid.NewGuid():N}.journal");
        try
        {
            var journal = new FileSimulationJournal(path, 16);
            var store = Store(journal);
            store.RegisterConfirmed(Intent());
            File.AppendAllText(path, "truncated-tail");
            Assert.Single(journal.ReadAll());
            File.AppendAllText(path, "\ncorrupt-committed\n");
            Assert.Throws<InvalidDataException>(() => journal.ReadAll());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void AppendRemovesTruncatedTailBeforeWritingNextCommittedEvent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trdng-{Guid.NewGuid():N}.journal");
        try
        {
            var journal = new FileSimulationJournal(path, 16);
            var intent = Intent();
            var first = Store(journal);
            first.RegisterConfirmed(intent);
            File.AppendAllText(path, "partial-tail");
            first.Submit(intent);
            var replayed = new SimulationOrderStore(journal, 16, () => Now,
                recoverPendingAsUnknown: false);
            Assert.Equal(SimulationOrderState.Submitted,
                replayed.Orders[intent.ClientOrderId].State);
            Assert.Equal(2, journal.ReadAll().Count);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void FailedAppendDoesNotMutateStateOrConsumeSequence()
    {
        var journal = new FailOnceJournal();
        var store = Store(journal);
        var intent = Intent();
        Assert.Throws<IOException>(() => store.RegisterConfirmed(intent));
        Assert.Empty(store.Orders);
        store.RegisterConfirmed(intent);
        Assert.Equal(1, Assert.Single(journal.Events).Sequence);
    }

    [Fact]
    public void StopAndSelectionChangeBlockPlaybackButPreserveHistory()
    {
        var store = Store(new InMemorySimulationJournal(32));
        var coordinator = new SimulationPlaybackCoordinator(store);
        var intent = Intent();
        Assert.Throws<InvalidOperationException>(() => coordinator.ActivateConfirmed(intent));
        coordinator.SetStop(false);
        coordinator.ActivateConfirmed(intent);
        coordinator.SetStop(true);
        Assert.Throws<InvalidOperationException>(() =>
            coordinator.Play(SimulationScenario.AcknowledgeAndFill));
        Assert.Single(coordinator.History);
        coordinator.SetStop(false);
        coordinator.ActivateConfirmed(intent);
        coordinator.InvalidateActive();
        Assert.Throws<InvalidOperationException>(() =>
            coordinator.Play(SimulationScenario.AcknowledgeAndFill));
        Assert.Single(coordinator.History);
    }

    [Fact]
    public void DuplicateCallbackIsAuditedWithoutJournalMutation()
    {
        var journal = new InMemorySimulationJournal(16);
        var store = Store(journal);
        var intent = Intent();
        store.RegisterConfirmed(intent);
        store.Submit(intent);
        store.ApplyCallback(intent.ClientOrderId, SimulationOrderState.Acknowledged, "ACK");
        var count = journal.ReadAll().Count;
        store.ApplyCallback(intent.ClientOrderId, SimulationOrderState.Acknowledged, "DUP ACK");
        Assert.Equal(count, journal.ReadAll().Count);
        Assert.Contains(store.Audit,
            item => item.Action == SimulationLifecycleAuditAction.DuplicateIgnored);
    }

    [Fact]
    public void JournalAndAuditAreBoundedAndContainNoSecretOrTokenFields()
    {
        var journal = new InMemorySimulationJournal(2);
        var store = new SimulationOrderStore(journal, 2, () => Now,
            recoverPendingAsUnknown: false);
        var intent = Intent();
        store.RegisterConfirmed(intent);
        store.Submit(intent);
        Assert.Throws<InvalidOperationException>(() =>
            store.ApplyCallback(intent.ClientOrderId, SimulationOrderState.Acknowledged, "ACK"));
        Assert.True(store.Audit.Count <= 2);
        foreach (var type in new[] { typeof(SimulationJournalEvent), typeof(SimulationLifecycleAuditEvent) })
        {
            var names = type.GetProperties().Select(property => property.Name);
            Assert.DoesNotContain(names, name =>
                name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Key", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void ReplayRejectsNonContiguousCommittedSequence()
    {
        var journal = new InMemorySimulationJournal(4);
        var intent = Intent();
        journal.Append(new(2, Now, intent.ClientOrderId, IntentFingerprint.Create(intent),
            SimulationOrderState.Confirmed, SimulationTransitionKind.Standard,
            "CONFIRMED", intent));
        Assert.Throws<InvalidDataException>(() => Store(journal));
    }

    private static SimulationOrderStore Store(ISimulationJournal journal) =>
        new(journal, 32, () => Now);

    private static MarketOrderIntent Intent() =>
        new(TradingVenue.Mexc,
            new("APT", "USDT", MarketProduct.Spot),
            OrderSide.Buy, OrderType.Market, OrderSizingMode.QuoteNotional,
            5, "trdng-sim-1");

    private sealed class FailOnceJournal : ISimulationJournal
    {
        private bool _fail = true;
        public List<SimulationJournalEvent> Events { get; } = [];
        public IReadOnlyList<SimulationJournalEvent> ReadAll() => Events;
        public void Append(SimulationJournalEvent value)
        {
            if (_fail)
            {
                _fail = false;
                throw new IOException("simulated write failure");
            }
            Events.Add(value);
        }
    }
}
