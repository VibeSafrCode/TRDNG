# TRDNG architecture

TRDNG is a small .NET monorepo. Core owns venue-neutral invariants, venue projects translate official exchange contracts, and Desktop composes the macOS UI. Dependencies point inward.

```mermaid
flowchart LR
  UI["Trdng.Desktop"] --> CORE["Trdng.Core\nbooks, risk, simulation"]
  UI --> BYBIT["Trdng.Bybit"]
  UI --> GATE["Trdng.Gate"]
  UI --> MEXC["Trdng.Mexc"]
  BYBIT --> CORE
  GATE --> CORE
  MEXC --> CORE
  TESTS["Trdng.Core.Tests"] --> CORE
  TESTS --> BYBIT
  TESTS --> GATE
  TESTS --> MEXC
```

## Data and trust boundaries

```mermaid
flowchart TD
  PUB["Public REST/WS"] --> ADAPTERS["Venue parser + sequence lifecycle"]
  ADAPTERS --> BOOKS["Separate normalized books"]
  BOOKS --> VIEW["Freshness-gated cards + comparison"]
  KEYCHAIN["macOS login Keychain"] --> LEASE["Bounded SecretLease"]
  LEASE --> RO["MEXC signed GET\naccount/openOrders"]
  LEASE --> TEST["POST /api/v3/order/test\nvalidation only"]
  STOP["Atomic STOP + owner gate"] --> TEST
  META["Fresh official metadata + tiny cap"] --> TEST
  TEST --> EVIDENCE["Bound evidence\nnot production authorization"]
  PROD["POST /api/v3/order\ncancel/withdraw/transfer"]:::forbidden
  classDef forbidden fill:#3a1118,stroke:#ff6677,color:#fff;
```

Public data contains no credentials. Read-only and order-test MEXC credentials use separate Keychain identities. Secrets are leased briefly and excluded from UI, audit and journals. Redirects, cookies and unsafe retries are disabled. The test path allows only `/order/test`; the production route is absent.

Every public WebSocket connection passes complete text and binary messages through
one fixed-size pooled envelope before any venue parser runs. The shared hard limit
is 1 MiB. Fragmented messages are counted cumulatively; an oversized or
type-inconsistent message is rejected with a stable safe code, the partial buffer
is reset, and the venue session follows its existing reconnect/resynchronization
path. Raw WebSocket payloads are not included in that error or in MEXC text-frame
diagnostics.

All public REST metadata, catalog and MEXC depth-snapshot responses pass through
one bounded streaming reader before JSON parsing. It performs a declared-length
precheck and reads at most one byte beyond an endpoint-specific success cap;
error bodies and unexpected media types are not parsed or logged. The production
public client has a five-second timeout with redirects and cookies disabled.
Current caps are 4 MiB per Bybit instrument page, 8 MiB for Gate contracts and
MEXC exchange info, and 2/8 MiB for MEXC depth 1,000/5,000 respectively.

Normalized order books are bounded by a venue-configured
`OrderBookCapacityPolicy`. Snapshots validate into replacement state; deltas
validate their complete change set and projected side counts/cross before any
mutation. Policy failure never truncates a book: the venue session clears state
and requires resynchronization. MEXC also bounds the total number of levels held
before its REST snapshot bridge. Current trade-cluster intervals have independent
price-level and trade-count caps; an overflowed partial interval is suppressed
and reported through core metrics.

## State and lifecycle

- Selection is canonical `asset + product`, generation-scoped and latest-request-wins; old callbacks cannot populate a new selection.
- Each book owns snapshot/delta continuity and resync. Warning data may compare; stale data is excluded.
- Dry-run intent is immutable and targets exactly one venue. STOP starts engaged; confirmation is exact, expiring and single-use.
- Simulation is journaled locally. Ambiguous recovery becomes `Unknown/RequiresReconciliation` and never retries automatically.
- Probe evidence binds candidate and canonical wire-body fingerprints but cannot become production-filter or S4 authorization.

## Sources of truth

- [Stage 1 plan](stage-1-plan.md)
- [Stage 1 ledger](stage-1-ledger.md)
- [Canonical document index](source-of-truth.md)
- [Security policy](../SECURITY.md)
- [PR-03 bounded HTTP evidence](pr03-bounded-http-evidence.md)
