# PR-02 bounded order-book and cluster memory — evidence

Date: 2026-08-30 (+05:00).

## Scope and baseline

- Task: only `PR-02 — bounded order book and cluster memory` from the imported
  independent audit plan.
- Baseline: merged `main` at `f69d1a1f59c18546d8e5cdaa2683f64caf78f691`;
  working branch `codex/bounded-orderbook-memory`.
- Excluded: visual changes, public HTTP limits, UI flow refactors, private API,
  credentials, order-test, production orders, money actions and new venues.

## Implementation

- `OrderBookCapacityPolicy` bounds levels per side, entries per update and an
  optional maximum price. Venue clients configure the cap from their subscribed
  contract: Bybit selected depth, Gate 50, MEXC synchronized REST depth.
- Snapshot input is completely validated into replacement dictionaries before
  publication. Delta input is validated and projected against current counts and
  best prices before any mutation. Violations are never silently truncated.
- Duplicate prices, invalid levels, price-limit breaches, side overflow and
  crossed books produce stable payload-free policy codes. Rejected updates do
  not advance sequence IDs or change levels.
- Bybit, Gate and MEXC sessions enter an explicit resynchronization path after a
  policy violation and clear the current book.
- MEXC pre-snapshot storage now has both a delta-count cap and a total buffered-
  level cap. Each buffered delta is policy-validated before retention.
- `TradeClusterAggregator` bounds unique prices and trade count in the current
  interval. Overflow clears the partial bucket, suppresses its projection, and
  exposes bounded counters/state; the next interval starts cleanly.
- Delta preflight allocates only bounded change maps. It does not clone the
  entire 1,000/5,000-level book on every update.

## Verification

- Targeted test assembly compile: `PASS`, 0 warnings, 0 errors.
- Added deterministic boundary, no-partial-mutation, crossed/duplicate,
  venue-resync, MEXC buffered-level, cluster-overflow and seeded randomized
  scenarios. Runtime execution is `NOT RUN` locally: a temporary in-process
  runner stalled before execution while restoring its temporary project and was
  cancelled; it was not retried.
- One official local full VSTest attempt: `BLOCKED` before tests by the known
  sandbox MSBuild/VSTest IPC `SocketException (13)`; no retry.
- Final full `Trdng.slnx` Release build: `PASS`, 0 warnings, 0 errors.
- One self-contained `osx-arm64` publish updated the existing ignored app only.
  Strict deep ad-hoc codesign: `PASS`.
- Packaged `Trdng.Core.dll` SHA-256:
  `91c10efed8dfd9cbcb7cfaa36cea62fc42f36abae518aa30f3896af8b39893c8`.
- Signed packaged executable SHA-256:
  `75da43b19cae768db5ff51336f009d240c762734931b1631224dba45672e9450`.
- GUI, live network, private/authenticated requests and money actions: `NOT RUN`.

## Debt and next gate

- `OPEN`: independent review of exact PR-02 implementation commit `c7f3ce0`.
- `OPEN`: official deterministic runtime suite must pass once in GitHub CI after
  accepted commit/pull request.
- `OPEN`: PR-04 still owns the real Mac memory soak and agreed footprint limits;
  bounded collections reduce the attack surface but do not prove stable RSS.
- `OPEN`: cluster overflow metrics are core-only and not yet surfaced in UI or
  telemetry; no visual behavior was changed in this bounded sprint.
- `OPEN`: PR-03 still owns bounded public HTTP response reading.
- Rollback: revert only the PR-02 implementation/evidence changes. PR-01 merge
  `f69d1a1` remains the baseline.

Implementation and closure evidence are isolated in local commit `c7f3ce0`
(`fix: bound order book and cluster memory`). Push, pull request, merge, tag,
notarization and release have not been performed at the time of this update.
