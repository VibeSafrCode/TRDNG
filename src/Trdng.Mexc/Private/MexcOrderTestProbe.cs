using System.Security.Cryptography;
using System.Text;
using Trdng.Core.Instruments;
using Trdng.Core.Orders;
using Trdng.Mexc.MarketData;

namespace Trdng.Mexc.Private;

[Flags]
public enum MexcProbeCaveat
{
    None = 0,
    QuoteOrderQtySupportUnproven = 1,
    BaseMaximumUnproven = 2,
    BaseStepUnproven = 4
}

public enum MexcProbeCandidateState { Prepared, Rejected }

public sealed record MexcOrderTestProbeCandidate(
    string Symbol,
    CanonicalInstrument Instrument,
    OrderSide Side,
    OrderSizingMode SizingMode,
    decimal Value,
    decimal EstimatedQuoteExposure,
    decimal TinyQuoteCap,
    DateTimeOffset MetadataObservedAt,
    DateTimeOffset MetadataValidUntil,
    DateTimeOffset? ReferencePriceObservedAt,
    DateTimeOffset? ReferencePriceValidUntil,
    string ClientOrderId,
    MexcProbeCaveat Caveats,
    string Fingerprint);

public sealed record MexcProbeCandidateResult(
    MexcProbeCandidateState State, string Code, MexcOrderTestProbeCandidate? Candidate);

public static class MexcOrderTestProbePolicy
{
    public static MexcProbeCandidateResult Derive(
        CanonicalInstrument instrument,
        OrderSide side,
        MexcInstrumentMetadata metadata,
        DateTimeOffset metadataObservedAt,
        DateTimeOffset now,
        TimeSpan metadataMaxAge,
        decimal tinyQuoteCap,
        string clientOrderId,
        decimal? explicitOwnerQuoteValue = null,
        ReferencePrice? executableReferencePrice = null,
        TimeSpan? referencePriceMaxAge = null)
    {
        if (instrument.Product != MarketProduct.Spot || metadataMaxAge <= TimeSpan.Zero ||
            metadataObservedAt == default || metadataObservedAt > now ||
            now - metadataObservedAt > metadataMaxAge)
            return Reject("METADATA_STALE_OR_PRODUCT_INVALID");
        var capability = StarterInstrumentCatalog.Find(instrument, TradingVenue.Mexc);
        if (capability is null || !string.Equals(capability.VenueSymbol, metadata.Symbol,
                StringComparison.Ordinal) ||
            !string.Equals(metadata.BaseAsset, instrument.BaseAsset, StringComparison.Ordinal) ||
            !string.Equals(metadata.QuoteAsset, instrument.QuoteAsset, StringComparison.Ordinal))
            return Reject("METADATA_INSTRUMENT_MISMATCH");
        if (metadata.Status != "1" || !metadata.IsSpotTradingAllowed ||
            !metadata.OrderTypes.Contains("MARKET", StringComparer.Ordinal))
            return Reject("MARKET_DISABLED");
        if (!SideAllowed(metadata.TradeSideType, side)) return Reject("SIDE_DISABLED_OR_UNKNOWN");
        if (tinyQuoteCap <= 0 || !ClientOrderIdPolicy.IsValid(clientOrderId))
            return Reject("LOCAL_GATE_INVALID");

        DateTimeOffset metadataValidUntil;
        try { metadataValidUntil = metadataObservedAt + metadataMaxAge; }
        catch (ArgumentOutOfRangeException) { return Reject("METADATA_TIME_INVALID"); }

        decimal value;
        decimal exposure;
        DateTimeOffset? referenceObservedAt = null;
        DateTimeOffset? referenceValidUntil = null;
        MexcProbeCaveat caveats;
        if (side == OrderSide.Buy)
        {
            if (metadata.QuoteOrderQtyMarketAllowed is false)
                return Reject("QUOTE_ORDER_QTY_DISABLED");
            if (metadata.MinimumMarketQuoteAmount is not > 0 ||
                metadata.MaximumMarketQuoteAmount is not > 0 ||
                metadata.MinimumMarketQuoteAmount > metadata.MaximumMarketQuoteAmount)
                return Reject("MARKET_QUOTE_LIMITS_MISSING");
            value = explicitOwnerQuoteValue ?? metadata.MinimumMarketQuoteAmount.Value;
            if (value < metadata.MinimumMarketQuoteAmount ||
                value > metadata.MaximumMarketQuoteAmount || value > tinyQuoteCap)
                return Reject("QUOTE_VALUE_OUTSIDE_DOCUMENTED_OR_LOCAL_LIMIT");
            exposure = value;
            caveats = metadata.QuoteOrderQtyMarketAllowed is null
                ? MexcProbeCaveat.QuoteOrderQtySupportUnproven : MexcProbeCaveat.None;
        }
        else
        {
            if (metadata.MinimumBaseQuantity is not > 0)
                return Reject("BASE_MINIMUM_MISSING");
            if (referencePriceMaxAge is not { } maxAge || maxAge <= TimeSpan.Zero ||
                executableReferencePrice is null || executableReferencePrice.Price <= 0 ||
                executableReferencePrice.ObservedAt == default ||
                executableReferencePrice.ObservedAt > now ||
                now - executableReferencePrice.ObservedAt > maxAge)
                return Reject("FRESH_EXECUTABLE_PRICE_REQUIRED");
            value = metadata.MinimumBaseQuantity.Value;
            try { exposure = checked(value * executableReferencePrice.Price); }
            catch (OverflowException) { return Reject("EXPOSURE_OVERFLOW"); }
            if (exposure > tinyQuoteCap) return Reject("TINY_QUOTE_CAP_EXCEEDED");
            referenceObservedAt = executableReferencePrice.ObservedAt;
            try { referenceValidUntil = executableReferencePrice.ObservedAt + maxAge; }
            catch (ArgumentOutOfRangeException) { return Reject("REFERENCE_TIME_INVALID"); }
            caveats = MexcProbeCaveat.BaseMaximumUnproven |
                MexcProbeCaveat.BaseStepUnproven;
        }

        var sizing = side == OrderSide.Buy
            ? OrderSizingMode.QuoteNotional : OrderSizingMode.BaseQuantity;
        var fingerprint = Fingerprint(capability.VenueSymbol, instrument, side, sizing,
            value, exposure, metadataObservedAt, metadataValidUntil, referenceObservedAt,
            referenceValidUntil, tinyQuoteCap, caveats, clientOrderId);
        return new(MexcProbeCandidateState.Prepared,
            caveats == MexcProbeCaveat.None ? "PROBE_PREPARED" : "UNPROVEN_UNTIL_ORDER_TEST",
            new(capability.VenueSymbol, instrument, side, sizing, value, exposure,
                tinyQuoteCap, metadataObservedAt, metadataValidUntil, referenceObservedAt,
                referenceValidUntil, clientOrderId, caveats, fingerprint));
    }

