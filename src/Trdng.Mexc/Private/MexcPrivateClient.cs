using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Trdng.Mexc.Private;

public enum MexcPrivateAuditAction { TimeSync, Account, OpenOrders, OrderTest }
public sealed record MexcPrivateAuditEvent(DateTimeOffset Timestamp,
    MexcPrivateAuditAction Action, MexcPrivateState State,
    MexcDiagnostic? Diagnostic = null);

public sealed class MexcPrivateAudit
{
    private readonly int _capacity;
    private readonly object _sync = new();
    private readonly Queue<MexcPrivateAuditEvent> _events = [];
    private readonly Func<DateTimeOffset> _clock;
    public MexcPrivateAudit(int capacity, Func<DateTimeOffset>? clock = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }
    public IReadOnlyList<MexcPrivateAuditEvent> Events
    { get { lock (_sync) return _events.ToArray(); } }
    public void Add(MexcPrivateAuditAction action, MexcPrivateState state,
        MexcDiagnostic? diagnostic = null)
    {
        var item = new MexcPrivateAuditEvent(_clock(), action, state, diagnostic);
        lock (_sync)
        {
            while (_events.Count >= _capacity) _events.Dequeue();
            _events.Enqueue(item);
        }
    }
}

public sealed class MexcPrivateClient : IDisposable
{
    private const int MaxResponseBytes = 1_048_576;
    private static readonly Uri TimeEndpoint = new("https://api.mexc.com/api/v3/time");
    private readonly HttpMessageInvoker _http;
    private readonly MexcCredentialProvider _credentials;
    private readonly MexcOrderTestCredentialProvider _orderTestCredentials;
    private readonly MexcTimeSynchronizer _time;
    private readonly Func<long> _clockMilliseconds;
    private readonly TimeSpan _timeout;
    private readonly int _recvWindow;
    private readonly MexcPrivateAudit _audit;
    private readonly IMexcProbeKillSwitch _probeKillSwitch;

    internal MexcPrivateClient(HttpMessageHandler handler, MexcCredentialProvider credentials,
        MexcOrderTestCredentialProvider orderTestCredentials,
        MexcTimeSynchronizer time, Func<long> clockMilliseconds, TimeSpan timeout,
        int recvWindow, MexcPrivateAudit audit, IMexcProbeKillSwitch? probeKillSwitch = null)
    {
        _http = new HttpMessageInvoker(handler, disposeHandler: true);
        _credentials = credentials;
        _orderTestCredentials = orderTestCredentials;
        _time = time;
        _clockMilliseconds = clockMilliseconds;
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (recvWindow is <= 0 or > 60_000) throw new ArgumentOutOfRangeException(nameof(recvWindow));
        _timeout = timeout;
        _recvWindow = recvWindow;
        _audit = audit;
        _probeKillSwitch = probeKillSwitch ?? new MexcProbeKillSwitch();
    }

    public static MexcPrivateClient CreateProduction(MexcCredentialProvider credentials,
        MexcOrderTestCredentialProvider orderTestCredentials,
        MexcTimeSynchronizer time, Func<long> clockMilliseconds, TimeSpan timeout,
        int recvWindow, MexcPrivateAudit audit, IMexcProbeKillSwitch probeKillSwitch) =>
        new(CreateProductionHandler(), credentials,
            orderTestCredentials,
            time, clockMilliseconds, timeout, recvWindow, audit, probeKillSwitch);

