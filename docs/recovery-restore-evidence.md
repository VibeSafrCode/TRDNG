# Recovery restore evidence

Date: 2026-08-31; updated 2026-09-10. Status:
`PASS / CURRENT TERMINAL CORRECTION COMMIT`.

## Current terminal-only recovery artifact

- Covered current correction head:
  `1c3a37f5f370111c5856a433f77e20a29ed3db9c`.
- Repository-relative locator (ignored, local-only):
  `artifacts/backups/trdng-terminal-closure-1c3a37f.bundle`.
- Size: `4259275` bytes; permissions: `0600` (`-rw-------`).
- SHA-256:
  `a2db3423688cba7df07d6637517484fe85910e0908346f358d604531c9c538d7`.
- `git bundle verify`: PASS; complete history and exact branch ref.
- Isolated branch-aware clone under `/private/tmp`: PASS.
- `git fsck --full --strict`: PASS; exact restored HEAD and clean worktree PASS.
- Retention: preserve through the closure/release decision and replace only
  after a newer accepted code head receives the same restore verification.

## Prior closure recovery artifact

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

This prior artifact covers the earlier verified code/CI head exactly. It is
superseded for current recovery by the `1c3a37f` bundle above.

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