    private static bool SideAllowed(int? type, OrderSide side) => type == 1 ||
        side == OrderSide.Buy && type == 2 || side == OrderSide.Sell && type == 3;

    internal static bool HasValidFingerprint(MexcOrderTestProbeCandidate candidate) =>
        string.Equals(candidate.Fingerprint, Fingerprint(candidate.Symbol,
            candidate.Instrument, candidate.Side, candidate.SizingMode, candidate.Value,
            candidate.EstimatedQuoteExposure, candidate.MetadataObservedAt,
            candidate.MetadataValidUntil, candidate.ReferencePriceObservedAt,
            candidate.ReferencePriceValidUntil, candidate.TinyQuoteCap, candidate.Caveats,
            candidate.ClientOrderId),
            StringComparison.Ordinal);

    private static string Fingerprint(string symbol, CanonicalInstrument instrument,
        OrderSide side, OrderSizingMode sizing, decimal value, decimal exposure,
        DateTimeOffset observed, DateTimeOffset validUntil,
        DateTimeOffset? referenceObserved, DateTimeOffset? referenceValidUntil,
        decimal cap, MexcProbeCaveat caveats, string clientId)
    {
        var canonical = string.Join('|', symbol, instrument.Id, side, sizing,
            MexcDecimalWire.Format(value), MexcDecimalWire.Format(exposure),
            observed.ToUnixTimeMilliseconds(), validUntil.ToUnixTimeMilliseconds(),
            referenceObserved?.ToUnixTimeMilliseconds().ToString() ?? "-",
            referenceValidUntil?.ToUnixTimeMilliseconds().ToString() ?? "-",
            MexcDecimalWire.Format(cap), (int)caveats, clientId);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static MexcProbeCandidateResult Reject(string code) =>
        new(MexcProbeCandidateState.Rejected, code, null);
}

public sealed record MexcPreparedProbe(
    string Token, MexcOrderTestProbeCandidate Candidate, DateTimeOffset ExpiresAt);
public sealed record MexcProbePrepareResult(bool Prepared, string Code, MexcPreparedProbe? Value);

public interface IMexcProbeKillSwitch
{
    bool IsEngaged { get; }
}

public sealed class MexcProbeKillSwitch : IMexcProbeKillSwitch
{
    private int _engaged = 1;
    public bool IsEngaged => Volatile.Read(ref _engaged) != 0;
    public void Engage() => Interlocked.Exchange(ref _engaged, 1);
    public void Disengage() => Interlocked.Exchange(ref _engaged, 0);
}

public sealed class MexcProbeAuthorization
{
    private int _consumed;
    internal MexcProbeAuthorization(MexcOrderTestProbeCandidate candidate,
        IMexcProbeKillSwitch killSwitch)
    {
        Candidate = candidate;
        KillSwitch = killSwitch;
    }
    public MexcOrderTestProbeCandidate Candidate { get; }
    internal IMexcProbeKillSwitch KillSwitch { get; }
    internal bool TryConsume() => Interlocked.Exchange(ref _consumed, 1) == 0;
}

public sealed class MexcProbeAuthorizationController
{
    private readonly TimeSpan _ttl;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<string> _token;
    private readonly IMexcProbeKillSwitch _killSwitch;
    private readonly object _sync = new();
    private MexcPreparedProbe? _prepared;

