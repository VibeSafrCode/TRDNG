# Performance and runtime-safety contract

TRDNG is a Mac-first scalping terminal. Stability, response time, resource
efficiency, operational safety and trader comfort are acceptance dimensions for
every sprint, not a one-time optimization phase.

## Non-negotiable runtime budgets

For the current three-book app on every supported Mac:

| Gate | Owned process-tree physical memory | Action |
|---|---:|---|
| Healthy target | <= 512 MiB | Continue and record evidence |
| Warning | 1.5 GiB | Capture bounded diagnostics; reduce render cadence |
| Soft stop | 2.25 GiB for 3 x 10 s | Stop public feeds and shut down gracefully |
| Hard stop | 3 GiB on any sample | TERM, then KILL after 5 s |

The 3-GiB ceiling is hardware-independent until the Founder explicitly changes
it. A future 64-GiB Mac adds headroom; it does not authorize more retention.
The older 8-GiB absolute invariant remains a final unreachable safety assertion,
not an operating limit.

The external supervisor also stops TRDNG below these limits if system free
memory becomes critical or macOS swap-out counters grow materially during the
run. Normal operation and tests must not rely on swap.

## Bounded market-data to UI path

- One latest snapshot is retained per logical book; publishing replaces it.
- Any number of producer updates coalesces to one pending dirty bit.
- The desktop consumes at most 10 render updates per second, independent of
  exchange producer frequency.
- No per-update `Dispatcher.UIThread.Post` closure may capture a snapshot.
- Snapshot depth is capped at 200 levels per side.
- The current UI mutates existing row view models in place. Collection members
  are only added/removed when visible depth changes.
- The scalable foundation retains at most 100 logical latest snapshots, allows
  at most 12 simultaneously visible books, and caps the visible global row
  budget at 1,200 rows across both sides.
- Historical snapshots, diagnostics and metrics require explicit bounded
  capacities. Missing evidence is never represented as zero.
- A three-venue atomic market switch starts no replacement client before
  commit. It may briefly retain at most three old plus three fully constructed
  but unstarted replacements; steady-state started clients remain at most three.
  Every untransferred replacement is disposed in a `finally` path.

## Layered protection

1. Source-level latest-wins state, fixed render cadence and row reuse prevent
   queue and visual-tree growth.
2. An independent in-process timer warns at 1.5 GiB, soft-stops at 2.25 GiB
   sustained and hard-stops at 3 GiB.
3. `scripts/run-macos-trdng-guarded.zsh` is mandatory around every macOS GUI QA
   or soak. It verifies the exact target hash, owns a PID file and complete child
   tree, samples RSS/physical footprint/system pressure/swap, captures bounded
   diagnostics and always cleans up.
4. GitHub CI runs restore/build/tests/replay in a Docker cgroup with equal memory
   and memory-swap limits. Lack of Docker support fails the job explicitly.
5. A global user LaunchAgent guardian is deferred. It must not be installed
   until independent review proves exact identity matching, protected-app
   exclusions, recovery and user-visible logging.

### Decision matrix

| Option | Prevents | Limitation / risk | Decision |
|---|---|---|---|
| Source-level latest-wins pipeline | Dispatcher backlog and repeated visual-tree allocation | Cannot contain an unrelated native/runtime failure by itself | Required and implemented |
| In-process circuit breaker | A live app that crosses the warning/soft/hard policy | Shares the failing process and cannot be the sole guard | Required and implemented |
| Exact-process external watchdog | Runaway app/tree, system pressure and swap growth even if the UI hangs | Must prove exact identity/tree ownership and safe cleanup | Required for every macOS GUI QA/soak |
| CI cgroup envelope | Unbounded deterministic build/test/replay resource use | Linux/headless evidence does not replace macOS GUI evidence | Required and configured |
| Global LaunchAgent guardian | Could supervise future unattended launches | Broad targeting/recovery risk; unnecessary for the present controlled workflow | Deferred |

The accepted strategy is the first four layers together. No individual layer is
treated as sufficient, and the deferred guardian must not be installed as an
implicit workaround.

## Scale evidence

Synthetic tiers are 3, 6, 12, 24, 48 and 100 logical books. Each tier must
record GC heap, allocations, retained snapshot count, pending render work (must
remain 0 or 1), configured maximum cadence, CPU and bounded duration. Only
visible books perform row mutation. The structural probe is network-free, so its
reconnect count is explicitly synthetic zero. One outer watchdog observes the
combined process-tree footprint; per-tier native footprint and effective GUI
cadence are proved only by the later guarded macOS app gates. External
connection counts are not increased merely to prove the memory model.

The final 2026-08-31 headless acceptance run processed 1,000,000 replay cycles
and 3,003,000 applied book updates. Peak working set was 59,850,752 bytes;
1,600 clients were created and all 1,600 were disposed. The deterministic
two-venue switch observed exactly four simultaneously constructed-but-not-yet-
disposed clients (two old plus two staged/unstarted). Every
scale tier passed with one pending render item maximum, one retained latest
snapshot per logical book, no reconnects, no more than 1,200 visible rows and a
64,389,120-byte observed working set during the tier probe. Each tier performed
12,000,000 in-place synthetic row mutations. These are structural bounded-
foundation facts, not Avalonia bindings, wall-clock 10-Hz cadence or macOS GUI-
soak evidence.

A tier stops immediately on an unbounded upward trend, critical system pressure
or material swap growth. No reproduction intentionally approaches 3 GiB, 8 GiB
or OOM.

## Continuous sprint review

Every meaningful change reviews memory, CPU, allocations/GC, queue depth,
backpressure, UI latency/render cadence, exchange response/reconnect latency,
request efficiency, rate limits, long-run stability and safe shutdown. Claims
need measured baselines and regression thresholds.

Private trading and large-money work additionally requires a threat model,
credential isolation, no-withdrawal keys, idempotency, reconciliation, kill
switch, hard limits, bounded audit trail and rollback evidence.
