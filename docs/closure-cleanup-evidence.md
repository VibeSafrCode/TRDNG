# S1.7 and cleanup closure evidence

Date: 2026-08-31. Branch: `codex/adaptive-orderbooks`. Status:
`IMPLEMENTATION ACCEPTED / RELEASE BLOCKED BY ENVIRONMENT GATES`.

## Closed implementation and hardening

- S1.7 independent order books retain BTC/USDT startup, three exact public
  venue mappings, independent depth/trackpad/volume/color settings and the
  owner palette.
- The P0 GUI-memory defect is contained by latest-wins snapshots, one bounded
  render gate, 10 Hz UI cadence, in-place rows, bounded books/clusters and both
  in-process and exact-process memory guards.
- Public catalog refresh runs after ten minutes, expires at fifteen minutes,
  preserves a proven catalog on partial/failing refresh, bootstraps from a later
  partial success, and transactionally rebuilds or fail-closes active clients
  when official symbol/tick/multiplier metadata changes.
- Per-venue book settings persist in a bounded versioned local JSON document.
  Writes are latest-wins, unique-temp atomic, disk-flushed off the UI thread and
  awaited by the normal cancellable shutdown path. Invalid state restores
  defaults.
- MEXC public perpetual polling was measured with the production client: 20/20
  sequential BTC snapshots in 26.982 seconds, 44.4737 polls/minute, no
  reconnect. It is accepted only as the bounded public-data MVP; it is not an
  execution-grade transport decision.
- CI no longer uses deprecated setup-dotnet. It uses a reviewed full-SHA
  checkout action and a digest-pinned .NET SDK container with a 2 GiB/no-swap
  cgroup, bounded test timeout and deterministic replay.

## Verification

- Independent P0/P1 implementation audit: `PASS`; no remaining P0/P1 in the
  catalog transaction, shutdown writer or memory-containment patch.
- Final local Release solution build: `PASS`, 0 warnings, 0 errors.
- First full acceptance run was 366/367: the replay correctly observed four
  constructed clients during atomic two-venue staging while the legacy test
  expected at most two. Independent audit confirmed the bounded semantics;
  the test was strengthened to exact four. Final full official local suite:
  `367/367 PASS`, 0 failed, 0 skipped; the strengthened exact regression test
  passed again after compilation.
- Final deterministic replay: 1,000,000 cycles; 3,003,000 applied updates;
  1,600 clients created/1,600 disposed; memory evaluation `PASS`.
- Self-contained `osx-arm64` publish and strict deep ad-hoc codesign: `PASS`.
  The first publish process exited 1 without output and produced zero files;
  the app was still untouched. One controlled retry with build-server reuse
  disabled completed successfully before the single app replacement.
- Signed executable SHA-256:
  `1d93a3a074aa0bfdf36e5a49091a9b1acf9d51ecaf2790678fa3de4ba6b25e90`.
- Packaged/publish `Trdng.Desktop.dll` SHA-256:
  `c5c65e792fd58c91f7c1fe6a609bc8ce89f6d061083c300afe566257a6b9b7b3`.
- Guarded five-minute exact-package run: `PASS_DURATION`, peak physical
  footprint 191,515,776 bytes, swap growth 0, cleanup PASS.
- The following 15-minute attempt stopped after 6m25s on
  `SYSTEM_SWAP_GROWTH`; app peak/final footprint was
  199,281,728/190,204,992 bytes and cleanup PASS. Classification:
  `BLOCKED_ENVIRONMENT`, not app growth.

## Open gates, not hidden debt

- Screenshot/large-window/settings visual acceptance is
  `BLOCKED_ENVIRONMENT`: one capture attempt failed because Screen Recording
  was unavailable; no TCC workaround or retry was used.
- Quiet-host guarded 15-, 30- and 120-minute gates remain required. The
  30-minute and two-hour runs were not started after the 15-minute prerequisite
  stopped.
- Current terminal-only Git bundle/isolated restore is pending the accepted
  implementation commit. The already verified pre-separation bundle is
  historical only and is not represented as current recovery.
- PR #10 publication and one GitHub CI run are pending. Merge, tag and release
  remain blocked until the visual and soak gates pass or the Founder explicitly
  accepts a documented waiver.

## Security boundary

No credentials were read, printed or changed. No authenticated/private request,
`/order/test`, production order, withdrawal, transfer or money action ran in
this closure. S3.3, new exchanges and screener work were not started.
