using Trdng.Core.Instruments;
using Trdng.Core.Orders;

namespace Trdng.Core.Tests.Orders;

public sealed class DryRunRiskGateTests
{
    private static readonly CanonicalInstrument Instrument =
        new("APT", "USDT", MarketProduct.Spot);
    private static readonly DateTimeOffset Start =
        new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
    [Fact]
    public void StartupKillSwitchBlocksPrepareAndConfirm()
    {
        var controller = Controller(out _, out _);
        var intent = Intent(OrderSizingMode.QuoteNotional, 5);
        Assert.Equal(PrepareStatus.Rejected,
            controller.Prepare(intent, ValidFor(intent), Profile(intent), null).Status);
        Assert.Equal(ConfirmationStatus.Rejected,
            controller.Confirm("token", intent).Status);
    }

    [Fact]
    public void ProductionProfileCannotDisengageSimulationStop()
    {
        var controller = Controller(out _, out _);
        var intent = Intent(OrderSizingMode.QuoteNotional, 5);
        Assert.False(controller.DisengageForSimulation(
            RiskProfile.ProductionUnconfigured(intent.Venue, intent.Instrument,
                intent.Side, intent.SizingMode)));
        Assert.True(controller.KillSwitchEngaged);
    }

    [Fact]
    public void MissingLimitsOrOfficialValidationFailClosed()
    {
        var intent = Intent(OrderSizingMode.QuoteNotional, 5);
        Assert.Equal(RiskDecisionStatus.NeedsConfiguration,
            DryRunRiskPolicy.Evaluate(intent, ValidFor(intent), null, null, Start).Status);
        Assert.Equal(RiskDecisionStatus.NeedsConfiguration,
            DryRunRiskPolicy.Evaluate(intent,
                new(OrderValidationStatus.NeedsMetadata, "missing"),
                Profile(intent), null, Start).Status);
    }

    [Fact]
    public void ValidationEvidenceMustBeBoundToExactIntent()
    {
        var intent = Intent(OrderSizingMode.QuoteNotional, 5);
        var unbound = new OrderValidationResult(OrderValidationStatus.Valid, "manual");
        Assert.Equal(RiskDecisionStatus.NeedsConfiguration,
            DryRunRiskPolicy.Evaluate(intent, unbound, Profile(intent), null, Start).Status);

        foreach (var other in new[]
        {
            intent with { ClientOrderId = "trdng-other" },
            intent with { SizingValue = 6 },
            intent with { Venue = TradingVenue.Gate }
        })
        {
            Assert.Equal(RiskDecisionStatus.NeedsConfiguration,
                DryRunRiskPolicy.Evaluate(intent, ValidFor(other), Profile(intent), null, Start).Status);
        }
    }

    [Fact]
    public void ConfiguredProductionProfileIsStillFailClosed()
    {
        var intent = Intent(OrderSizingMode.QuoteNotional, 5);
        var production = Profile(intent) with { Mode = RiskProfileMode.Production };
        Assert.Equal(RiskDecisionStatus.NeedsConfiguration,
            DryRunRiskPolicy.Evaluate(intent, ValidFor(intent), production, null, Start).Status);
    }

    [Fact]
    public void QuoteCapAllowsBoundaryAndBlocksAbove()
    {
        var atCap = Intent(OrderSizingMode.QuoteNotional, 10);
        var above = atCap with { SizingValue = 10.01m };
        Assert.True(DryRunRiskPolicy.Evaluate(
            atCap, ValidFor(atCap), Profile(atCap), null, Start).IsAllowed);
        Assert.Equal(RiskDecisionStatus.Blocked, DryRunRiskPolicy.Evaluate(
            above, ValidFor(above), Profile(above), null, Start).Status);
    }

    [Fact]
    public void BaseQuantityRequiresFreshPriceAndBothCaps()
    {
        var intent = Intent(OrderSizingMode.BaseQuantity, 0.1m, OrderSide.Sell);
        var profile = Profile(intent) with { MaximumBaseQuantity = 0.1m };
        Assert.Equal(RiskDecisionStatus.NeedsConfiguration,
            DryRunRiskPolicy.Evaluate(intent, ValidFor(intent), profile, null, Start).Status);
        Assert.Equal(RiskDecisionStatus.NeedsConfiguration,
            DryRunRiskPolicy.Evaluate(intent, ValidFor(intent), profile,
                new(100, Start - TimeSpan.FromSeconds(3)), Start).Status);
        Assert.True(DryRunRiskPolicy.Evaluate(intent, ValidFor(intent), profile,
            new(100, Start), Start).IsAllowed);
        Assert.Equal(RiskDecisionStatus.Blocked,
            DryRunRiskPolicy.Evaluate(intent with { SizingValue = 0.11m },
                ValidFor(intent with { SizingValue = 0.11m }),
                profile, new(100, Start), Start).Status);
        Assert.Equal(RiskDecisionStatus.Blocked,
            DryRunRiskPolicy.Evaluate(intent, ValidFor(intent),
                profile with { MaximumQuoteNotional = 9 }, new(100, Start), Start).Status);
    }

