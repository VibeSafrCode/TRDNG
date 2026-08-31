# Recovery restore evidence

Date: 2026-08-31. Status: `PASS / CURRENT TERMINAL IMPLEMENTATION COMMIT`.

## Current terminal-only recovery artifact

- Covered verified code/CI head: `83e92ba4aca685abc21888cb24317a2c611eb39d`.
  It includes implementation commit
  `fb252bfa0afda8f57d51202d74baeb29e8954d79`, the recovery-doc record and the
  independently audited CI tmpfs correction.
- Repository-relative locator (ignored, local-only):
  `artifacts/backups/trdng-terminal-closure-83e92ba.bundle`.
- Created and verified at `2026-08-31T12:38:27Z`.
- Size: `4255900` bytes; permissions: `0600` (`-rw-------`).
- SHA-256:
  `af37d24bc2ca9c809538e9868810140245458355fe271fe64696bf32c5a96dd4`.
- Retention: preserve through the closure/release decision and replace only
  with a newer independently restored terminal-only bundle.
- `git bundle verify`: PASS; complete history, exact branch ref.
- Isolated branch-aware clone under `/private/tmp`: PASS.
- `git fsck --full --strict`: PASS; exact restored HEAD match; clean restored
  worktree.

This artifact covers the verified code/CI head exactly. The later docs-only
factual record of this verification is intentionally not inside that same
bundle.

## Historical pre-separation artifact

This check used the existing complete pre-separation Git bundle. The source
recovery artifact stayed outside the repository and was not modified.

### Artifact identity

- Size: `450445` bytes.
- SHA-256: `96b6a1fd9001626e35698d809ed625b96f8af397a4551fc424e7bcf5722213c2`.
- `git bundle verify`: PASS; complete history, seven refs.

### Isolated restore

- The bundle was cloned into a newly created directory under `/private/tmp`.
- Restored HEAD: `e2aaa18c729f8b74fce36e905fc15f543321778e`.
- `git fsck --full --strict`: PASS.
- Restored history: 17 commits; restored tracked snapshot: 201 files.
- Restored worktree: clean.
- The isolated restored copy was removed after verification. The source bundle
  and the live TRDNG repository were not changed by this operation.

### Boundary

This proves that the historical complete Git bundle is readable and restorable,
but it remains historical only. Current terminal recovery is provided by the
artifact above. The separate directory archive was not restored. Any real
recovery must still resolve an exact target directory and preserve current work
before replacement.
