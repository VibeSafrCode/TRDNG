# GitHub readiness evidence

Status: TERMINAL-ONLY SNAPSHOT / INDEPENDENT AUDIT OPEN
Date: 2026-08-19
Baseline: `e2aaa18c729f8b74fce36e905fc15f543321778e`

## Result

The proposed snapshot contains only the TRDNG terminal, its deterministic tests,
packaging metadata and terminal documentation. Separate research and integration
tracks were removed from the tracked snapshot and remain preserved outside this
repository. This preparation made no new push, history rewrite, build, package
or app change.

Founder selected a **private** repository and no license. The private remote is
`https://github.com/VibeSafrCode/TRDNG`; its initial `main` push was
`e2aaa18c729f8b74fce36e905fc15f543321778e`.

The remote's reachable history currently contains excluded local-only material.
Founder authorized a terminal-only force replacement, pending acceptance of
this snapshot. A verified external local archive and verified complete
pre-separation Git bundle are available as recovery points. No replacement or
new GitHub write was performed in this preparation step.

## Repository audit

- `git ls-files` tracked inventory reviewed.
- Current `git ls-files` path/name scan and case-insensitive tracked-content scan
  return zero excluded research-track terms. Remote reachable-history cleanup is
  authorized and pending, not part of this snapshot step.
- Object store at initial audit: 694 loose objects, about 3.32 MiB, no pack files.
- Installers, `.tools/`, packaged app and extracted payload remain ignored and
  must not be force-added.
- `git ls-files -ci --exclude-standard` returned no tracked file accidentally
  covered by the strengthened ignore rules.

## CI trust posture

- `contents: read`; no secrets, live tests, authenticated endpoint, upload, release or deploy.
- One terminal job on stable `ubuntu-24.04` restores, builds and runs official
  `dotnet test` for `Trdng.slnx`. It enables no live exchange network or
  credentials.
- The first terminal CI run against the old snapshot built successfully. The
  official suite passed 241 of 245 tests; four deterministic failures remain
  open and must be corrected before CI acceptance.
- Official Actions are pinned to full SHAs independently verified read-only
  against their official repositories on 2026-08-19:
  `actions/checkout@11d5960a326750d5838078e36cf38b85af677262` (`v4`) and
  `actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9`
  (`v4`). Dependabot still monitors GitHub Actions updates.

## Validation

- YAML syntax: PASS for workflow and Dependabot files using local Ruby/Psych.
- Markdown link/path check: PASS across 30 remaining Markdown files.
- Large reachable-object audit: PASS; largest reachable blob is 175,875 bytes
  (`src/Trdng.Desktop/Assets/avalonia-logo.ico`), with no large binary payload.
- Heuristic tracked/current-history secret-pattern scan: no credential value or
  private-key block found. One current-source match is the intentional
  `RandomNumberGenerator.GetBytes(32)` synthetic Keychain test buffer—not a
  credential. This scan is evidence, not a substitute for GitHub push protection.
- `git diff --check`: PASS.
- `git ls-files -ci --exclude-standard`: empty; no legitimate tracked source is
  hidden by ignore rules.
- Source/build/package: NOT RUN; source did not change.
- Current snapshot preparation: no push, remote mutation or history rewrite.
