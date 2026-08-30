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
- working set peak: 768 MiB;
- process private memory when the runtime exposes it: 2 GiB;
- retained managed growth after forced final collection: 64 MiB;
- allocation rate: 32 KiB per applied book update;
- memory samples retained in-process: 256 maximum.

On macOS, `.NET Process.PrivateMemorySize64` can be unavailable and is then
recorded as `null`, never as a proven zero. `footprint` and `vmmap` are the
authoritative native measurements.

## Real macOS app gate

Preconditions:

1. Use the exact signed package hash recorded in the sprint evidence.
2. Confirm no existing app process. Do not run Computer Use or Screen Recording
   automation during the soak.
3. Do not open credential forms or invoke read-only/private/order-test actions.
4. Public catalogs/books may connect. No raw market payload is recorded.
5. Capture one `vmmap -summary` at start and end. Sample RSS/VSZ every 10 seconds
   and process physical footprint with Apple's `footprint` tool.

The first bounded gate is 15 minutes. It is diagnostic acceptance, not the
two-hour release gate. Provisional emergency-stop thresholds for the target M1
Mac with 8 GiB RAM are deliberately conservative:

- physical footprint reaches 1.5 GiB;
- RSS reaches 1 GiB;
- process swapped/compressed memory grows by 512 MiB;
- physical footprint grows by 256 MiB over a five-minute window without
  returning toward baseline;
- a second app process appears;
- the UI becomes unresponsive or the process crashes.

If a threshold is hit, capture one final `footprint`/`vmmap` sample, send
`SIGTERM` only to the exact app PID and classify the gate `BLOCKED`. Never wait
for the historical 3.4 GiB footprint or a misleading 40+ GiB VSZ display.

## Classification

- `PASS_15_MIN`: exact package completes 15 minutes; no threshold, crash,
  duplicate process or monotonic native growth. This does not equal a two-hour
  release pass.
- `BLOCKED`: any emergency threshold or measurement failure that prevents a
  trustworthy conclusion.
- `NOT RUN`: app never reached a stable measurable process.

The later release gate remains a two-hour public-data soak with separately
accepted thresholds, GUI responsiveness evidence and reconnect/market-switch
coverage. Keep raw native tool output local; repository evidence contains only
aggregated, non-sensitive measurements.
