# TRDNG memory soak runbook

This runbook is local-only operational guidance. It does not enable remote
telemetry, private API calls, order submission or money actions.

## Deterministic replay gate

Run from the repository root:

```sh
./.tools/dotnet/dotnet run \
  --project tools/Trdng.MemorySoak/Trdng.MemorySoak.csproj \
  -c Release --no-build --no-restore -- \
  --cycles 1000000 --switch-every 1000 \
  --sample-every 100000 --book-depth 256
```

The harness uses no network and no credentials. It replays bounded snapshots,
deltas and clusters while switching deterministic Spot/Perpetual selections.
JSON lines contain process counters and structural counts only. Exit `0` means
the harness budgets passed; exit `2` means one or more allowlisted budget codes
failed.

Current harness budgets are intentionally separate from app release budgets:

- managed heap peak: 256 MiB;
- working set peak: 512 MiB;
- process private memory when the runtime exposes it: 1 GiB;
- retained managed growth after forced final collection: 64 MiB;
- allocation rate: 32 KiB per applied book update;
- memory samples retained in-process: 256 maximum.

On macOS, `.NET Process.PrivateMemorySize64` can be unavailable and is then
recorded as `null`, never as a proven zero. `footprint` and `vmmap` are the
authoritative native measurements.

## Real macOS app gate

Preconditions:

1. Build/sign the package, record the exact executable SHA-256, then launch it
   only through `scripts/run-macos-trdng-guarded.zsh`. A direct `open`, apphost,
   IDE or Computer Use launch is forbidden.
2. The watchdog must confirm no existing run, own the PID file and exact child
   tree, and use its immutable normal profile. `--test-profile` refuses every
   target except the harmless tracked dummy probe.
   A stale `artifacts/qa-diagnostics/trdng-guard.lock` is never taken over
   automatically. Inspect its recorded owner PID/start time and confirm the
   entire prior owned tree is absent before manually removing only that exact
   stale lock; otherwise stop.
3. Do not run Computer Use or Screen Recording
   automation during the soak.
4. Do not open credential forms or invoke read-only/private/order-test actions.
5. Public catalogs/books may connect. No raw market payload is recorded.

Example after replacing `<sha256>` with the independently verified executable
hash:

```sh
scripts/run-macos-trdng-guarded.zsh \
  --duration 300 --expected-sha <sha256> -- \
  artifacts/TRDNG.app/Contents/MacOS/Trdng.Desktop
```

Validation advances 5 minutes, 15 minutes, 30 minutes and only then two hours.
Each earlier run must show flat memory and responsive UI before the next. The
watchdog uses process-tree RSS and physical footprint and applies:

- healthy target <= 512 MiB;
- warning at 1.5 GiB with bounded diagnostics and reduced in-app cadence;
- soft stop at 2.25 GiB for three consecutive ten-second samples;
- hard stop at 3 GiB on any sample, TERM then KILL after five seconds;
- earlier stop if system free memory is critical or swap-out activity grows
  materially during the run;
- an 8-GiB absolute invariant that is never used as an operating target.

The 3-GiB hard cap is unchanged on larger Macs. The watchdog samples bounded
process-tree RSS and `phys_footprint`, system pressure and swap counters. On a
threshold it contains the owned tree before writing lightweight identity and
policy evidence; it deliberately does not run potentially blocking `vmmap` in
the TERM-to-KILL path. Evidence stays in the ignored
`artifacts/qa-diagnostics/` directory. It never targets an unrelated process.

## Classification

- `PASS_15_MIN`: exact package completes 15 minutes; no threshold, crash,
  duplicate process, swap growth or monotonic native growth. This does not equal
  a two-hour release pass.
- `BLOCKED`: any emergency threshold or measurement failure that prevents a
  trustworthy conclusion.
- `NOT RUN`: app never reached a stable measurable process.

The later release gate remains a two-hour public-data soak with separately
accepted thresholds, GUI responsiveness evidence and reconnect/market-switch
coverage. Keep raw native tool output local; repository evidence contains only
aggregated, non-sensitive measurements.
