using Trdng.Core.Credentials;
using Trdng.Core.Instruments;
using Trdng.Core.Orders;
using Trdng.Mexc.MarketData;

namespace Trdng.Mexc.Private;

public enum MexcOrderTestPreflightState
{
    Eligible, NeedsMetadata, MetadataStale, MarketBlocked, RiskBlocked,
    StopEngaged, OrderTestKeyRequired
}

public sealed record MexcCredentialProfilePresence(
    CredentialVaultState ApiKey, CredentialVaultState Secret)
{
    public bool IsStored => ApiKey == CredentialVaultState.Stored &&
        Secret == CredentialVaultState.Stored;
    public string Masked => IsStored ? "STORED" : "MISSING";
}

public sealed record MexcOrderTestPreflight(
    string Symbol,
    MarketProduct Product,
    OrderSide Side,
    OrderSizingMode SizingMode,
    decimal Value,
    string MetadataSource,
    DateTimeOffset? MetadataObservedAt,
    bool MetadataFresh,
    decimal? RiskCap,
    bool StopEngaged,
    string ReadOnlyProfile,
    string OrderTestProfile,
    MexcOrderTestPreflightState State,
    string Code,
    bool IsActionEnabled = false);

public static class MexcOrderTestPreflightPresentation
{
    public static string Masked(MexcOrderTestPreflight value) =>
        $"{value.Symbol} · {value.Product.ToString().ToUpperInvariant()} · " +
        $"{value.Side.ToString().ToUpperInvariant()} {value.Value} · " +
        $"METADATA {(value.MetadataFresh ? "FRESH" : "MISSING/STALE")} · " +
        $"LIMIT {(value.RiskCap is { } cap ? cap : "UNCONFIGURED")} · " +
        $"STOP {(value.StopEngaged ? "ON" : "OFF")} · " +
        $"READ {value.ReadOnlyProfile} · TEST {value.OrderTestProfile} · {value.Code}";
}

public static class MexcOrderTestPreflightEvaluator
{
    public const string OfficialMetadataSource =
        "https://api.mexc.com/api/v3/exchangeInfo";

    public static MexcOrderTestPreflight Evaluate(
        MarketOrderIntent intent,
        MexcInstrumentMetadata? metadata,
        DateTimeOffset? metadataObservedAt,
        DateTimeOffset now,
        TimeSpan metadataMaxAge,
        RiskProfile? riskProfile,
        ReferencePrice? referencePrice,
        bool stopEngaged,
        MexcCredentialProfilePresence readOnlyProfile,
        MexcCredentialProfilePresence orderTestProfile)
    {
        var symbol = StarterInstrumentCatalog.Find(intent.Instrument, TradingVenue.Mexc)?.VenueSymbol
            ?? intent.Instrument.PairId;
        var fresh = metadata is not null && metadataObservedAt is { } observed &&
            metadataMaxAge > TimeSpan.Zero && observed <= now && now - observed <= metadataMaxAge;
        var cap = intent.SizingMode == OrderSizingMode.QuoteNotional
            ? riskProfile?.MaximumQuoteNotional : riskProfile?.MaximumBaseQuantity;

        MexcOrderTestPreflightState state;
        string code;
        if (intent.Venue != TradingVenue.Mexc || intent.Instrument.Product != MarketProduct.Spot)
            (state, code) = (MexcOrderTestPreflightState.MarketBlocked, "MEXC_SPOT_REQUIRED");
        else if (metadata is null)
            (state, code) = (MexcOrderTestPreflightState.NeedsMetadata, "METADATA_MISSING");
        else if (!fresh)
            (state, code) = (MexcOrderTestPreflightState.MetadataStale, "METADATA_STALE");
        else if (!string.Equals(metadata.Symbol, symbol, StringComparison.Ordinal) ||
                 !string.Equals(metadata.BaseAsset, intent.Instrument.BaseAsset,
                     StringComparison.Ordinal) ||
                 !string.Equals(metadata.QuoteAsset, intent.Instrument.QuoteAsset,
                     StringComparison.Ordinal))
            (state, code) = (MexcOrderTestPreflightState.NeedsMetadata,
                "METADATA_INSTRUMENT_MISMATCH");
        else
        {
            var mapped = MexcOrderTestMetadataMapper.Map(metadata, intent.Side);
            if (mapped.State != MexcOrderTestMetadataState.Available)
                (state, code) = mapped.State == MexcOrderTestMetadataState.Blocked
                    ? (MexcOrderTestPreflightState.MarketBlocked, mapped.Code)
                    : (MexcOrderTestPreflightState.NeedsMetadata, mapped.Code);
            else
            {
                var validation = MarketOrderValidator.Validate(intent, mapped.Filters);
                var risk = DryRunRiskPolicy.Evaluate(
                    intent, validation, riskProfile, referencePrice, now);
                if (!validation.IsValid || !risk.IsAllowed)
                    (state, code) = (MexcOrderTestPreflightState.RiskBlocked,
                        validation.IsValid ? risk.Reason : validation.Message);
                else if (stopEngaged)
                    (state, code) = (MexcOrderTestPreflightState.StopEngaged, "STOP_ENGAGED");
                else if (!orderTestProfile.IsStored)
                    (state, code) = (MexcOrderTestPreflightState.OrderTestKeyRequired,
                        "ORDER_TEST_KEY_REQUIRED");
                else
                    (state, code) = (MexcOrderTestPreflightState.Eligible,
                        "OWNER_GATED_TEST_ELIGIBLE");
            }
        }

        return new(symbol, intent.Instrument.Product, intent.Side, intent.SizingMode,
            intent.SizingValue, OfficialMetadataSource, metadataObservedAt, fresh, cap,
            stopEngaged, readOnlyProfile.Masked, orderTestProfile.Masked, state, code);
    }
}
