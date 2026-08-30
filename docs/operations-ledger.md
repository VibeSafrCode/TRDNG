# TRDNG — backup and commit/release ledger

Этот журнал фиксирует только проверяемые операции. Он не разрешает их
выполнение. Backup, restore, Git init, commit, push, tag и release требуют
отдельного явного разрешения Founder.

## Текущее состояние

| Gate | Состояние | Evidence / причина |
|---|---|---|
| External archive | VERIFIED | 12 MiB; directory comparison PASS |
| Pre-separation Git bundle | VERIFIED COMPLETE | Full bundle verified before clean recreation |
| Restore verification | NOT RUN | Recovery artifacts exist; restore procedure not executed |
| Repository root | VERIFIED | `5780ef66b20143e918e1d88399bfe985b0c1287e` |
| Current main / origin | VERIFIED | PR-04 merged as `2e7d9218c2db462bd0b45ec9f372462b1945cd00` |
| Current working branch | VERIFIED | local `main` fast-forwarded to `origin/main` after PR-04 merge |
| GitHub publication | VERIFIED PUBLIC | Read-only check 2026-08-30: `PUBLIC`, default branch `main`; no visibility change performed in PR-02 |
| CI acceptance | VERIFIED | PR-04 run `33302487008`: Release build PASS; official tests 327/327 PASS |
| Older sprint commit IDs | PRE-SEPARATION LOCAL HISTORY | IDs in older evidence documents are preserved by the verified bundle and are not ancestors of the recreated root |
| Tag / release | NOT RUN | Не разрешены; проверяемых идентификаторов нет |

## 2026-08-20 masked credential UI verification

- Both in-app credential profiles reported `СОХРАНЕНО В KEYCHAIN` through the
  masked UI. No values, lengths or partial values were recorded.
- Fresh packaged signed executable SHA-256
  `56190f245471e79bafd477a12aa9412b0d0ac9f7ec3e54f8bc98a2acf425e4d1`
  launched; credential form and sidebar scrolling worked.
- Catalog/book runtime acceptance was not reached because DNS was restricted
  (`curl api.mexc.com: Could not resolve host`). Status:
  `ENVIRONMENT-LIMITED / NOT PASS`, not a product failure.
- No authenticated/private call, `/order/test` request, production order or
  money action occurred. The app was closed after verification.

## 2026-08-28 local PR-01 package

- One self-contained `osx-arm64` publish replaced the existing ignored
  `artifacts/TRDNG.app`; this is a local development package, not a release.
- Strict deep ad-hoc codesign verification: PASS.
- Packaged `Trdng.Desktop.dll` SHA-256:
  `7044c51b8cc5298a87dfb5e9e31ae7a83f78748da506446f87bf53f5a394725b`.
- Signed executable SHA-256:
  `b6727ede95f65861c3fb712814e91eaf3fff30636e4c8cf31a1dd58360327505`.
- The previous ignored app bundle was not backed up before replacement:
  `BACKUP NOT RUN`. No tag, notarization, release or push was performed as part
  of packaging.
- Source commits created after package verification: `b0af3b2`, `665e1c4`,
  `a3435bc`; evidence through `8f88ffb`. The public branch remote SHA was verified
  at `8f88ffbb961f21b7764b81e9920e578406580a1c`. No PR, merge, tag,
  notarization or release. Branch push did not trigger CI under current workflow
  rules.

## 2026-08-30 local PR-02 package

- One self-contained `osx-arm64` publish replaced only the existing ignored
  `artifacts/TRDNG.app`; this is not a release.
- Strict deep ad-hoc codesign verification: PASS.
- Packaged `Trdng.Core.dll` SHA-256:
  `91c10efed8dfd9cbcb7cfaa36cea62fc42f36abae518aa30f3896af8b39893c8`.
- Signed executable SHA-256:
  `75da43b19cae768db5ff51336f009d240c762734931b1631224dba45672e9450`.
- Backup, GUI, notarization, tag and release: NOT RUN. PR-02 source/evidence
  commit `c7f3ce0` passed PR `#7` CI and merged in `c8e2362`.

