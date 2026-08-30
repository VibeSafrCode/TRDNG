# Independent review packet — PR-03 bounded public HTTP

## Reviewer task

Review exact implementation commit `89e645e` on branch
`codex/bounded-public-http` against accepted baseline `e3f5cb7`.
Implementation evidence is in
[`../pr03-bounded-http-evidence.md`](../pr03-bounded-http-evidence.md).

This is PR-03 / HTTP-001 from
[`TRDNG_FRESH_AUDIT_CODEX_PLAN_2026-08-25.md`](TRDNG_FRESH_AUDIT_CODEX_PLAN_2026-08-25.md).
It is not authorization for PR-04, private API work or production trading.

## Files in review scope

- `src/Trdng.Core/MarketData/BoundedHttpContentReader.cs`;
- `src/Trdng.Core/MarketData/PublicHttpTransport.cs`;
- Bybit, Gate and MEXC public metadata/catalog clients;
- MEXC public REST depth-snapshot path;
- the production public `HttpClient` construction in `MainViewModel`;
- `BoundedHttpContentReaderTests`;
- architecture, evidence and factual ledgers for this slice.

## Required findings

Classify findings P0/P1/P2 and explicitly check:

1. Can a declared, chunked, fragmented or lying response retain/read more than
   `maximum + 1` bytes before rejection?
2. Is exact-boundary input accepted and is the pool returned on every exception
   or cancellation path?
3. Can an oversized/error response body or unexpected media type reach a parser,
   exception, diagnostic or UI string?
4. Are the four endpoint cap families compatible with their official request
   bounds, especially MEXC depth 1,000 versus 5,000?
5. Does every current public metadata/catalog/snapshot `HttpClient` byte path use
   the shared reader, while private MEXC remains unchanged?
6. Are timeout, fixed endpoints, no redirect, no cookies, page count and JSON
   depth sufficient and fail closed? Is a project-wide explicit JSON depth worth
   a follow-up or a blocker?
7. Do content encoding, missing/variant JSON Content-Type or HTTP status handling
   introduce a compatibility or denial-of-service regression?
8. Do the tests prove early `Content-Length` rejection, streamed `max + 1`, no
   retry, cancellation and actual venue wiring without allocating oversized
   fixture bodies?

## Evidence summary

- Targeted test assembly compile: PASS, 0 warnings/errors.
- One official local VSTest attempt: BLOCKED before runtime by known sandbox IPC;
  no retry.
- Full Release solution build: PASS, 0 warnings/errors.
- Package/codesign: PASS. GUI/live/private/auth/order/money: NOT RUN.
- GitHub CI: pending branch publication and pull request.

Explicit exclusions: visual behavior, credentials, private endpoints,
`/api/v3/order/test`, production order/cancel/withdraw/transfer routes, money
actions, new venues, reconnect policy, MainViewModel refactor and PR-04 soak.
