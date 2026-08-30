# Independent review packet — PR-02 bounded market-data memory

## Reviewer task

Review the exact uncommitted diff on branch `codex/bounded-orderbook-memory`
against merged baseline `f69d1a1f59c18546d8e5cdaa2683f64caf78f691`.
Implementation evidence is in
[`../pr02-bounded-memory-evidence.md`](../pr02-bounded-memory-evidence.md).

This is PR-02 from
[`TRDNG_FRESH_AUDIT_CODEX_PLAN_2026-08-25.md`](TRDNG_FRESH_AUDIT_CODEX_PLAN_2026-08-25.md),
not an authorization to begin PR-03 or trading work.

## Files in review scope

- `src/Trdng.Core/MarketData/OrderBookCapacityPolicy.cs`;
- `src/Trdng.Core/MarketData/OrderBookEngine.cs`;
- `src/Trdng.Core/Clusters/TradeClusterAggregator.cs`;
- Bybit/Gate/MEXC order-book sessions and public client policy wiring;
- the corresponding order-book/session/cluster tests;
- `docs/ARCHITECTURE.md`;
- PR-02 evidence, ledger and this packet.

## Required findings

Classify findings P0/P1/P2 and explicitly check:

1. Can snapshot, delta or buffered MEXC input exceed a configured count before
   rejection or remain retained after resync/disconnect?
2. Does every policy failure leave prices, quantities, sequence IDs and session
   state unmodified except for the explicit resync/reset transition?
3. Are projected side counts and best bid/ask correct for add, update and delete
   combinations, including an update that deletes the previous best?
4. Can duplicate prices, integer/count overflow, invalid decimal values or a
   maximum-price boundary bypass the policy?
5. Do Bybit/Gate/MEXC use caps compatible with the actually subscribed/snapshot
   depth without confusing UI capture depth with synchronization depth?
6. Can a cluster interval exceed either price-level or trade count, publish a
   partial overflowed bucket, or retain overflowed levels after rollover/reset?
7. Do the seeded randomized tests exercise enough add/delete/overflow paths, and
   what smallest additional property materially improves confidence?
8. Did the implementation introduce avoidable per-delta whole-book allocation,
   blocking, visual behavior or private/trading scope?

## Evidence summary

- Targeted test assembly compile: PASS, 0 warnings/errors.
- Added runtime scenarios: compiled; local runtime NOT RUN due the documented
  temporary-runner restore stall and sandbox VSTest IPC blocker.
- Full Release solution build: PASS, 0 warnings/errors.
- Package/codesign: PASS; GUI/live/private/auth/order/money NOT RUN.
- GitHub CI: pending accepted commit and pull request.

Explicit exclusions: public HTTP bounds, memory soak thresholds, MainViewModel
backpressure/refactor, secret-input changes, authenticated endpoints and any
production order path.
