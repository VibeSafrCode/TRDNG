# GitHub operating model

## Initial posture

The owner selected a **private** repository and no license. The private remote
`https://github.com/VibeSafrCode/TRDNG` exists, and the initial `main` snapshot
was pushed at `e2aaa18c729f8b74fce36e905fc15f543321778e`.

That remote snapshot's reachable history contains material excluded from the
terminal-only repository. Founder authorized a terminal-only force replacement;
it remains pending independent acceptance of the prepared snapshot. A verified
external local archive and a verified complete pre-separation Git bundle exist.
Do not perform the replacement without checking those recovery points and the
exact remote target. Keep secret scanning, push protection, Dependabot alerts
and updates enabled on the private remote.

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

Use scoped imperative commits and do not normally rewrite accepted shared
history. The single terminal-only force replacement described above is an
explicitly authorized exception and must preserve audit/recovery evidence.
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
