using Trdng.Core.Instruments;
using Trdng.Core.Orders;

namespace Trdng.Core.Tests.Orders;

public sealed class DryRunOrderModelTests
{
    private static readonly CanonicalInstrument Spot = new("APT", "USDT", MarketProduct.Spot);
    private static readonly CanonicalInstrument Perpetual = new("APT", "USDT", MarketProduct.Perpetual);

    [Fact]
    public void CreatesIntentForExactlyTheExplicitVenueWithoutFallback()
    {
        var result = Factory().Create(TradingVenue.Bybit, Perpetual, OrderSide.Buy,
            OrderSizingMode.QuoteNotional, 10, QuoteFilters());
        Assert.Equal(TradingVenue.Bybit, result.Intent!.Venue);
        Assert.Equal(Perpetual, result.Intent.Instrument);
        Assert.Equal(OrderType.Market, result.Intent.Type);
        Assert.Null(result.Intent.Price);
    }

    [Theory]
    [InlineData(OrderSide.Buy, OrderSizingMode.QuoteNotional)]
    [InlineData(OrderSide.Sell, OrderSizingMode.BaseQuantity)]
    public void PreservesExplicitSizingSemantics(OrderSide side, OrderSizingMode mode)
    {
        var filters = mode == OrderSizingMode.QuoteNotional ? QuoteFilters() : BaseFilters();
        var result = Factory().Create(TradingVenue.Mexc, Spot, side, mode, 10, filters);
        Assert.Equal(mode, result.Intent!.SizingMode);
        Assert.Equal(10, result.Intent.SizingValue);
    }

