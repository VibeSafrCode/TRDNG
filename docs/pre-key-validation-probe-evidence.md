# Pre-key validation probe — evidence

Status: ACCEPTED / PRIVATE REQUEST NOT RUN
Date: 2026-08-19
Accepted local commit: `10ca5166b66172bc68599b37e13ab43e2778be98`
Worktree after commit: clean; GitHub/push: not performed

## User result

TRDNG can derive a tightly bounded, no-execution MEXC Spot validation candidate
without weakening production `OrderFilterSet`. BUY uses documented quote min/max;
SELL uses documented base minimum plus a fresh executable price and a hard local
quote cap. Missing per-symbol proof remains an explicit caveat and never becomes
production capability.

The separate owner token is exact, expiring and single-use. STOP, stale metadata,
stale reference price, missing order-test credentials or unsynchronized time fail
closed. A successful future `/api/v3/order/test` produces only exact
`OrderTestValidatedEvidence`; it cannot authorize S4 or create a production-order
authorization. There is no retry and no production order route.

## Official provenance

Official MEXC Spot V3 documentation checked 2026-08-19:
`https://mexcdevelop.github.io/apidocs/spot_v3_en/`.

The documented contract says `/api/v3/order/test` validates but does not send an
order to the matching engine; MARKET accepts `quantity` or `quoteOrderQty`, with
BUY quote amount and SELL base amount semantics. The endpoint still requires a
separate `SPOT_DEAL_WRITE` key. No private or authenticated call was made.

## Verification

- Targeted test-assembly compile: PASS; runtime VSTest NOT RUN (known IPC blocker).
- Final Release solution build: PASS, 0 errors; one `NU1900` warning because the
  sandbox could not reach the NuGet vulnerability index.
- One self-contained `osx-arm64` publish replaced the existing
  `artifacts/TRDNG.app`; strict codesign: PASS.
- Publish/app `Trdng.Mexc.dll` SHA-256 match:
  `ad2ecd2e5458b551804b659a84af4ea41246f32f9c37a848c0d81435c44f50da`.
- Signed app executable SHA-256:
  `d162fc6ff20b31b3028af2116c1316ba93d103e2a5a8b16692c8cad43f2c5c99`.
- GUI/visual acceptance: NOT RUN (known permission debt).
- Real keys, private network, money and S4: NOT USED / ABSENT.
- Independent repeat audit: ACCEPTED. VSTest runtime, GUI and authenticated
  private probe remain NOT RUN.

## Independent-audit corrections

- Probe execution and confirmation now read one injected atomic kill-switch;
  there is no caller-supplied STOP boolean. Every execution attempt consumes the
  authorization before checking STOP, so a rejected attempt cannot be replayed.
- A 2xx response creates evidence only when its bounded body is exactly an empty
  JSON object. Empty/HTML/array/non-empty object responses fail closed.
- Prepare/confirm/invalidate are linearized by one lock; generated owner tokens
  have an allowlisted bounded format, TTL overflow fails closed, and concurrent
  confirmation can produce at most one authorization.
- Evidence distinguishes the immutable candidate fingerprint from the SHA-256
  fingerprint of the exact canonical order-test body actually sent (excluding
  API key, secret and signature).

## Owner action

Create four separate generic-password items using the system Keychain Access GUI
per `mexc-keychain-provisioning.md`. Keep read-only and order-test identities
separate. The order-test key must be no-withdrawal and preferably IP-bound. Then
the Founder must explicitly approve the exact symbol, side and bounded value for
one single-use `/order/test` probe. That future result remains validation evidence
only, never permission for `/api/v3/order`.
