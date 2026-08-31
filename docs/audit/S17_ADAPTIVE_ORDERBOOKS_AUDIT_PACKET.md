# S1.7 adaptive order books — independent audit packet

Review the exact uncommitted diff on branch `codex/adaptive-orderbooks` against
baseline `b7b0e7060f4c00d7fcb072d78f02dfb59be2ee9e`. Do not mutate the repository,
use credentials, call private APIs or execute an order.

## Founder goal

The terminal must default to BTC, show non-empty independent books for MEXC,
Gate and Bybit, use all available vertical space on large monitors, and let the
user tune each book separately with a trackpad and a gear. The visible ask and
bid sides normalize independently: yellow/red for ordinary/largest asks and
blue/green for ordinary/largest bids, with four editable colors per venue.

## In scope

- adaptive full-height book layout and spread-row clipping protection;
- independent depth, trackpad step, volume reference and palette per venue;
- BTC startup/test default;
- public MEXC USDT perpetual catalog and bounded REST book adapter;
- deeper bounded snapshots for all three venues;
- Gate per-entry catalog rejection instead of whole-venue failure;
- deterministic tests, package evidence and documentation closure.

## Explicit exclusions

- private/authenticated APIs, credentials, `/order/test`, orders and money;
- merged liquidity, smart routing and automated trading;
- persistence of display settings;
- Git commit/push/PR/merge, tag, notarization or release.

## Review questions

1. Can any resize/depth combination produce clipped rows, sub-readable text,
   excessive collections or overlap with the spread strip?
2. Are ask and bid visible maxima truly independent and are manual references
   fail-closed and bounded?
3. Do lifecycle cancellation and latest-generation guards prevent stale MEXC
   REST callbacks after selection switches or disposal?
4. Does the MEXC contract parser reject malformed, crossed, duplicate,
   oversized or unsupported data without guessing symbols/units?
5. Is 750 ms bounded public polling acceptable for this MVP, and what measured
   criterion should gate a later WebSocket replacement?
6. Did the Gate per-entry policy or three-venue capability change create a
   fail-open route anywhere outside public market data?

Evidence: [S1.7 local evidence](../s1.7-adaptive-orderbooks-evidence.md).
Canonical plan: [Stage 1 plan](../stage-1-plan.md).
Architecture: [ARCHITECTURE.md](../ARCHITECTURE.md).
Security boundary: [SECURITY.md](../../SECURITY.md).