    [Theory]
    [InlineData(OrderSide.Buy, OrderSizingMode.QuoteNotional, "USDT")]
    [InlineData(OrderSide.Sell, OrderSizingMode.BaseQuantity, "APT")]
    public void DefaultsKeepSideSpecificUnitExplicit(
        OrderSide side, OrderSizingMode expectedMode, string expectedUnit)
    {
        var sizing = OrderSizingDefaults.For(side, "APT", "USDT");
        Assert.Equal(expectedMode, sizing.Mode);
        Assert.Equal(expectedUnit, sizing.Unit);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void RejectsNonPositiveValue(string raw)
    {
        var result = Factory().Create(TradingVenue.Mexc, Spot, OrderSide.Buy,
            OrderSizingMode.QuoteNotional, decimal.Parse(raw), QuoteFilters());
        Assert.Equal(OrderValidationStatus.Invalid, result.Validation.Status);
    }

    [Fact]
    public void RejectsQuantityOutsideLimitsOrStep()
    {
        var factory = Factory();
        Assert.Equal(OrderValidationStatus.Invalid,
            factory.Create(TradingVenue.Bybit, Perpetual, OrderSide.Sell,
                OrderSizingMode.BaseQuantity, 0.005m, BaseFilters()).Validation.Status);
        Assert.Equal(OrderValidationStatus.Invalid,
            factory.Create(TradingVenue.Bybit, Perpetual, OrderSide.Sell,
                OrderSizingMode.BaseQuantity, 1.005m, BaseFilters()).Validation.Status);
    }

    [Fact]
    public void MissingOfficialFiltersIsNeedsMetadataNotGuessed()
    {
        var result = Factory().Create(TradingVenue.Mexc, Spot, OrderSide.Buy,
            OrderSizingMode.QuoteNotional, 10, null);
        Assert.Equal(OrderValidationStatus.NeedsMetadata, result.Validation.Status);
    }

    [Fact]
    public void MexcPerpetualIsBlockedAndDoesNotFallback()
    {
        var result = Factory().Create(TradingVenue.Mexc, Perpetual, OrderSide.Buy,
            OrderSizingMode.QuoteNotional, 10, QuoteFilters());
        Assert.False(result.Created);
        Assert.Equal(OrderValidationStatus.Blocked, result.Validation.Status);
    }

    [Fact]
    public void UnsupportedSpotVenueIsBlocked()
    {
        var result = Factory().Create(TradingVenue.Bybit, Spot, OrderSide.Buy,
            OrderSizingMode.QuoteNotional, 10, QuoteFilters());
        Assert.False(result.Created);
    }

    [Fact]
    public void ExplicitUnsupportedCapabilityCreatesNoIntentAndDoesNotFallback()
    {
        var capability = new VenueInstrumentCapability(
            Perpetual, TradingVenue.Bybit, "APTUSDT",
            CapabilityAvailability.Available, CapabilityAvailability.Unsupported);
        var factory = new DryRunOrderFactory(
            new ClientOrderIdGenerator(() => DateTimeOffset.UnixEpoch, () => 1),
            (_, _) => capability);
        var result = factory.Create(TradingVenue.Bybit, Perpetual, OrderSide.Buy,
            OrderSizingMode.QuoteNotional, 10, QuoteFilters());
        Assert.False(result.Created);
        Assert.Equal(OrderValidationStatus.Blocked, result.Validation.Status);
    }

    [Theory]
    [InlineData("1,5", "1.5")]
    [InlineData("1.5", "1.5")]
    [InlineData("-1,5", "-1.5")]
    public void StrictSizingParserAcceptsOneDecimalSeparator(string raw, string expected)
    {
        Assert.True(OrderSizingValueParser.TryParse(raw, out var value));
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), value);
    }

    [Theory]
    [InlineData("1,000.5")]
    [InlineData("1.000,5")]
    [InlineData("1e3")]
    [InlineData("1 000")]
    [InlineData("1,")]
    [InlineData("abc")]
    public void StrictSizingParserRejectsGroupsExponentAndGarbage(string raw) =>
        Assert.False(OrderSizingValueParser.TryParse(raw, out _));

    [Theory]
    [InlineData(10, 5, OrderValidationStatus.Valid)]
    [InlineData(11, 5, OrderValidationStatus.Invalid)]
    [InlineData(10, -1, OrderValidationStatus.NeedsMetadata)]
    public void ValidatesOptionalQuoteNotionalStep(
        int value, int step, OrderValidationStatus expected)
    {
        var filters = QuoteFilters() with { QuoteNotionalStep = step };
        var result = Factory().Create(TradingVenue.Mexc, Spot, OrderSide.Buy,
            OrderSizingMode.QuoteNotional, value, filters);
        Assert.Equal(expected, result.Validation.Status);
    }

    [Fact]
    public void MissingQuoteStepDoesNotBlockKnownMinMax()
    {
        var result = Factory().Create(TradingVenue.Mexc, Spot, OrderSide.Buy,
            OrderSizingMode.QuoteNotional, 10, QuoteFilters());
        Assert.Equal(OrderValidationStatus.Valid, result.Validation.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("UPPER")]
    [InlineData("bad_id")]
    public void ValidatorRejectsUnsafeClientOrderId(string clientOrderId)
    {
        var intent = new MarketOrderIntent(
            TradingVenue.Mexc, Spot, OrderSide.Buy, OrderType.Market,
            OrderSizingMode.QuoteNotional, 10, clientOrderId);
        Assert.Equal(OrderValidationStatus.Invalid,
            MarketOrderValidator.Validate(intent, QuoteFilters()).Status);
    }

    [Fact]
    public void ValidatorRejectsOverlongClientOrderId()
    {
        var intent = new MarketOrderIntent(
            TradingVenue.Mexc, Spot, OrderSide.Buy, OrderType.Market,
            OrderSizingMode.QuoteNotional, 10, new string('a', 33));
        Assert.Equal(OrderValidationStatus.Invalid,
            MarketOrderValidator.Validate(intent, QuoteFilters()).Status);
    }

    [Fact]
    public void ValidatorRejectsClientOrderIdContainingOnlyDashes()
    {
        var intent = new MarketOrderIntent(
            TradingVenue.Mexc, Spot, OrderSide.Buy, OrderType.Market,
            OrderSizingMode.QuoteNotional, 10, "---");
        Assert.Equal(OrderValidationStatus.Invalid,
            MarketOrderValidator.Validate(intent, QuoteFilters()).Status);
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(100, -1)]
    [InlineData(100, 10)]
    public void ContradictoryQuoteFiltersAreNeverValid(int minimum, int maximum)
    {
        var filters = new OrderFilterSet(
            MinimumQuoteNotional: minimum,
            MaximumQuoteNotional: maximum);
        var result = Factory().Create(TradingVenue.Mexc, Spot, OrderSide.Buy,
            OrderSizingMode.QuoteNotional, 20, filters);
        Assert.Equal(OrderValidationStatus.NeedsMetadata, result.Validation.Status);
    }

    [Theory]
    [InlineData(-1, 100, 1)]
    [InlineData(1, -100, 1)]
    [InlineData(100, 10, 1)]
    [InlineData(1, 100, -1)]
    public void ContradictoryBaseFiltersAreNeverValid(
        int minimum, int maximum, int step)
    {
        var filters = new OrderFilterSet(
            MinimumBaseQuantity: minimum,
            MaximumBaseQuantity: maximum,
            BaseQuantityStep: step);
        var result = Factory().Create(TradingVenue.Bybit, Perpetual, OrderSide.Sell,
            OrderSizingMode.BaseQuantity, 20, filters);
        Assert.Equal(OrderValidationStatus.NeedsMetadata, result.Validation.Status);
    }

    [Fact]
    public void DeniedVenueMessageNamesRejectedAndStillActiveVenue()
    {
        Assert.Equal("MEXC ОТКЛОНЕНА · АКТИВНА GATE",
            DryRunVenueSelectionMessage.Rejected(TradingVenue.Mexc, TradingVenue.Gate));
    }

    [Fact]
    public void ClientOrderIdIsDeterministicSafeAndUnique()
    {
        long sequence = 0;
        var generator = new ClientOrderIdGenerator(
            () => DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000),
            () => ++sequence);
        var first = generator.Next();
        var second = generator.Next();
        Assert.Equal("trdng-18bcfe56800-1", first);
        Assert.NotEqual(first, second);
        Assert.True(second.Length <= ClientOrderIdGenerator.MaximumLength);
        Assert.Matches("^[a-z0-9-]+$", second);
    }

    private static DryRunOrderFactory Factory() => new(new ClientOrderIdGenerator(
        () => DateTimeOffset.UnixEpoch, () => 1));
    private static OrderFilterSet QuoteFilters() => new(
        MinimumQuoteNotional: 5, MaximumQuoteNotional: 1000);
    private static OrderFilterSet BaseFilters() => new(
        MinimumBaseQuantity: 0.01m, MaximumBaseQuantity: 100,
        BaseQuantityStep: 0.01m);
}
