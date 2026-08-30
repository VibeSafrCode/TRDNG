# PR-01 bounded WebSocket envelope — evidence

Date: 2026-08-28 (+05:00).

## Scope and baseline

- Task: only `PR-01 — bounded WebSocket message envelope` from the independent
  audit plan.
- Working baseline: branch `codex/gpt-pro-audit-request`, HEAD
  `fcebe3403f9b55ab894ea2d8fa6ffdf203122d34`.
- The worktree already contained uncommitted S3.2/S3.3 diagnostics, branding,
  Keychain and documentation work. It was preserved. This PR-01 slice must be
  reviewed and committed separately from that carry-in work.
- Excluded: order-book capacity (PR-02), HTTP bounds, MainViewModel refactor,
  credentials, private requests, `/api/v3/order/test`, production order routes,
  money actions and new venues.

## Implementation

- `BoundedWebSocketMessageReader` uses a fixed-size pooled message buffer and a
  separate bounded receive chunk. It never grows the complete-message storage.
- The hard limit is 1,048,576 bytes for Bybit, Gate and MEXC. This preserves the
  previously accepted MEXC limit and is intentionally much larger than normal
  public depth/trade frames while still preventing unbounded fragmentation.
- Exactly the limit is accepted. Any next non-empty byte is rejected before it
  is copied or parsed.
- Text and binary fragmentation, including empty fragments, is supported. A
  mid-message type change is rejected fail-closed.
- Reject and cancellation clear the accumulator state. The next read cannot see
  the previous partial message.
- Safe failure codes are `WS_MESSAGE_TOO_LARGE` and
  `WS_MESSAGE_TYPE_CHANGED`; they contain no remote payload.
- All three venue clients catch this typed failure, publish `Reconnecting`, and
  their existing `finally` blocks reset the venue session/cluster state before
  the next connection attempt.
- MEXC text-frame diagnostics now retain only the allowlisted event name and byte
  count, not the decoded remote frame.

## Verification

- Targeted test assembly compile: `PASS`, 0 warnings, 0 errors.
- Direct in-process execution of the nine new deterministic scenarios: `9/9
  PASS` (single text, fragmented text/empty, fragmented binary, exact boundary,
  boundary + 1, reset after reject, type change, cancellation/reset and close).
  This is not reported as official VSTest evidence.
- One official full local `dotnet test --no-build --no-restore` attempt:
  `BLOCKED` before test execution by the known sandbox VSTest listener error,
  `SocketException (13): Permission denied`. It was not retried.
- Final full `Trdng.slnx` Release build: `PASS`, 0 warnings, 0 errors.
- One final self-contained `osx-arm64` publish and replacement of the existing
  `artifacts/TRDNG.app`: `PASS`.
- Strict deep ad-hoc codesign verification: `PASS`.
- Packaged `Trdng.Desktop.dll` SHA-256:
  `7044c51b8cc5298a87dfb5e9e31ae7a83f78748da506446f87bf53f5a394725b`.
- Signed packaged executable SHA-256:
  `b6727ede95f65861c3fb712814e91eaf3fff30636e4c8cf31a1dd58360327505`.
- Packaged icon/source ICNS hash match:
  `624c8d81d1440c5e01c7be14cb3ba4aa792ed0745d4529d8b43ccfab4baa2369`.
- Tracked diff check and no-index whitespace checks for all new PR-01/audit text
  files: `PASS`.
- Local Markdown link check: `63` relative links checked, `PASS`.
- PR-01 source/test/audit safety scan: no credential assignment, private-key
  material or production-order endpoint match.
- GUI, live WebSocket, private/authenticated network and money actions: `NOT RUN`.

## Debt and rollback

- `OPEN`: official runtime suite must run in GitHub CI after an accepted isolated
  commit; local VSTest is sandbox-blocked.
- `OPEN`: a real reconnect/resync live smoke was not run. Deterministic tests
  prove envelope behavior; code inspection proves all three clients route the
  typed failure to `Reconnecting` and reset their sessions.
- `OPEN / CARRY-IN DOCUMENT DRIFT`: the current README and uncommitted S3.2/S3.3
  evidence stop at an earlier authenticated acceptance state, while later
  operator results from the preceding sprint still need one factual
  reconciliation pass. PR-01 does not rewrite or certify those unrelated facts.
- `OPEN / OWNER ACTION`: a read-only GitHub check at 2026-08-28 07:49 +05:00
  reported `VibeSafrCode/TRDNG` as `PUBLIC`. No visibility change was made in
  this sprint. Commit/push should remain blocked until the Founder explicitly
  chooses `PRIVATE` or consciously accepts continued public visibility.
- `OPEN / OUT OF SCOPE`: unresolved memory-soak anomaly, unbounded order-book and
  cluster state, bounded public HTTP, lifecycle and secret-input recommendations
  remain later audit PRs.
- `OPEN`: the replaced ignored app bundle was not backed up before packaging.
  This is not a release; rollback of source is the isolated PR-01 diff, while a
  previous binary package would need reconstruction from its accepted revision.
- Rollback PR-01 only: remove the shared reader/test, restore the three venue
  receive loops and restore the previous MEXC text diagnostic. Do not roll back
  unrelated carry-in files.

The accepted implementation is isolated in local commit
`a3435bc` (`fix: bound public WebSocket messages`). The preceding carry-in was
also separated into `b0af3b2` (MEXC diagnostics) and `665e1c4` (branding). At the
time of this evidence update no push, PR, merge, GitHub setting change or release
had been performed.

## Publication update

- Public branch `codex/gpt-pro-audit-request` was fast-forwarded through factual
  documentation commit `8f88ffb`; remote SHA verification: PASS.
- Repository visibility remained `PUBLIC` by explicit Founder decision; default
  branch remained `main` and was not changed.
- No GitHub CI run was created because the workflow triggers only for `main` push
  or pull request. PR, merge and release remain `NOT RUN`.

## Final acceptance update

- Pull request `#6` was independently checked and merged.
- GitHub CI run `33296738736`: Release build PASS; official deterministic suite
  295/295 PASS, 0 failed, 0 skipped.
- Merge commit on `main`:
  `f69d1a1f59c18546d8e5cdaa2683f64caf78f691`.
- Tag, notarization and production release remain NOT RUN.
