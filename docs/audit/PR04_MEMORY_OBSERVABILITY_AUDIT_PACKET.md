# Independent review packet — PR-04 memory observability and soak

## Reviewer task

Review the exact PR-04 branch diff against accepted `main` baseline `8e9c1b9`.
Implementation evidence is in
[`../pr04-memory-soak-evidence.md`](../pr04-memory-soak-evidence.md) and the local
operator boundary is in
[`../memory-soak-runbook.md`](../memory-soak-runbook.md).

This is PR-04 / memory observability from
[`TRDNG_FRESH_AUDIT_CODEX_PLAN_2026-08-25.md`](TRDNG_FRESH_AUDIT_CODEX_PLAN_2026-08-25.md).
It is not authorization for private API work, production trading or a release.

## Files in review scope

- `src/Trdng.Core/Diagnostics/RuntimeMemoryObservation.cs`;
- `src/Trdng.Core/Diagnostics/DeterministicMarketDataReplay.cs`;
- `tools/Trdng.MemorySoak/**`;
- `tests/Trdng.Core.Tests/Diagnostics/MemoryObservabilityTests.cs`;
- solution wiring, runbook, evidence, architecture and factual ledgers.

## Required findings

Classify findings P0/P1/P2 and explicitly check:

1. Are every recorder, book, cluster, client and output collection bounded?
2. Can an invalid option, overflow, cancellation or failed selection leave a
   client alive, corrupt sample order or misreport a pass?
3. Are missing native measurements represented honestly rather than as zero?
4. Are retained-growth and allocation-rate calculations meaningful and
   overflow-safe for the configured ten-million-cycle maximum?
5. Does the deterministic replay exercise book replacement/delta, clusters,
   market switches and disposal without network or credentials?
6. Can JSONL output contain market payload, credentials, user data, exception
   text or an unbounded diagnostic value?
7. Are the deterministic harness budgets distinct from real-app emergency stop
   thresholds, and can either be mistaken for release acceptance?
8. Does the macOS evidence support only `PASS_15_MIN`, `BLOCKED` or `NOT RUN`,
   with a two-hour release soak still explicitly open?
9. Are the newly observed adaptive-depth, empty-book and BTC-default product
   issues correctly excluded from this unchanged exact-package measurement?

## Current evidence summary

- Targeted test assembly compile: PASS, 0 warnings/errors.
- One official local VSTest attempt: BLOCKED before runtime by known sandbox
  IPC; no retry.
- Full Release solution build: PASS, 0 warnings/errors.
- One-million-cycle credential-free replay: PASS under every configured harness
  budget.
- Package/codesign: PASS. Exact signed app completed 15 minutes 40 seconds with
  one process: final/peak physical footprint 204,065,920 / 220,843,136 bytes,
  final RSS 74,224 KiB and only about 34.3 MiB swapped growth. Classification:
  `PASS_15_MIN`, not a two-hour release pass.
- Private/authenticated/order/money actions: NOT RUN.

Explicit exclusions: visual redesign, production telemetry, credentials,
private endpoints, `/api/v3/order/test`, order/cancel/withdraw/transfer routes,
money actions, new venues, tag, notarization and release.

## Superseding acceptance

- Exact commit `8f0eab7` passed pull request `#9` CI run `33302487008`:
  Release build PASS with 0 warnings/errors and the official deterministic suite
  327/327 PASS.
- Pull request `#9` merged to `main` as
  `2e7d9218c2db462bd0b45ec9f372462b1945cd00`.
- The 15-minute diagnostic is accepted only as `PASS_15_MIN`; the two-hour
  release soak and the deferred visual/public-data sprint remain open.
