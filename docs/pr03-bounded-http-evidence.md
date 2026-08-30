# PR-03 bounded public HTTP — evidence

Date: 2026-08-30 (+05:00).

## Scope and baseline

- Task: only `PR-03 — bounded public HTTP responses` from the imported
  independent audit plan.
- Baseline: accepted `main` at `e3f5cb7`; working branch
  `codex/bounded-public-http`.
- Exact implementation commit: `89e645e` (`fix: bound public HTTP responses`).
- Excluded: visual behavior, private MEXC requests, credentials, `/order/test`,
  production order routes, money actions, new venues and the PR-04 memory soak.

## Implementation

- `BoundedHttpContentReader` uses `ResponseHeadersRead`, checks declared
  `Content-Length`, rents one fixed `maximum + 1` buffer, stops on the single
  discriminator byte and never reads or logs the remaining oversized body.
- Stable payload-free failures distinguish declared oversize, streamed oversize
  and unexpected media type. Non-success response bodies are not read at all.
- JSON success responses require `application/json`, `text/json` or a `+json`
  media type. Existing `JsonDocument` parsing remains bounded by the runtime
  default depth; no permissive comment or trailing-comma option was introduced.
- The production public client has a five-second timeout and an explicit
  `SocketsHttpHandler` with redirects and cookies disabled.
- Endpoint-specific success caps:
  - Bybit single metadata/catalog page: 4 MiB;
  - Gate USDT contracts response: 8 MiB;
  - MEXC exchange info: 8 MiB;
  - MEXC depth snapshot: 2 MiB at depth 1,000; 8 MiB at depth 5,000.
- Fixed exchange endpoints and Bybit's pre-existing 20-page cursor bound remain
  unchanged. Private MEXC keeps its separate accepted reader and was not
  refactored.

## Verification

- Targeted test assembly compile: `PASS`, 0 warnings, 0 errors.
- Fourteen new deterministic cases cover exact-boundary and fragmented reads,
  `Content-Length` precheck, chunked `max + 1`, empty/error/media-type paths,
  unsafe limits, cancellation, no retry, production handler policy, all three
  metadata clients and both MEXC depth limits. Local runtime execution is
  `NOT RUN`: the single official VSTest attempt was blocked before tests by the
  known sandbox IPC `SocketException (13)` and was not retried.
- Final full `Trdng.slnx` Release build: `PASS`, 0 warnings, 0 errors.
- The first publish process waited behind an orphaned compile process created in
  this sprint and produced no files. After read-only PID/path identification,
  only that process was terminated. The single effective self-contained
  `osx-arm64` publish then passed and updated the existing ignored app.
- Strict deep ad-hoc codesign: `PASS`.
- Packaged `Trdng.Core.dll` SHA-256:
  `4eaa90c921c539ea3ccae25853b1240fd784ddde61cbf0f42403b3f63fe369ac`.
- Signed packaged executable SHA-256:
  `91026b011e7b55376dec773471be3a542d8ee8aa4685a9a6a90279c3d25e4a9c`.
- GUI, live network, private/authenticated calls, orders and money actions:
  `NOT RUN`.

## Debt and next gate

- `PASS`: first PR CI `33299901029` ran 323 tests and exposed two failures in
  test instrumentation only: the fake `MemoryStream` counted each virtual read
  twice. Production code was unchanged; test-only commit `2a0c938` replaced it
  with a non-dispatching counted stream.
- `PASS`: final PR CI `33299985960` completed the Release build and the official
  deterministic suite: 323/323 passed, 0 failed, 0 skipped.
- `OPEN`: PR-04 still owns the real-Mac RSS/footprint soak and stop thresholds.
- `OPEN / P2`: JSON parsing uses the bounded .NET default maximum depth rather
  than one shared explicit project constant. This does not leave depth
  unbounded, but an auditor may recommend standardizing the value later.
- `OPEN`: package backup, GUI, live endpoint compatibility, notarization, tag
  and release were not run.
- Rollback: revert implementation commit `89e645e`; PR-01 and PR-02 remain the
  accepted baseline.

## Publication acceptance

- Implementation commit: `89e645e`; test-only correction: `2a0c938`.
- Pull request [#8](https://github.com/VibeSafrCode/TRDNG/pull/8) merged to
  `main` as `8ba6fbfb2a15ee2a8f9fb2a6d4fbdf4f2991fdf7` after final CI PASS.
- The working tree was clean after fast-forwarding local `main` to
  `origin/main`. Tag, notarization and production release remain `NOT RUN`.
