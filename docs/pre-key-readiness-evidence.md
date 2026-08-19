# Pre-key readiness — official MEXC metadata and passive preflight

Date: 2026-08-19. Independent audit accepted the slice; accepted local commit:
`93f67922d7a932412107efb53f98567698a7860b`. Worktree was clean after the
commit; no GitHub push was performed.

## Official contract and one public smoke

Only the official [MEXC Spot V3 documentation](https://mexcdevelop.github.io/apidocs/spot_v3_en/)
and one credential-free `GET https://api.mexc.com/api/v3/exchangeInfo?symbols=APTUSDT,BTCUSDT`
smoke were used on 2026-08-19.

Documented order-test-relevant fields are parsed with their published meanings:
symbol status, `isSpotTradingAllowed`, `orderTypes`, optional
`quoteOrderQtyMarketAllowed`, `baseSizePrecision` (minimum base quantity),
`quoteAmountPrecisionMarket` (minimum MARKET quote amount),
`maxQuoteAmountMarket`, and `tradeSideType`. `quotePrecision` remains display
price precision and is not mapped to an order filter.

The single live payload returned both APTUSDT and BTCUSDT enabled with MARKET,
Spot allowed, side type 1, minimum base quantity and MARKET quote min/max. It did
not contain `quoteOrderQtyMarketAllowed`. It also did not document/provide a
MARKET maximum base quantity or base step. Therefore:

- BUY QuoteNotional remains `NeedsMetadata` until quote-order-quantity support is
  explicitly present and true in an official payload.
- SELL BaseQuantity remains `NeedsMetadata` because the official contract cannot
  prove all required min/max/step fields. `baseSizePrecision` is not relabeled as
  a step.

No defaults or inferred precision/filter values are used.

## Implementation

- Extended the metadata model/parser only with documented nullable fields.
- Added a side-specific fail-closed mapper to `OrderFilterSet`.
- Added a passive preflight model/presentation containing exact symbol/product/
  side/value, official source and freshness, risk cap, STOP, separate read-only
  and order-test Keychain profile states, and eligibility. Its action is always
  disabled in this slice.
- Preflight rejects stale, missing, mismatched, disabled, unsupported and
  incoherent metadata before credentials.
- Added [`mexc-keychain-provisioning.md`](mexc-keychain-provisioning.md) with the
  exact service/accounts and permission separation, without secret-bearing CLI,
  env, file or chat examples.

## Evidence and open gates

- Deterministic sanitized fixtures cover APT/BTC fields, missing quote support,
  independent BUY/SELL mapping, disabled/incoherent states, stale/missing/
  mismatched metadata, risk/STOP/profile presentation and passive action state.
- Targeted test assembly compile: PASS, 0 errors. Runtime VSTest: NOT RUN (known
  IPC blocker; not retried).
- Final Release solution build: PASS, 0 errors; one sandbox-only `NU1900`
  because NuGet vulnerability metadata was unreachable.
- Final self-contained `osx-arm64` publish and replacement of the single existing
  app: PASS. Strict deep codesign verification: PASS.
- Publish/app `Trdng.Mexc.dll` SHA-256 match:
  `89c84f609cc6e03692a453c9f127c836306d2b9c9df9606b6dfedbee5bc793d7`.
- Signed app executable SHA-256:
  `43d5a85ca49d3893b29cbfc45d002a53b911eda6d0cb6a3f7f3e1f13651ebc0c`.
- GUI, authenticated smoke, real credentials, private network and money: NOT RUN.

This slice cannot make `/order/test` eligible from the observed official live
metadata because required evidence is absent. That is an accepted fail-closed
result, not a guessed product restriction. Independent audit is accepted. No
owner action is needed until a later secure-entry/authenticated gate.