    internal static SocketsHttpHandler CreateProductionHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false
    };

    public void Dispose() => _http.Dispose();

    public async Task<MexcPrivateState> SynchronizeTimeAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sent = _clockMilliseconds();
            using var timeout = LinkedTimeout(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, TimeEndpoint);
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Audit(MexcPrivateAuditAction.TimeSync, MapHttp(response.StatusCode));
            var bytes = await ReadBoundedAsync(response, timeout.Token).ConfigureAwait(false);
            var received = _clockMilliseconds();
            var state = _time.Record(MexcTimeSynchronizer.ParseServerTime(bytes), sent, received)
                ? MexcPrivateState.Ready : MexcPrivateState.TimeUnsynced;
            return Audit(MexcPrivateAuditAction.TimeSync, state);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return Audit(MexcPrivateAuditAction.TimeSync, MexcPrivateState.Unavailable); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException
            or System.Text.Json.JsonException or InvalidOperationException or OverflowException)
        { return Audit(MexcPrivateAuditAction.TimeSync, MexcPrivateState.Unavailable); }
    }

    public Task<MexcPrivateResult<MexcAccount>> GetAccountAsync(
        CancellationToken cancellationToken = default) => SendAsync(
            "/api/v3/account", new Dictionary<string, string?>(),
            MexcPrivateAuditAction.Account, MexcPrivateJson.ParseAccount, cancellationToken);

    public Task<MexcPrivateResult<IReadOnlyList<MexcOpenOrder>>> GetOpenOrdersAsync(
        string symbol, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol) ||
            !symbol.All(character => char.IsAsciiLetterOrDigit(character)))
            throw new ArgumentException("Invalid MEXC symbol.", nameof(symbol));
        return SendAsync("/api/v3/openOrders",
            new Dictionary<string, string?> { ["symbol"] = symbol.ToUpperInvariant() },
            MexcPrivateAuditAction.OpenOrders, MexcPrivateJson.ParseOpenOrders,
            cancellationToken);
    }

    public async Task<MexcOrderTestState> TestOrderAsync(
        MexcOrderTestAuthorization? authorization,
        CancellationToken cancellationToken = default)
    {
        if (authorization is null || !authorization.TryConsume() ||
            MexcOrderTestPolicy.Parameters(authorization) is not { } parameters)
            return AuditTest(MexcOrderTestState.TestRejected);
        try
        {
            if (!_time.TryTimestamp(_clockMilliseconds(), out _))
                return AuditTest(MexcOrderTestState.TimeUnsynced);
        }
        catch { return AuditTest(MexcOrderTestState.Error); }
        MexcCredentialResult credentialResult;
        try { credentialResult = await _orderTestCredentials.ReadAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch { return AuditTest(MexcOrderTestState.Error); }
        if (credentialResult.Lease is null)
            return AuditTest(MapTest(credentialResult.State));
        using var credentials = credentialResult.Lease;
        try
        {
            var now = _clockMilliseconds();
            if (!_time.TryTimestamp(now, out var timestamp))
                return AuditTest(MexcOrderTestState.TimeUnsynced);
            using var signed = MexcSignedRequestBuilder.BuildOrderTestPost(parameters, timestamp,
                _recvWindow, credentials.ApiKey, credentials.Secret);
            using var timeout = LinkedTimeout(cancellationToken);
            using var response = await _http.SendAsync(signed.Request, timeout.Token).ConfigureAwait(false);
            var bytes = await ReadBoundedAsync(response, timeout.Token).ConfigureAwait(false);
            return AuditTest(response.IsSuccessStatusCode
                ? MexcOrderTestState.TestReady
                : MapOrderTestError(response.StatusCode, MexcPrivateJson.ErrorCode(bytes)));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return AuditTest(MexcOrderTestState.Unavailable); }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException) { return AuditTest(MexcOrderTestState.Unavailable); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or InvalidDataException or CryptographicException or OverflowException)
        { return AuditTest(MexcOrderTestState.Error); }
    }

    public async Task<MexcProbeExecutionResult> TestProbeAsync(
        MexcProbeAuthorization? authorization,
        CancellationToken cancellationToken = default)
    {
        if (authorization is null || !authorization.TryConsume() ||
            !ReferenceEquals(authorization.KillSwitch, _probeKillSwitch) ||
            _probeKillSwitch.IsEngaged)
            return ProbeFail(MexcOrderTestState.TestRejected);
        var candidate = authorization.Candidate;
        long localNow;
        try
        {
            localNow = _clockMilliseconds();
            var now = DateTimeOffset.FromUnixTimeMilliseconds(localNow);
            if (!MexcOrderTestProbePolicy.HasValidFingerprint(candidate) ||
                now > candidate.MetadataValidUntil ||
                (candidate.ReferencePriceValidUntil is { } referenceUntil && now > referenceUntil))
                return ProbeFail(MexcOrderTestState.TestRejected);
            if (!_time.TryTimestamp(localNow, out _))
                return ProbeFail(MexcOrderTestState.TimeUnsynced);
        }
        catch { return ProbeFail(MexcOrderTestState.Error); }

        MexcCredentialResult credentialResult;
        try
        {
            credentialResult = await _orderTestCredentials.ReadAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch { return ProbeFail(MexcOrderTestState.Error); }
        if (credentialResult.Lease is null)
            return ProbeFail(MapTest(credentialResult.State));
        using var credentials = credentialResult.Lease;
        try
        {
            localNow = _clockMilliseconds();
            var now = DateTimeOffset.FromUnixTimeMilliseconds(localNow);
            if (!MexcOrderTestProbePolicy.HasValidFingerprint(candidate) ||
                now > candidate.MetadataValidUntil ||
                (candidate.ReferencePriceValidUntil is { } referenceUntil && now > referenceUntil))
                return ProbeFail(MexcOrderTestState.TestRejected);
            if (!_time.TryTimestamp(localNow, out var timestamp))
                return ProbeFail(MexcOrderTestState.TimeUnsynced);
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["symbol"] = candidate.Symbol,
                ["side"] = candidate.Side == Trdng.Core.Orders.OrderSide.Buy ? "BUY" : "SELL",
                ["type"] = "MARKET",
                ["newClientOrderId"] = candidate.ClientOrderId,
                [candidate.Side == Trdng.Core.Orders.OrderSide.Buy ? "quoteOrderQty" : "quantity"] =
                    MexcDecimalWire.Format(candidate.Value)
            };
            var canonicalBody = MexcSignedRequestBuilder.BuildOrderTestCanonicalBody(
                parameters, timestamp, _recvWindow);
            var wireFingerprint = Convert.ToHexString(SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(canonicalBody))).ToLowerInvariant();
            using var signed = MexcSignedRequestBuilder.BuildOrderTestPost(parameters, timestamp,
                _recvWindow, credentials.ApiKey, credentials.Secret);
            using var timeout = LinkedTimeout(cancellationToken);
            using var response = await _http.SendAsync(signed.Request, timeout.Token)
                .ConfigureAwait(false);
            var bytes = await ReadBoundedAsync(response, timeout.Token).ConfigureAwait(false);
            var errorCode = MexcPrivateJson.ErrorCode(bytes);
            if (!response.IsSuccessStatusCode || errorCode is not null)
            {
                var diagnostic = Diagnose(response.StatusCode, errorCode);
                return ProbeFail(MapOrderTestError(response.StatusCode, errorCode), diagnostic);
            }
            if (!IsExactEmptyObject(bytes))
                return ProbeFail(MexcOrderTestState.Error,
                    new MexcDiagnostic((int)response.StatusCode, null,
                        MexcFailureReason.ProtocolError));
            var observed = DateTimeOffset.FromUnixTimeMilliseconds(_clockMilliseconds());
            AuditTest(MexcOrderTestState.TestReady);
            return new(MexcOrderTestState.TestReady, new(candidate.Symbol, candidate.Side,
                candidate.SizingMode, candidate.Value, candidate.MetadataObservedAt,
                candidate.Fingerprint, wireFingerprint, observed, candidate.Caveats));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return ProbeFail(MexcOrderTestState.Unavailable); }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException) { return ProbeFail(MexcOrderTestState.Unavailable); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or InvalidDataException or CryptographicException or OverflowException)
        { return ProbeFail(MexcOrderTestState.Error); }
    }

    private async Task<MexcPrivateResult<T>> SendAsync<T>(string path,
        IReadOnlyDictionary<string, string?> parameters, MexcPrivateAuditAction action,
        Func<ReadOnlyMemory<byte>, T> parser, CancellationToken cancellationToken)
    {
        try
        {
            if (!_time.TryTimestamp(_clockMilliseconds(), out _))
                return Fail<T>(action, MexcPrivateState.TimeUnsynced);
        }
        catch (OperationCanceledException) { throw; }
        catch { return Fail<T>(action, MexcPrivateState.Error); }
        MexcCredentialResult credentialResult;
        try { credentialResult = await _credentials.ReadAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch { return Fail<T>(action, MexcPrivateState.Error); }
        if (credentialResult.Lease is null)
            return Fail<T>(action, credentialResult.State);
        using var credentials = credentialResult.Lease;
        try
        {
            var now = _clockMilliseconds();
            if (!_time.TryTimestamp(now, out var timestamp))
                return Fail<T>(action, MexcPrivateState.TimeUnsynced);
            using var signed = MexcSignedRequestBuilder.BuildGet(path, parameters, timestamp,
                _recvWindow, credentials.ApiKey, credentials.Secret);
            using var timeout = LinkedTimeout(cancellationToken);
            using var response = await _http.SendAsync(signed.Request, timeout.Token).ConfigureAwait(false);
            var bytes = await ReadBoundedAsync(response, timeout.Token).ConfigureAwait(false);
            var errorCode = MexcPrivateJson.ErrorCode(bytes);
            if (!response.IsSuccessStatusCode || errorCode is not null)
            {
                var diagnostic = Diagnose(response.StatusCode, errorCode);
                return Fail<T>(action, MapDiagnostic(diagnostic), diagnostic);
            }
            try
            {
                var value = parser(bytes);
                _audit.Add(action, MexcPrivateState.Ready);
                return new(MexcPrivateState.Ready, value);
            }
            catch (Exception exception) when (exception is InvalidDataException or
                System.Text.Json.JsonException or KeyNotFoundException or FormatException)
            {
                var diagnostic = new MexcDiagnostic((int)response.StatusCode, null,
                    MexcFailureReason.ProtocolError);
                return Fail<T>(action, MexcPrivateState.Error, diagnostic);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return Fail<T>(action, MexcPrivateState.Unavailable); }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException) { return Fail<T>(action, MexcPrivateState.Unavailable); }
        catch (InvalidDataException) { return Fail<T>(action, MexcPrivateState.Error); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or CryptographicException or OverflowException)
        { return Fail<T>(action, MexcPrivateState.Error); }
    }

    private CancellationTokenSource LinkedTimeout(CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(_timeout);
        return source;
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > MaxResponseBytes)
            throw new InvalidDataException("MEXC response exceeds local limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        var rented = ArrayPool<byte>.Shared.Rent(MaxResponseBytes + 1);
        var total = 0;
        try
        {
            while (total < MaxResponseBytes + 1)
            {
                var count = await stream.ReadAsync(
                    rented.AsMemory(total, (MaxResponseBytes + 1) - total), cancellationToken)
                    .ConfigureAwait(false);
                if (count == 0) break;
                total = checked(total + count);
            }
            if (total > MaxResponseBytes)
                throw new InvalidDataException("MEXC response exceeds local limit.");
            return rented.AsSpan(0, total).ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rented.AsSpan(0, Math.Min(total, rented.Length)));
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private MexcPrivateState Audit(MexcPrivateAuditAction action, MexcPrivateState state)
    { _audit.Add(action, state); return state; }
    private MexcPrivateResult<T> Fail<T>(MexcPrivateAuditAction action, MexcPrivateState state,
        MexcDiagnostic? diagnostic = null)
    {
        _audit.Add(action, state, diagnostic);
        return MexcPrivateResult<T>.Fail(state, diagnostic);
    }

    private MexcOrderTestState AuditTest(MexcOrderTestState state,
        MexcDiagnostic? diagnostic = null)
    {
        _audit.Add(MexcPrivateAuditAction.OrderTest, state == MexcOrderTestState.TestReady
            ? MexcPrivateState.Ready : state switch
            {
                MexcOrderTestState.KeyRequired => MexcPrivateState.NotConfigured,
                MexcOrderTestState.KeychainDenied => MexcPrivateState.KeychainDenied,
                MexcOrderTestState.TimeUnsynced => MexcPrivateState.TimeUnsynced,
                MexcOrderTestState.PermissionDenied => MexcPrivateState.PermissionDenied,
                MexcOrderTestState.RateLimited => MexcPrivateState.RateLimited,
                MexcOrderTestState.Unavailable => MexcPrivateState.Unavailable,
                _ => MexcPrivateState.Error
            }, diagnostic);
        return state;
    }

    private MexcProbeExecutionResult ProbeFail(MexcOrderTestState state,
        MexcDiagnostic? diagnostic = null)
    { AuditTest(state, diagnostic); return new(state, null, diagnostic); }

    private static bool IsExactEmptyObject(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                !document.RootElement.EnumerateObject().Any();
        }
        catch (JsonException) { return false; }
    }

    private static MexcOrderTestState MapTest(MexcPrivateState state) => state switch
    {
        MexcPrivateState.NotConfigured => MexcOrderTestState.KeyRequired,
        MexcPrivateState.KeychainDenied => MexcOrderTestState.KeychainDenied,
        MexcPrivateState.TimeUnsynced => MexcOrderTestState.TimeUnsynced,
        MexcPrivateState.PermissionDenied => MexcOrderTestState.PermissionDenied,
        MexcPrivateState.RateLimited => MexcOrderTestState.RateLimited,
        MexcPrivateState.Unavailable => MexcOrderTestState.Unavailable,
        _ => MexcOrderTestState.Error
    };

    private static MexcOrderTestState MapOrderTestError(
        System.Net.HttpStatusCode status, int? code)
    {
        var mapped = MapTest(MapDiagnostic(Diagnose(status, code)));
        return mapped == MexcOrderTestState.Error && (int)status is >= 400 and < 500
            ? MexcOrderTestState.TestRejected : mapped;
    }

    internal static MexcDiagnostic Diagnose(System.Net.HttpStatusCode status, int? code)
    {
        var reason = code switch
        {
            700001 => MexcFailureReason.InvalidApiKeyFormat,
            10072 => MexcFailureReason.InvalidApiKey,
            700002 or 602 => MexcFailureReason.InvalidSignature,
            700003 or 10073 => MexcFailureReason.TimeWindow,
            700006 => MexcFailureReason.IpNotAllowed,
            700007 => MexcFailureReason.EndpointPermissionDenied,
            401 => MexcFailureReason.Unauthorized,
            403 => MexcFailureReason.AccessDenied,
            429 => MexcFailureReason.RateLimited,
            _ when status == System.Net.HttpStatusCode.Unauthorized =>
                MexcFailureReason.Unauthorized,
            _ when status == System.Net.HttpStatusCode.Forbidden =>
                MexcFailureReason.HttpForbiddenUnknown,
            _ when status == System.Net.HttpStatusCode.TooManyRequests =>
                MexcFailureReason.RateLimited,
            _ when (int)status >= 500 => MexcFailureReason.UpstreamUnavailable,
            _ => MexcFailureReason.ProtocolError
        };
        return new((int)status, code, reason);
    }

    private static MexcPrivateState MapError(System.Net.HttpStatusCode status, int? code) =>
        MapDiagnostic(Diagnose(status, code));

    private static MexcPrivateState MapDiagnostic(MexcDiagnostic diagnostic) =>
        diagnostic.Reason switch
        {
            MexcFailureReason.InvalidApiKeyFormat or MexcFailureReason.InvalidApiKey or
                MexcFailureReason.IpNotAllowed or MexcFailureReason.EndpointPermissionDenied or
                MexcFailureReason.Unauthorized or MexcFailureReason.AccessDenied or
                MexcFailureReason.HttpForbiddenUnknown => MexcPrivateState.PermissionDenied,
            MexcFailureReason.TimeWindow => MexcPrivateState.TimeUnsynced,
            MexcFailureReason.RateLimited => MexcPrivateState.RateLimited,
            MexcFailureReason.UpstreamUnavailable => MexcPrivateState.Unavailable,
            _ => MexcPrivateState.Error
        };

    private static MexcPrivateState MapHttp(System.Net.HttpStatusCode status) =>
        status == System.Net.HttpStatusCode.TooManyRequests ? MexcPrivateState.RateLimited :
        status is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
            ? MexcPrivateState.PermissionDenied :
        (int)status >= 500 ? MexcPrivateState.Unavailable : MexcPrivateState.Error;
}
