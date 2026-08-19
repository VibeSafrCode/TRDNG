# GitHub operating model

## Initial posture

The owner selected a **private** repository and no license. The previous remote
was deleted and the terminal-only repository was recreated cleanly at
`https://github.com/VibeSafrCode/TRDNG`. Its root is
`5780ef66b20143e918e1d88399bfe985b0c1287e`; the accepted portable-test fix is
`3e9d9e2cfc1ab0c3dffc54aa6cb3646e4c374966`. The replacement is complete, not
pending. Fresh-clone path/content scans and CI are accepted.

A 12 MiB external local archive and verified complete pre-separation Git bundle
preserve recovery evidence. Restore remains NOT RUN. Keep secret scanning, push
protection, Dependabot alerts and updates enabled on the private remote.

Protect `main` with pull requests, required CI, conversation resolution, no
force-push and no deletion. Independent audit is mandatory. Require a GitHub PR
approval when a distinct reviewer/collaborator identity exists. For a solo
private repository where self-approval is impossible, the owner may use a
documented audited exception after recording the independent audit evidence;
this is per-change, not a permanent administrator bypass. Use
`codex/<short-topic>` branches. Do not add `CODEOWNERS` until the correct GitHub
identity is known.

## Change flow

1. Define bounded scope and acceptance evidence.
2. Implement/test locally without credentials or private calls.
3. Run tracked-secret, large-object and diff checks.
4. Obtain independent code/security review.
5. Create a clean local commit.
6. Request owner approval before push or PR.
7. Merge only with required checks green.

Use scoped imperative commits and do not rewrite accepted shared history. The
one-time clean recreation is complete and documented; it is not an ongoing
exception.
Release tags come only from accepted `main`, after build/package evidence,
restore verification and owner approval.

## Evidence, backup and recovery

Evidence states what ran, outcome, artifact/hash and `NOT RUN` debt. Backups require inventory, checksum and tested restore; a remote alone is not a backup. Verify a recoverable copy and exact target before destructive operations. Never put secrets in evidence or backup manifests.

## Supply chain

CI has `contents: read`, no secrets, no artifact publication and no release
permissions. Official `actions/checkout` and `actions/setup-dotnet` are pinned to
full commit SHAs independently verified from their official repositories on
2026-08-19. Dependabot continues to monitor `github-actions` for reviewed update
proposals.