## 2026-08-30 local PR-03 package

- One effective self-contained `osx-arm64` publish replaced only the existing
  ignored `artifacts/TRDNG.app`; this is not a release.
- Strict deep ad-hoc codesign verification: PASS.
- Packaged `Trdng.Core.dll` SHA-256:
  `4eaa90c921c539ea3ccae25853b1240fd784ddde61cbf0f42403b3f63fe369ac`.
- Signed packaged executable SHA-256:
  `91026b011e7b55376dec773471be3a542d8ee8aa4685a9a6a90279c3d25e4a9c`.
- Backup, GUI, live network, notarization, tag and release: NOT RUN.
- Implementation commit `89e645e`; test-only correction `2a0c938`.
- Pull request `#8` final CI `33299985960`: Release build PASS, official tests
  323/323 PASS. Merged to `main` as
  `8ba6fbfb2a15ee2a8f9fb2a6d4fbdf4f2991fdf7`.

## 2026-08-30 local PR-04 package and diagnostic

- One self-contained `osx-arm64` publish replaced only the existing ignored
  `artifacts/TRDNG.app`; this is not a release.
- Strict deep ad-hoc codesign verification: PASS.
- Packaged `Trdng.Core.dll` SHA-256:
  `da061dd6039cd8cf2ad7764dd0a00684412cb5cc2e9d0836a6a32271572ce9ce`.
- Signed packaged executable SHA-256:
  `c5a369f42f87b6b485186c82854636b1c99932fcf45d06cae8cdad798aaf1f5e`.
- One-million-cycle credential-free replay and Release build: PASS. The exact
  package completed a 15m40s native diagnostic with one process. Final RSS:
  74,224 KiB; final/peak physical footprint: 204,065,920 / 220,843,136 bytes;
  final swapped memory: 159.2 MiB, about 34.3 MiB above the initial sample.
  Classification: `PASS_15_MIN`, not a two-hour release pass. Exact PID `7946`
  was terminated and absence verified.
- Backup, notarization, tag, release, private/authenticated calls, orders and
  money actions: NOT RUN.
- Implementation/evidence commit `8f0eab7`; pull request `#9`; final CI
  `33302487008` PASS with 327/327 official tests. Merged to `main` as
  `2e7d9218c2db462bd0b45ec9f372462b1945cd00`.

## 2026-08-30 local S1.7 adaptive-books package

- Branch `codex/adaptive-orderbooks`; baseline
  `b7b0e7060f4c00d7fcb072d78f02dfb59be2ee9e`; implementation remains
  uncommitted pending independent audit.
- Final Release solution build: PASS, 0 warnings/errors. One official local
  VSTest attempt was IPC-blocked before execution and not retried.
- One final self-contained `osx-arm64` synchronization replaced only the
  existing ignored `artifacts/TRDNG.app`; strict deep ad-hoc codesign: PASS.
- Packaged `Trdng.Desktop.dll` SHA-256:
  `467f9b7c706bcfc7adeefbaccaaa956d0ee1ff59d3d0489bf797d2dd223f9c9d`.
- Signed executable SHA-256:
  `84a68a4bf460885bac170fd82a4b0ed6f8843c7f456635c9a32667b87e5427a8`.
- Exact package startup with BTC default: PASS. Populated-book final screenshot,
  Git publication, CI, backup/restore, notarization, tag and release: NOT RUN.

## Формат backup-записи

`ID | состав | путь | UTC-время | размер | SHA-256 | retention | разрешение | restore evidence | статус`

Статус `VERIFIED` допустим только после успешной отдельной restore-проверки.

## Формат commit/release-записи

`ID | diff scope | branch | commit ID/message | clean status | tag | package path/size/SHA-256 | signing/notarization | release notes | rollback target | разрешение | статус`

Пустые поля не подразумевают успех: до появления проверяемого evidence операция
остаётся `NOT RUN`, `PENDING` или `BLOCKED`.
