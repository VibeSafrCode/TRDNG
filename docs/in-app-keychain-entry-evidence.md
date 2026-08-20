# In-app Keychain entry — implementation evidence

Date: 2026-08-20. Status: `IMPLEMENTED / SECURITY AUDIT OPEN`.

## User result and boundary

- The existing MEXC API card now contains two visually separate profiles:
  `READ-ONLY` and `ORDER TEST`.
- Each profile accepts an API key and secret and writes only its existing exact
  Keychain identity pair through `AuditedCredentialVault`.
- `ORDER TEST` is explicitly labelled as requiring trade permission and no
  withdrawal permission. It remains separate from read-only credentials.
- Saving and two-step revoke require the trusted application STOP state.
- No authenticated request, order-test call, production order, clipboard,
  environment, file-based credential storage or real credential was used.

## Fail-closed behavior

- A pair is shown as stored only when both Keychain items report `Stored`.
- Empty/partial/oversized input is rejected before the native vault boundary.
- The second item is never attempted after a first-item failure.
- A second-item failure or exception triggers best-effort removal of both items;
  a partial pair is never reported usable.
- Replacement is explicit. The checkbox warns that the old pair is removed
  before the new pair is written and a failed replacement can leave the profile
  not configured.
- UI-bound strings and mutable UTF-8 buffers are cleared after every attempt.
  The UI framework necessarily holds immutable input strings briefly in memory.
- Audit events contain only action, validated identity, masked state and
  allowlisted code; vault reason and credential values are not retained.

## Verification

- New deterministic contract tests cover successful pair storage, field clearing,
  first/second store failure, rollback, explicit replacement, mixed-pair state,
  STOP and two-step revoke, profile isolation and audit redaction.
- Targeted test assembly compile: PASS, 0 warnings, 0 errors.
- Targeted VSTest runtime: NOT RUN; the known local IPC socket denial occurred.
  The one full official test run is intentionally deferred to GitHub after the
  independent security audit.
- Final Release solution build: PASS, 0 warnings, 0 errors.
- One self-contained `osx-arm64` publish updated the existing app only.
- Publish/app `Trdng.Desktop.dll` SHA-256 match:
  `c203406a68f98632ae6898af8cd892585d811b981aa10c060a3cdbc9706212d4`.
- Signed app executable SHA-256:
  `af2876abebf7efb52f998929d32315ab744227c1f2bc5b770c3c95217c9e1919`.
- Strict deep codesign verification: PASS. GUI/real-key smoke: NOT RUN.

Audit corrections additionally recheck STOP after the second Keychain store and
rollback both items if it changed, mask API-key fields as well as secret fields,
keep revoke controls coherent with STOP, and restore passive masked private-path
status indicators.

## Open gates

- Independent security review and GitHub official full test run remain open.
- The diff is deliberately uncommitted. No remote write was performed.