    [Fact]
    public void BaseQuantityRejectsMissingAgeLimitAndNotionalOverflow()
    {
        var intent = Intent(OrderSizingMode.BaseQuantity, decimal.MaxValue, OrderSide.Sell);
        var profile = Profile(intent) with
        {
            MaximumBaseQuantity = decimal.MaxValue,
            MaximumQuoteNotional = decimal.MaxValue,
            ReferencePriceMaxAge = TimeSpan.Zero
        };
        Assert.Equal(RiskDecisionStatus.NeedsConfiguration,
            DryRunRiskPolicy.Evaluate(intent, ValidFor(intent), profile,
                new(2, Start), Start).Status);
        Assert.Equal(RiskDecisionStatus.Blocked,
            DryRunRiskPolicy.Evaluate(intent, ValidFor(intent),
                profile with { ReferencePriceMaxAge = TimeSpan.FromSeconds(2) },
                new(2, Start), Start).Status);
    }

    [Fact]
    public void ExecutableReferencePriceUsesAskForBuyAndBidForSell()
    {
        var book = new Trdng.Core.MarketData.OrderBookSnapshot(
            "APTUSDT", 1, 1, [new(9, 1)], [new(11, 1)]);
        Assert.Equal(11, ExecutableReferencePrice.Select(book, OrderSide.Buy, Start)!.Price);
        Assert.Equal(9, ExecutableReferencePrice.Select(book, OrderSide.Sell, Start)!.Price);
    }

    [Fact]
    public void ExactProfileRejectsVenueProductSideOrSizingMutation()
    {
        var intent = Intent(OrderSizingMode.QuoteNotional, 5);
        var profile = Profile(intent);
        Assert.Equal(RiskDecisionStatus.Blocked, DryRunRiskPolicy.Evaluate(
            intent with { Venue = TradingVenue.Gate },
            ValidFor(intent with { Venue = TradingVenue.Gate }), profile, null, Start).Status);
        Assert.Equal(RiskDecisionStatus.Blocked, DryRunRiskPolicy.Evaluate(
            intent with { Side = OrderSide.Sell },
            ValidFor(intent with { Side = OrderSide.Sell }), profile, null, Start).Status);
        Assert.Equal(RiskDecisionStatus.Blocked, DryRunRiskPolicy.Evaluate(
            intent with { Instrument = new("APT", "USDT", MarketProduct.Perpetual) },
            ValidFor(intent with { Instrument = new("APT", "USDT", MarketProduct.Perpetual) }),
            profile, null, Start).Status);
        Assert.Equal(RiskDecisionStatus.Blocked, DryRunRiskPolicy.Evaluate(
            intent with { SizingMode = OrderSizingMode.BaseQuantity },
            ValidFor(intent with { SizingMode = OrderSizingMode.BaseQuantity }),
            profile, new(1, Start), Start).Status);
    }

    [Fact]
    public void ConfirmationIsExactSingleUseAndReplaySafe()
    {
        var controller = Controller(out _, out _);
        var intent = Intent(OrderSizingMode.QuoteNotional, 5);
        Assert.True(controller.DisengageForSimulation(Profile(intent)));
        var prepared = controller.Prepare(intent, ValidFor(intent), Profile(intent), null).Candidate!;
        Assert.Equal(ConfirmationStatus.Rejected,
            controller.Confirm(prepared.Token, intent with { SizingValue = 6 }).Status);

        prepared = controller.Prepare(intent, ValidFor(intent), Profile(intent), null).Candidate!;
        Assert.Equal(ConfirmationStatus.Confirmed,
            controller.Confirm(prepared.Token, intent).Status);
        Assert.Equal(ConfirmationStatus.Rejected,
            controller.Confirm(prepared.Token, intent).Status);
    }

