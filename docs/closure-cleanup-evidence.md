# S1.7 and cleanup closure evidence

Date: 2026-08-31; updated 2026-09-10. Branch:
`codex/adaptive-orderbooks`. Status:
`IMPLEMENTATION + CI ACCEPTED / FINAL PACKAGE GUI + SOAK BLOCKED_ENVIRONMENT / 120M NOT RUN`.

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
- Current signed executable SHA-256:
  `07a2dd98b5353ed7159db15981765575155d0f68248fe33c0bbecebbd46b204f`.
- Current packaged/publish `Trdng.Desktop.dll` SHA-256:
  `304db925690b671645401a0d01b92416212fb6ae323ded869935b825b33ab062`.
- Historical predecessor package `1d93a3...` guarded five-minute run:
  `PASS_DURATION`, peak physical
  footprint 191,515,776 bytes, swap growth 0, cleanup PASS.
- The following `1d93a3...` 15-minute attempt stopped after 6m25s on
  `SYSTEM_SWAP_GROWTH`; app peak/final footprint was
  199,281,728/190,204,992 bytes and cleanup PASS. Classification:
  `BLOCKED_ENVIRONMENT`, not app growth.
- Fresh visual QA on predecessor package `6474cdf...` later became available.
  It proved BTC default,
  three populated `LIVE` books, large/full-screen fill and all three settings
  panels. The broken flyout was replaced by a bounded inline overlay. A clipped
  largest ask explained the incorrect sales-side scale; a symmetric 12-pixel
  spread reservation corrected it on all three venues.
- The predecessor `6474cdf...` visual-stress run is not memory evidence:
  Computer Use/full-screen work
  raised physical footprint to 819,040,448 bytes without swap growth. The clean
  no-Computer-Use `6474cdf...` 15-minute gate passed with peak/final footprint
  230,296,768/211,405,952 bytes and zero swap delta.
- Accessibility correction commit `9af7e0beb64a8bee7d311048ecde21f3519b01e5`
  is pushed. CI `34470736716` passed Release build, 367/367 tests and the bounded
  one-million-cycle replay.

## Open gates, not hidden debt

- Screenshot/large-window/settings/populated-book acceptance is `PASS` on
  predecessor exact package `6474cdf...`. Computer Use did not synthesize the
  exact trackpad wheel
  path, so only that GUI gesture remains `NOT PROVEN`; deterministic adjustment
  tests pass.
- Predecessor package `6474cdf...` quiet-host guarded 15-minute gate is `PASS`.
  Three 30-minute attempts stopped
  on `SYSTEM_SWAP_GROWTH`: 2026-08-31 after 5m10s with peak/final footprint
  207,949,120/205,081,856 bytes; 2026-09-10 after approximately 20 seconds with
  163,417,024 bytes; and a controlled retry after 10 seconds with 152,030,208
  bytes. All runs removed their owned process. Between the 2026-09-10 attempts,
  an operator-observed no-app baseline recorded the unchanged counter
  `39228865` in twelve samples from `10:30:09Z` through `10:31:59Z`; it is not
  claimed as a watchdog artifact. The 30-minute gate is `BLOCKED_ENVIRONMENT`;
  the two-hour release gate remains `NOT RUN`.
- The P2 accessibility debt is closed in current code head `9af7e0b`: Escape and
  backdrop close, cyclic Tab navigation and prior-focus restoration. Independent
  audit found no P0/P1/P2; local 367/367, full Release build and GitHub CI pass.
  Current-hash guarded GUI attempt stopped on host swap after 10 seconds before
  visual input, at 158,485,440 bytes app footprint; cleanup PASS.
- Closure implementation commit:
  `fb252bfa0afda8f57d51202d74baeb29e8954d79`. Earlier verified code/CI head
  `83e92ba4aca685abc21888cb24317a2c611eb39d` and its bundle are retained but
  superseded. Current verified code/CI head is
  `9af7e0beb64a8bee7d311048ecde21f3519b01e5`; its terminal-only Git bundle passed
  verification, isolated clone, strict fsck, exact HEAD and clean worktree
  checks. Exact artifact identity and retention are recorded in
  [`recovery-restore-evidence.md`](recovery-restore-evidence.md). The verified
  pre-separation bundle remains historical only.
- PR #10 is updated. The first closure run `33392273068` exposed a CI-only
  tmpfs-capacity error before compilation; the independently audited correction
  preserved the 2 GiB/no-swap envelope and moved only package/tool caches to
  job-scoped runner temp. Corrected run `33392591048`: Release build PASS,
  367/367 tests PASS and one-million-cycle replay PASS. Merge, tag and release
  remain blocked until current-package GUI and soak gates pass or the Founder
  explicitly accepts a documented waiver.

## Security boundary

No credentials were read, printed or changed. No authenticated/private request,
`/order/test`, production order, withdrawal, transfer or money action ran in
this closure. S3.3, new exchanges and screener work were not started.
