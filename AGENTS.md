# TRDNG repository operating invariants

TRDNG is a Mac-first scalping terminal intended to outperform Windows alternatives
in stability, response time, resource efficiency, operational safety and trader
comfort. Performance and security are acceptance dimensions of every sprint.

## Runtime and scale

- Never launch `TRDNG.app` or an Avalonia QA/soak process directly. Use
  `scripts/run-macos-trdng-guarded.zsh` with the exact expected executable hash.
- Current three-book healthy target: no more than 512 MiB for the owned process
  tree. Warning: 1.5 GiB. Soft stop: 2.25 GiB sustained for three ten-second
  samples. Hard stop: 3 GiB on any sample, followed by TERM and KILL. The 3-GiB
  cap is hardware-independent unless the Founder explicitly changes it.
- Stop earlier on critical macOS memory pressure or material swap-out growth.
  TRDNG must not depend on swap for normal operation or tests.
- Market data to UI is latest-wins: one retained current snapshot per book, one
  bounded dirty gate and a render cadence of at most 10 Hz. Never enqueue one UI
  closure per market event or retain unbounded historical snapshots.
- Cap snapshots at 200 levels per side. Render only visible information, reuse
  rows in place and keep explicit per-book, visible-row and global budgets.
- Scale synthetic tiers at 3/6/12/24/48/100 logical books before adding a dense
  multi-book UI. Record footprint, GC heap, pending queue count, render cadence,
  CPU/reconnect facts and stop on an unbounded trend.

## Every meaningful change

Review and record bounded memory, CPU, allocations/GC, queue depth/backpressure,
UI latency/render cadence, exchange response/reconnect latency, request
efficiency, rate-limit behavior, long-run stability and safe shutdown. Define a
baseline and regression threshold; never claim optimization without evidence.

Use coalescing, pooling/reuse, virtualization and visibility-aware rendering.
Keep optimization work bounded to the approved sprint rather than expanding the
product scope.

## Security and money boundary

Before private trading or large-money functionality, require a threat model,
credential isolation, no-withdrawal keys, idempotency, reconciliation, kill
switch, hard limits, bounded audit trail and rollback evidence. Never include
secrets, credentials, signatures, private payloads or PII in source, logs, docs
or audit packets.

Keep sanitized independent-review material in `docs/audit/`. Audit recommendations
are inputs to triage, not authority to mutate the product automatically.
