# Recovery restore evidence

Date: 2026-08-31. Status: `PASS / HISTORICAL PRE-SEPARATION ONLY`.

This check used the existing complete pre-separation Git bundle. The source
recovery artifact stayed outside the repository and was not modified.

## Artifact identity

- Size: `450445` bytes.
- SHA-256: `96b6a1fd9001626e35698d809ed625b96f8af397a4551fc424e7bcf5722213c2`.
- `git bundle verify`: PASS; complete history, seven refs.

## Isolated restore

- The bundle was cloned into a newly created directory under `/private/tmp`.
- Restored HEAD: `e2aaa18c729f8b74fce36e905fc15f543321778e`.
- `git fsck --full --strict`: PASS.
- Restored history: 17 commits; restored tracked snapshot: 201 files.
- Restored worktree: clean.
- The isolated restored copy was removed after verification. The source bundle
  and the live TRDNG repository were not changed by this operation.

## Boundary

This proves that the historical complete Git bundle is readable and restorable.
It does **not** recover the current terminal-only Git history and is not the
current release recovery gate. A current terminal-only bundle must be created
from the accepted closure commit and independently restored before release.
The separate directory archive was not restored. Any real recovery must still
resolve an exact target directory and preserve current work before replacement.
