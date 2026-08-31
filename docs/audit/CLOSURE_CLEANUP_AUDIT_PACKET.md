# S1.7 + cleanup closure — independent audit packet

Perform a read-only audit of the accepted closure branch/PR. Do not launch the
app, use credentials, call private endpoints, place orders, mutate GitHub or
change repository files.

## Founder goal and bounded scope

The immediate product goal is a stable Mac terminal that opens BTC/USDT and any
officially discovered supported instrument across MEXC, Gate and Bybit public
books, with independent per-book depth, scale and colors. This closure finishes
S1.7 and hardens existing architecture; it does not add private trading, new
exchanges, a screener or S3.3.

Review the exact accepted diff against these invariants:

1. High-rate market data is latest-wins, renders at most 10 Hz and does not
   retain dispatcher closures or recreate all visual rows per update.
2. Market switching is latest-request-wins and transactionally stages at most
   one bounded replacement set; unstarted staged clients cannot leak on factory,
   dispose, cancellation or supersession failures.
3. Catalog refresh never marks a rolled-back catalog fresh, survives a failed
   reconciliation, blocks stale new selections and rebuilds/fail-closes an
   active instrument when official mapping metadata changes.
4. Settings persistence is bounded, versioned, validated, unique-temp atomic,
   off the UI thread and awaited on normal close. Forced process termination is
   not claimed durable.
5. In-process and exact-process memory guards have no broad target, stale-lock,
   PID-reuse, orphan or TERM/KILL bypass.
6. CI has least privilege, a reviewed full-SHA checkout, digest-pinned SDK,
   2 GiB memory/no-swap cgroup and bounded build/test/replay commands.
7. Documentation distinguishes application PASS from environment-blocked
   screenshot and long-soak gates, and historical recovery from current
   terminal recovery.

## Evidence supplied

- [Closure evidence](../closure-cleanup-evidence.md)
- [P0 incident evidence](../p0-memory-incident-evidence.md)
- [Performance contract](../PERFORMANCE-SAFETY.md)
- [S1.7 evidence](../s1.7-adaptive-orderbooks-evidence.md)
- [MEXC polling evidence](../mexc-polling-evidence.md)
- [Recovery evidence](../recovery-restore-evidence.md)
- [Operations ledger](../operations-ledger.md)

Local final facts before GitHub publication: Release build 0 warnings/errors;
official deterministic suite 367/367 PASS; one-million-cycle replay PASS;
final signed executable SHA-256
`1d93a3a074aa0bfdf36e5a49091a9b1acf9d51ecaf2790678fa3de4ba6b25e90`;
five-minute guarded app run PASS. The 15-minute prerequisite was stopped by
system swap while app physical footprint remained near 190 MiB; visual capture
was blocked by Screen Recording permission. These are not release PASS claims.

## Requested output

Return P0/P1/P2 findings with file/line evidence. Separate release blockers from
future product work. Explicitly state whether the branch may be committed and
pushed for CI, and whether it may be merged/released under the documented gates.
Do not recommend weakening memory thresholds or treating missing visual/soak
evidence as PASS.
