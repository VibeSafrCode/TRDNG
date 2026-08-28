# Independent review packet — PR-01 bounded WebSocket envelope

## Reviewer task

Review only PR-01 implementation commit `a3435bc` plus its closure documents,
described in
[`../pr01-bounded-websocket-evidence.md`](../pr01-bounded-websocket-evidence.md).
The preceding branch commits `b0af3b2` and `665e1c4` are separate carry-in
changesets. Do not attribute them to PR-01.

Baseline:

- branch: `codex/gpt-pro-audit-request`;
- HEAD: `fcebe3403f9b55ab894ea2d8fa6ffdf203122d34`;
- audit source:
  [`TRDNG_FRESH_AUDIT_CODEX_PLAN_2026-08-25.md`](TRDNG_FRESH_AUDIT_CODEX_PLAN_2026-08-25.md);
- selected item: PR-01 only.
- implementation commit: `a3435bc`.

## Files in review scope

- `src/Trdng.Core/MarketData/BoundedWebSocketMessageReader.cs`;
- `src/Trdng.Bybit/MarketData/BybitPublicOrderBookClient.cs`;
- `src/Trdng.Gate/MarketData/GatePublicMarketDataClient.cs`;
- `src/Trdng.Mexc/MarketData/MexcPublicOrderBookClient.cs`;
- `tests/Trdng.Core.Tests/MarketData/BoundedWebSocketMessageReaderTests.cs`;
- `docs/ARCHITECTURE.md`;
- `docs/pr01-bounded-websocket-evidence.md`;
- this packet and the audit index link.

## Required findings

Please classify issues as P0/P1/P2 and explicitly check:

1. Can any fragmented text/binary message allocate or copy beyond 1 MiB before
   rejection, including integer overflow and empty-fragment cases?
2. Can a partial/oversized message reach any venue parser or survive into the
   next message?
3. Is the returned pooled memory lifetime safe for the synchronous parser usage
   in all three clients?
4. Does cancellation/disposal return pooled arrays exactly once and preserve
   existing client lifecycle behavior?
5. Does every typed reject cause reconnect plus venue-session reset without raw
   payload in state, exception, diagnostic or UI?
6. Did the MEXC diagnostic change remove raw text without breaking the existing
   live-smoke troubleshooting contract?
7. Are the nine deterministic tests sufficient, and what smallest missing test
   would materially improve confidence?

Explicit exclusions: do not add trading, credentials, private requests, public
HTTP bounds, order-book capacity, memory soak or MainViewModel refactoring to
this review. Those are separate audit PRs.

## Evidence summary

- New deterministic in-process scenarios: 9/9 PASS.
- Full Release solution build: PASS, 0 warnings/errors.
- Official local VSTest: BLOCKED before execution by sandbox IPC; no retry.
- Package/codesign: PASS; GUI/live network NOT RUN.
- Secret/private/order/money actions: none.
- Read-only repository check on 2026-08-28: `PUBLIC`; no setting was changed.
- Unrelated carry-in README/S3.2/S3.3 factual reconciliation remains open and is
  not evidence for or against PR-01.
- Implementation commit: created locally; push/PR/merge/release were not yet
  performed when this packet was prepared.
