# Security policy

## Supported status

TRDNG is pre-release Stage 1 software. Public market data and local simulation are the supported paths. Authenticated MEXC reads and `/api/v3/order/test` have deterministic foundations but no real-key acceptance smoke. Production trading is unsupported and disabled.

## Reporting

Do not open a public issue containing exploit details, account data or secrets. Until the owner configures private GitHub security reporting, contact the owner through an established private channel with minimum reproducible, redacted information. This document intentionally does not invent an email address.

If a credential may be exposed, revoke it immediately at the exchange, remove its Keychain item, review account activity and rotate related credentials. Never paste keys, secrets, signatures, tokens, private payloads or Keychain values into issues, pull requests, chat, logs, screenshots or fixtures.

## Explicit exclusions

- Production order placement, cancellation, withdrawal and transfer.
- Automatic retry of ambiguous private operations.
- Smart routing or simultaneous multi-venue execution.
- MEXC Futures private trading.
- Repository, environment-variable or command-line credential storage.

## Dependencies and disclosure

Review dependency and Action updates through bounded Dependabot pull requests. Security fixes require tests, diff review and evidence. Triage suspected vulnerabilities privately, fix on a review branch, independently audit, and disclose only after the owner chooses timing and affected users have a safe update path.