    public MexcProbeAuthorizationController(TimeSpan ttl, IMexcProbeKillSwitch killSwitch,
        Func<DateTimeOffset>? clock = null, Func<string>? token = null)
    {
        if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl));
        _ttl = ttl;
        _killSwitch = killSwitch ?? throw new ArgumentNullException(nameof(killSwitch));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _token = token ?? (() => Guid.NewGuid().ToString("N"));
    }

    public MexcProbePrepareResult Prepare(MexcOrderTestProbeCandidate candidate)
    {
        lock (_sync)
        {
            _prepared = null;
            var now = _clock();
            if (_killSwitch.IsEngaged || now > candidate.MetadataValidUntil ||
                (candidate.ReferencePriceValidUntil is { } referenceUntil && now > referenceUntil) ||
                !MexcOrderTestProbePolicy.HasValidFingerprint(candidate))
                return new(false, _killSwitch.IsEngaged ? "STOP_ENGAGED" :
                    "FINGERPRINT_INVALID", null);
            var token = _token();
            if (!ValidToken(token)) return new(false, "TOKEN_INVALID", null);
            try { _prepared = new(token, candidate, now + _ttl); }
            catch (ArgumentOutOfRangeException)
            { return new(false, "TIME_INVALID", null); }
            return new(true, "OWNER_CONFIRMATION_REQUIRED", _prepared);
        }
    }

    public MexcProbeAuthorization? Confirm(string token,
        MexcOrderTestProbeCandidate exactCandidate, bool orderTestKeyStored,
        bool timeSynchronized)
    {
        lock (_sync)
        {
            var prepared = _prepared;
            _prepared = null;
            var now = _clock();
            if (prepared is null || _killSwitch.IsEngaged || !orderTestKeyStored ||
                !timeSynchronized || now >= prepared.ExpiresAt ||
                now > exactCandidate.MetadataValidUntil ||
                (exactCandidate.ReferencePriceValidUntil is { } referenceUntil &&
                    now > referenceUntil) || token != prepared.Token ||
                exactCandidate != prepared.Candidate ||
                exactCandidate.Fingerprint != prepared.Candidate.Fingerprint ||
                !MexcOrderTestProbePolicy.HasValidFingerprint(exactCandidate))
                return null;
            return new(exactCandidate, _killSwitch);
        }
    }

    public void Invalidate() { lock (_sync) _prepared = null; }

    private static bool ValidToken(string? token) => token is { Length: >= 16 and <= 128 } &&
        token.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
}

public sealed class OrderTestValidatedEvidence
{
    internal OrderTestValidatedEvidence(string symbol, OrderSide side,
        OrderSizingMode sizingMode, decimal value, DateTimeOffset metadataObservedAt,
        string candidateFingerprint, string wireRequestFingerprint,
        DateTimeOffset resultObservedAt, MexcProbeCaveat caveats)
    {
        Symbol = symbol;
        Side = side;
        SizingMode = sizingMode;
        Value = value;
        MetadataObservedAt = metadataObservedAt;
        CandidateFingerprint = candidateFingerprint;
        WireRequestFingerprint = wireRequestFingerprint;
        ResultObservedAt = resultObservedAt;
        Caveats = caveats;
    }
    public string Symbol { get; }
    public OrderSide Side { get; }
    public OrderSizingMode SizingMode { get; }
    public decimal Value { get; }
    public DateTimeOffset MetadataObservedAt { get; }
    public string CandidateFingerprint { get; }
    public string WireRequestFingerprint { get; }
    public DateTimeOffset ResultObservedAt { get; }
    public MexcProbeCaveat Caveats { get; }
}

public sealed record MexcProbeExecutionResult(
    MexcOrderTestState State,
    OrderTestValidatedEvidence? Evidence,
    MexcDiagnostic? Diagnostic = null);
