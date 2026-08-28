# Sprint closure and audit-package checklist

Complete this checklist once, at the end of each sprint, before acceptance or
publication.

## Technical debt

- [ ] Scope and safety boundaries still match the Founder decision.
- [ ] Build, targeted tests, full tests/CI, GUI and live checks have exact factual
      statuses; anything not executed is `NOT RUN`.
- [ ] Runtime, performance, memory, security, packaging, compatibility and
      dependency debt is listed as `FIXED`, `ACCEPTED` or `OPEN`.
- [ ] Temporary development credentials and permissions have a rotation/revoke
      step; no secret value appears in source, Git, docs, logs or screenshots.
- [ ] No unapproved production order, cancellation, withdrawal, transfer or money
      action exists.

## Documentation debt

- [ ] README and architecture describe the current product and naming.
- [ ] Stage plan, factual ledger, evidence and operations ledger agree with code,
      tests, package and Git state.
- [ ] Superseded blockers are retained as history but clearly marked superseded.
- [ ] Commit IDs, CI runs, hashes and package names are recorded only after they
      are actually created and verified.

## External audit package

- [ ] Audit prompt states the current user goal, sprint scope and explicit
      exclusions.
- [ ] Auditor receives the exact commit or uncommitted diff to inspect.
- [ ] Evidence summary includes build/tests/runtime/GUI/live results and blockers.
- [ ] Open technical/product/security questions are consolidated into one list.
- [ ] Markdown links, `git diff --check`, tracked-file secret scan and repository
      visibility are verified before sharing.
- [ ] Package contains no credentials, signed URLs, signatures, raw private API
      payloads, personal data or local-only secret paths.