    [Fact]
    public void ExpiryInputChangeAndKillSwitchInvalidatePreparation()
    {
        var controller = Controller(out var clock, out _);
        var intent = Intent(OrderSizingMode.QuoteNotional, 5);
        controller.DisengageForSimulation(Profile(intent));
        var prepared = controller.Prepare(intent, ValidFor(intent), Profile(intent), null).Candidate!;
        clock.Now += TimeSpan.FromSeconds(6);
        Assert.Equal(ConfirmationStatus.Rejected,
            controller.Confirm(prepared.Token, intent).Status);

        prepared = controller.Prepare(intent, ValidFor(intent), Profile(intent), null).Candidate!;
        controller.InvalidateConfirmation("SELECTION CHANGED");
        Assert.Equal(ConfirmationStatus.Rejected,
            controller.Confirm(prepared.Token, intent).Status);

        prepared = controller.Prepare(intent, ValidFor(intent), Profile(intent), null).Candidate!;
        controller.EngageKillSwitch();
        Assert.Equal(ConfirmationStatus.Rejected,
            controller.Confirm(prepared.Token, intent).Status);
    }

    [Fact]
    public void AuditIsBoundedAndHasNoSecretFields()
    {
        var audit = new DryRunAuditTrail(3);
        var controller = new DryRunConfirmationController(
            TimeSpan.FromSeconds(5), audit, () => Start, () => "token");
        var intent = Intent(OrderSizingMode.QuoteNotional, 5);
        controller.DisengageForSimulation(Profile(intent));
        controller.Prepare(intent, ValidFor(intent), Profile(intent), null);
        controller.Confirm("token", intent);
        Assert.Equal(3, audit.Events.Count);
        var names = typeof(DryRunAuditEvent).GetProperties().Select(property => property.Name);
        Assert.DoesNotContain(names, name =>
            name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Key", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AuditWithoutIntentUsesNullsAndIntentEventsArePopulated()
    {
        var audit = new DryRunAuditTrail(8);
        var controller = new DryRunConfirmationController(
            TimeSpan.FromSeconds(5), audit, () => Start, () => "token");
        controller.EngageKillSwitch();
        var stop = Assert.Single(audit.Events);
        Assert.Null(stop.Venue);
        Assert.Null(stop.Product);
        Assert.Null(stop.Side);
        Assert.Null(stop.SizingMode);
        Assert.Null(stop.SizingValue);

        var intent = Intent(OrderSizingMode.QuoteNotional, 5);
        controller.DisengageForSimulation(Profile(intent));
        controller.Prepare(intent, ValidFor(intent), Profile(intent), null);
        var prepare = audit.Events.Single(item => item.Action == DryRunAuditAction.Prepare);
        Assert.Equal(intent.Venue, prepare.Venue);
        Assert.Equal(intent.Instrument.Product, prepare.Product);
        Assert.Equal(intent.Side, prepare.Side);
        Assert.Equal(intent.SizingMode, prepare.SizingMode);
        Assert.Equal(intent.SizingValue, prepare.SizingValue);
    }

    private static DryRunConfirmationController Controller(
        out MutableClock clock, out DryRunAuditTrail audit)
    {
        clock = new(Start);
        audit = new(32);
        var captured = clock;
        return new(TimeSpan.FromSeconds(5), audit, () => captured.Now, () => "token");
    }

    private static MarketOrderIntent Intent(
        OrderSizingMode mode, decimal value, OrderSide side = OrderSide.Buy) =>
        new(TradingVenue.Mexc, Instrument, side, OrderType.Market, mode,
            value, "trdng-test-1");

    private static RiskProfile Profile(MarketOrderIntent intent) =>
        new("SIMULATION · 10 USDT CAP", RiskProfileMode.Simulation, true,
            intent.Venue, intent.Instrument, intent.Side, intent.SizingMode,
            10, 1, TimeSpan.FromSeconds(2));

    private static OrderValidationResult ValidFor(MarketOrderIntent intent) =>
        MarketOrderValidator.Validate(intent, intent.SizingMode == OrderSizingMode.QuoteNotional
            ? new OrderFilterSet(MinimumQuoteNotional: 0.01m,
                MaximumQuoteNotional: decimal.MaxValue)
            : new OrderFilterSet(MinimumBaseQuantity: 0.01m,
                MaximumBaseQuantity: decimal.MaxValue, BaseQuantityStep: 0.01m));

    private sealed class MutableClock(DateTimeOffset now)
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
