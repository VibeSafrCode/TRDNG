# P0 GUI memory incident — containment and correction evidence

Date: 2026-08-31 (+05:00). Status: `P0/P1 FIX AUDIT PASS`; final package and
five-minute guarded gate PASS. Screenshot acceptance is environment-blocked;
the 15-minute gate stopped on host-wide swap growth and the 30-minute/two-hour
release gates were not started.

## Confirmed incident facts

- The primary TRDNG task launched the exact ad-hoc signed package
  `artifacts/TRDNG.app` for the authorized public-data soak.
- Host evidence identifies `Trdng.Desktop` PID `41117`, bundle
  `com.trdng.terminal`, at the repository package path.
- Executable SHA-256:
  `84a68a4bf460885bac170fd82a4b0ed6f8843c7f456635c9a32667b87e5427a8`.
  Desktop DLL SHA-256:
  `467f9b7c706bcfc7adeefbaccaaa956d0ee1ff59d3d0489bf797d2dd223f9c9d`.
- The owner screenshot reported `Avalonia Application (paused)` at 33.32 GB.
  Unified host logs recorded macOS memory-pressure level 4 and repeated HTTPS
  activity by PID `41117` around the same minute.
- The process had already exited before containment capture; PID/PPID/start
  evidence beyond the host log is unavailable and is not guessed. Subsequent
  `ps`/`pgrep` checks confirmed no TRDNG, Avalonia or memory-soak process.
- No authenticated/private request, order or money action was involved.

## Confirmed causal defect

The live desktop registered up to one book callback every 75 ms for Bybit and
Gate, a MEXC REST snapshot about every 750 ms, plus state/cluster callbacks.
Every callback posted a new closure to `Dispatcher.UIThread`; snapshot closures
retained their complete snapshot. Each closure rebuilt all three books and
cleared/re-added all six `ObservableCollection` row lists.

There was no queue bound, producer backpressure, global render cadence or
in-place row reuse. When the fullscreen UI/accessibility inspection became slow
and timed out, network producers continued. This is a proven architecture-level
path for unbounded retained dispatcher work and visual-tree churn. A heap dump
was not captured, so the exact split between managed queued closures and native
Avalonia/Skia retention remains unmeasured.

Rapid reconnects are a possible multiplier, not a proven independent root
cause. Static inspection currently finds one reconnect task per client,
generation-scoped event handlers and bounded HTTP/WebSocket readers. Duplicate
subscriptions, timers, client accumulation and buffer retention remain explicit
audit questions.

## Why the earlier replay passed

The one-million-cycle deterministic replay was headless, network-free and
completed in about five seconds. It exercised bounded engines, clusters and
client lifecycle but not Avalonia, `Dispatcher.UIThread`, visual controls or
live reconnect timing. Its ~60 MiB peak RSS therefore did not cover the failing
GUI path.

## Correction and safe validation

- High-frequency callbacks now replace only latest fields and set a single
  `BoundedRenderUpdateGate` bit. A UI timer consumes at most 10 Hz.
- Book rows are updated in place; add/remove occurs only when visible depth
  changes.
- Explicit snapshot/visible/global scaling budgets are code-level invariants.
- In-process and external memory guards implement the staged 1.5/2.25/3-GiB
  policy and the external guard also checks system pressure/swap growth.
- Release solution build passed with 0 warnings and 0 errors.
- The full official local deterministic suite passed: 358/358, 0 failed,
  0 skipped. The diagnostics subset passed: 23/23.
- The final guarded headless replay processed 1,000,000 cycles, 3,003,000
  applied book updates and 1,000 market switches. Peak managed heap was
  7,118,416 bytes, peak working set 59,850,752 bytes, retained managed growth
  479,160 bytes, and the budget result was PASS. The corrected scale run
  performed 12,000,000 in-place synthetic row
  mutations per tier; it remains structural evidence, not an Avalonia render
  measurement.
- Synthetic 3/6/12/24/48/100-book tiers all passed. Each retained exactly one
  latest snapshot per logical book, observed at most one pending render item,
  rendered no more than 1,200 visible rows and stayed at 65,339,392 bytes
  working set during the corrected tier probe. The guarded target exited cleanly with
  `PASS_TARGET_EXIT`; no PID file remained.
- Dummy watchdog soft and hard tests used only ~87 MiB and ~171 MiB physical
  footprint. Soft sustained shutdown removed the complete tree. The hard probe
  ignored TERM; the supervisor escalated to KILL and verified both owned PIDs
  absent. No swap/memory pressure was induced.
- Independent review found and blocked two watchdog escape paths before any GUI
  relaunch: reparented descendants and synchronous diagnostic commands delaying
  containment. The corrected guard starts the exact hashed target in an isolated
  process session, retains PID+start identities, rejects orphan clean-exit,
  requires an atomic lock and fails closed on missing measurements. TERM/KILL
  containment now precedes lightweight incident capture; a final bounded
  identity check rejects surviving owned processes.
- Corrected controlled outcomes: `PASS_DURATION`, `ORPHAN_AFTER_ROOT_EXIT`,
  `SOFT_LIMIT_SUSTAINED`, `HARD_LIMIT`, `MEASUREMENT_FAILED` and
  `DURATION_SHUTDOWN_KILL_ESCALATED` all followed their expected pass/fail path;
  no probe PID or guard lock remained.

## Final closure package gates

The final signed package executable is
`1d93a3a074aa0bfdf36e5a49091a9b1acf9d51ecaf2790678fa3de4ba6b25e90`;
packaged `Trdng.Desktop.dll` is
`c5c65e792fd58c91f7c1fe6a609bc8ce89f6d061083c300afe566257a6b9b7b3`.

- Five-minute guarded run `20260831T115550Z-75048`: `PASS_DURATION`, 30
  samples, peak RSS 205,029,376 bytes, peak physical footprint 191,515,776
  bytes, minimum free memory 30%, swap-out delta 0, no live owned PID after
  cleanup.
- Fifteen-minute attempt `20260831T120129Z-82067`: watchdog stopped after
  6m25s with `SYSTEM_SWAP_GROWTH`. Across 39 samples, app peak RSS was
  221,724,672 bytes, peak footprint 199,281,728 bytes and final footprint
  190,204,992 bytes; minimum free memory was 31%. System swap-out delta was
  11,956 pages. No live owned PID remained. Classification:
  `BLOCKED_ENVIRONMENT`, not an application-memory regression.
- The 30-minute and two-hour gates were not started after the failed 15-minute
  prerequisite. Thresholds were not weakened and the run was not immediately
  retried.

One earlier two-hour attempt on the preceding package was also stopped by
system swap while the app footprint stayed flat. A quiet-host rerun remains a
release prerequisite; it is not replaced by headless evidence.

The independent audit and watchdog bypass review passed before TRDNG was
relaunched. Validation advances through guarded 5-, 15- and 30-minute runs
before the two-hour release soak. Headless PASS does not substitute for
Avalonia/native visual evidence.
