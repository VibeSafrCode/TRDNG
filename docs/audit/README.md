# External audit package

This folder is the entry point for an independent GPT Pro review of TRDNG.

Start with [GPT_PRO_PROJECT_AUDIT_PROMPT.md](GPT_PRO_PROJECT_AUDIT_PROMPT.md),
then inspect the whole repository rather than relying on summaries alone. The
repository visibility is owner-controlled external state and must be verified
before every share. Tracked audit material contains no API-key values. Do not ask
the owner to paste credentials into chat and do not execute authenticated
requests or orders during a read-only audit.

Useful sources:

- [Repository README](../../README.md)
- [Architecture](../ARCHITECTURE.md)
- [Stage 1 plan](../stage-1-plan.md)
- [Factual Stage 1 ledger](../stage-1-ledger.md)
- [Product experience brief](../product-experience-brief.md)
- [Security policy](../../SECURITY.md)
- [Operations ledger](../operations-ledger.md)

The documents are evidence and context, not unquestionable truth. Compare their
claims with the current source, tests, CI configuration and Git history. Call out
document drift explicitly.

Before sharing this folder for a new sprint audit, complete
[SPRINT_CLOSURE_CHECKLIST.md](SPRINT_CLOSURE_CHECKLIST.md) and refresh the audit
prompt with the current accepted scope, exact evidence and open debt.

Current implementation-review entry point:
[PR03_BOUNDED_HTTP_AUDIT_PACKET.md](PR03_BOUNDED_HTTP_AUDIT_PACKET.md).

Previous accepted review packet:
[PR02_BOUNDED_MEMORY_AUDIT_PACKET.md](PR02_BOUNDED_MEMORY_AUDIT_PACKET.md).
