# PR-04 memory observability and bounded soak — evidence

Date: 2026-08-30 (+05:00).

## Scope and baseline

- Task: only `PR-04 — memory observability and bounded soak` from the imported
  independent audit plan.
- Baseline: accepted `main` at `8e9c1b9`; working branch
  `codex/memory-observability-soak`.
- Included: bounded local counters, a credential-free deterministic replay,
  lifecycle/selection churn, allocation budgets, a macOS runbook and one
  15-minute exact-package diagnostic soak.
- Excluded: remote telemetry, credentials, private/authenticated requests,
  `/order/test`, production orders, money actions, visual redesign and the
  later two-hour release soak.

## Implementation

- `RuntimeMemoryRecorder` retains a configured bounded chronological sample
  window. Samples contain managed heap, LOH, total allocation counter, GC
  counts, RSS/working set, optional process-private memory and thread count.
- Missing macOS private-memory evidence is represented as `null`, never as a
  measured zero. Apple's `footprint` and `vmmap` remain authoritative for the
  native process.
- `MemorySoakBudget` produces only stable allowlisted failure codes and checks
  managed peak, working-set peak, optional private-memory peak, retained managed
  growth and allocation bytes per applied update.
- `Trdng.MemorySoak` uses no network or credentials. It deterministically
  replays bounded snapshots, deltas and clusters through three independent
  books while switching Spot/Perpetual selections and disposing local fake
  clients.
- Local output is bounded JSONL containing counters only. No market payload,
  API material, user data or remote telemetry is emitted.

## Deterministic verification

- Targeted test assembly compile: `PASS`, 0 warnings, 0 errors.
- One official local VSTest attempt: `BLOCKED` before runtime by the known
  sandbox IPC `SocketException (13)`; no retry.
- Full Release solution build, including the soak tool: `PASS`, 0 warnings,
  0 errors.
- One-million-cycle replay: `PASS`, exit 0.
  - cycles: 1,000,000;
  - applied book updates: 3,003,000;
  - market switches: 1,000;
  - fake clients created/disposed: 1,500 / 1,500;
  - maximum simultaneously active clients: 2;
  - maximum levels per side: 256;
  - maximum completed clusters: 9;
  - retained memory samples: 12;
  - peak managed heap: 6,775,480 bytes;
  - peak RSS/working set: 59,441,152 bytes;
  - retained managed growth: 478,736 bytes;
  - allocation rate: 2,391 bytes per applied update;
  - process-private memory: unavailable from `.NET` on this macOS run and
    recorded as `null`.

## Package and real-Mac diagnostic

- One self-contained `osx-arm64` publish updated only the existing ignored
  `artifacts/TRDNG.app`; strict deep ad-hoc codesign: `PASS`.
- Packaged `Trdng.Core.dll` SHA-256:
  `da061dd6039cd8cf2ad7764dd0a00684412cb5cc2e9d0836a6a32271572ce9ce`.
- Signed executable SHA-256:
  `c5a369f42f87b6b485186c82854636b1c99932fcf45d06cae8cdad798aaf1f5e`.
- Exact app PID: `7946`; one process was observed. Public-data UI and manual
  window resize were exercised; credentials/private/order actions were not.
- Initial sample near 10 seconds: RSS 174,016 KiB; footprint 187,812,928 bytes;
  physical footprint 187,845,696 bytes; recorded peak 200,002,624 bytes.
- Initial `vmmap`: 3.5 GiB virtual, 341.8 MiB resident, 39.5 MiB dirty,
  124.9 MiB swapped; 7,102 regions. Virtual size is not treated as RAM.
- The process completed 15 minutes 40 seconds. Samples were sparse during the
  first six minutes and then taken every ten seconds through 14:39; Apple's
  final report also supplied the process-wide peak footprint.
- Final RSS: 74,224 KiB; final footprint: 204,033,152 bytes; physical footprint:
  204,065,920 bytes; peak physical footprint: 220,843,136 bytes.
- Final `vmmap`: 3.5 GiB virtual, 316.8 MiB resident, 32.3 MiB dirty,
  159.2 MiB swapped and 7,228 regions. Footprint growth from the initial sample
  was 16,220,224 bytes; swapped growth was approximately 34.3 MiB. Sampled RSS
  fell from 174,016 KiB initially to 74,224 KiB finally.
- Exactly one app process remained responsive during manual resize. No crash,
  duplicate process, emergency threshold or monotonic physical growth was
  observed. Exact PID `7946` was terminated with `SIGTERM` after measurement and
  absence was verified.
- Classification: `PASS_15_MIN`. This is explicitly not the two-hour release
  gate.

## Product observations outside PR-04

The owner found three separate product issues during the manual observation.
They are recorded for the next visual/public-market-data sprint and do not
change this exact package during the soak:

- each venue book must fill the available vertical space on large monitors;
- each book needs independent fine-grained depth control, including a vertical
  two-finger trackpad gesture centered on the spread;
- a gear in each book's spread row must expose per-book auto/manual depth,
  gesture step and bar-scale controls; manual bar scale uses an explicit
  reference volume, four independent colors and a reset to the owner's default
  palette;
- automatic bar width uses the largest currently visible volume in that book as
  100%; normal asks are yellow and normal bids blue, while the largest visible
  ask is red and the largest visible bid green;
- Gate and MEXC must not remain empty when the selected exact market is
  available; startup and test selection should default to BTC rather than APT.

## Debt and next gate

- `PENDING`: GitHub official deterministic runtime suite after independent
  review and publication.
- `PASS_15_MIN`: exact signed app stayed far below every emergency threshold.
- `OPEN`: two-hour public-data release soak, GUI responsiveness, reconnect and
  market-switch coverage remain a later release gate.
- `OPEN`: backup, notarization, tag and release were not run.
