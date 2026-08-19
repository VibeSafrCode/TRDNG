# MEXC credentials — secure local provisioning runbook

Status: pre-key documentation only, 2026-08-19. This runbook does not ask for,
contain, transmit or create credentials. Do not paste an API key or secret into
chat, terminal commands, environment variables, files, screenshots, clipboard
history or shell history.

## Exact Keychain namespace

TRDNG stores four independent generic-password items in the user's local macOS
login Keychain:

- service: `com.trdng.desktop.credentials.v1`
- account: `mexc:readonly-api-key`
- account: `mexc:readonly-secret`
- account: `mexc:order-test-api-key`
- account: `mexc:order-test-secret`

The app uses Security.framework directly. Until an audited in-app entry flow is
available, create the items only in the system **Keychain Access** GUI. There is
intentionally no `security` CLI command, shell example, config file, environment
variable or command-line argument in this runbook: all of those can leak secrets
into process lists, history, logs or backups.

## Keychain Access — GUI-only procedure

1. Open **Keychain Access** from Spotlight or Applications → Utilities.
2. Select the user's **login** keychain and the Passwords category.
3. Choose **File → New Password Item** (called a generic password item by the
   Keychain API).
4. Set the Keychain Item Name/service exactly to
   `com.trdng.desktop.credentials.v1`. Set Account Name to exactly one account
   from the list above. Enter its value only in the password field and save.
5. Repeat the operation until four separate items exist—one per exact account.
   Never combine API key and secret in one item.
6. Verify only the visible service/item name and account labels in the list or
   Get Info. Do not enable “Show password”, copy the value, take a screenshot of
   it, or expose it while asking for support.

Creating these four entries does not authorize TRDNG to make a private request.
The later owner gate must still explicitly enable the exact single-use probe.

## Permission separation

The read-only pair is only for S3.2 account/open-orders reads. Give it only the
minimum official read permissions required (`SPOT_ACCOUNT_READ` and
`SPOT_DEAL_READ`); withdrawal, transfer and trading permissions stay disabled.

The order-test pair is separate because MEXC requires `SPOT_DEAL_WRITE` even for
`POST /api/v3/order/test`. That endpoint validates a request but does not create
an order. The key is nevertheless trade-enabled: withdrawal and transfer must be
disabled, an IP allowlist should be set where available, and it must never be
reused as the S3.2 read-only key.

No authenticated smoke is authorized by this document. After the secure-entry
UI exists, the Founder must separately approve the exact symbol, limits and one
`/order/test` attempt. Production `/api/v3/order` remains absent and forbidden.

## Safe preparation checklist

1. Create two different MEXC API profiles: read-only and order-test.
2. Disable withdrawal and transfer on both profiles.
3. Apply an IP allowlist where MEXC supports it.
4. Verify the read-only profile has no write permission.
5. Verify the order-test profile has only the minimum Spot trade permission
   required by MEXC plus no withdrawal.
6. Create the four separate items through Keychain Access as described above,
   then stop. The Founder must open the next explicit gate before any private
   read or `/order/test` validation probe.
