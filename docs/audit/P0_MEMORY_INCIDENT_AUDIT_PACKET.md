# P0 memory incident — independent audit packet

Perform a read-only architecture, safety and bypass review. Do not launch
`TRDNG.app`, use credentials, call private APIs, execute orders, merge or release.

## Incident and goal

The exact S1.7 macOS GUI process reached a reported 33.32 GB while paused/slow,
causing critical system memory pressure. The product must remain responsive and
bounded as it evolves from three books toward dense logical-book workflows.

Review the current P0 diff and:

1. Prove or refute that all high-rate market/state/cluster paths are latest-wins
   and cannot enqueue an unbounded number of dispatcher closures.
2. Check retained snapshots, event subscriptions, timers, reconnect tasks,
   clients, HTTP/WebSocket buffers, collection mutations and native visual churn.
3. Challenge the staged target/warning/soft/hard policy in
   [PERFORMANCE-SAFETY.md](../PERFORMANCE-SAFETY.md) and every bypass of the
   external/in-process guards.
4. Verify the watchdog owns only its exact hashed PID tree, refuses concurrent
   runs, captures bounded diagnostics, handles traps and escalates TERM to KILL
   without targeting unrelated applications.
5. Verify CI really disables swap for its cgroup, has bounded time/process/memory
   limits and fails explicitly when the envelope is unavailable.
6. Review scale tiers 3/6/12/24/48/100: one retained latest snapshot per logical
   book, one pending render bit, visible/global row budgets and no hidden attempt
   to add a 100-book product UI.
7. Identify any P0/P1 issue before a short supervised GUI run.

## Boundaries

- No global user memory guardian is installed; that option is deferred.
- No authenticated/private calls, order tests, production orders or money.
- No secrets, credentials, PII, raw private payloads or local diagnostic dumps
  belong in the audit response.

Factual evidence: [P0 incident evidence](../p0-memory-incident-evidence.md).
Architecture contract: [Performance safety](../PERFORMANCE-SAFETY.md).
Original sprint: [S1.7 audit packet](S17_ADAPTIVE_ORDERBOOKS_AUDIT_PACKET.md).
