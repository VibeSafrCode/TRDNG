# Recovery restore evidence

Date: 2026-08-31. Status: `PASS / CURRENT TERMINAL IMPLEMENTATION COMMIT`.

## Current terminal-only recovery artifact

- Covered commit: `fb252bfa0afda8f57d51202d74baeb29e8954d79`
  (`fix: close adaptive order-book stability debt`).
- Repository-relative locator (ignored, local-only):
  `artifacts/backups/trdng-terminal-closure-fb252bf.bundle`.
- Created and verified at `2026-08-31T12:29:34Z`.
- Size: `4253174` bytes; permissions: `0600` (`-rw-------`).
- SHA-256:
  `efaecd333d9e4113cb29e974793d8684079ade28f37413d9156d9ad56d547a9a`.
- Retention: preserve through the closure/release decision and replace only
  with a newer independently restored terminal-only bundle.
- `git bundle verify`: PASS; complete history, exact branch ref.
- Isolated branch-aware clone under `/private/tmp`: PASS.
- `git fsck --full --strict`: PASS; exact restored HEAD match; clean restored
  worktree.

This artifact covers the implementation commit exactly. The later docs-only
record of this verification is intentionally not inside that same bundle.

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
