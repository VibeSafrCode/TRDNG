# Contributing

TRDNG currently uses a small-team workflow in a public repository. The Founder has intentionally selected no license, so public visibility does not grant permission to redistribute or reuse the source.

1. Create a focused `codex/<short-topic>` branch from current `main`.
2. Keep one bounded change per pull request.
3. Add deterministic tests and update evidence.
4. Restore/build/test locally without secrets or private live calls.
5. Complete independent diff/security review before merge.
6. Commit locally first; push, PR, merge, tag or release only after an owner gate.

Never commit credentials, journals, dumps, certificates, Keychains, installer payloads, `.tools`, build output or packaged apps. Do not add live exchange tests to CI. See the [GitHub operating model](docs/GITHUB-OPERATING-MODEL.md) and [security policy](SECURITY.md).
