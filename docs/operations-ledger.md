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
| Current main / origin | VERIFIED | `af86d2f969d75c84cc8518860be50e23b0776faf`; local `main` equals `origin/main` |
| Current working branch | DIRTY / PUBLICATION PENDING | `codex/gpt-pro-audit-request`; MEXC diagnostics `b0af3b2`, branding `665e1c4`, PR-01 implementation `a3435bc`; closure docs pending |
| GitHub publication | OWNER ACTION | Read-only check 2026-08-28 07:49 +05:00: `PUBLIC`, no license; no visibility change performed |
| CI acceptance | VERIFIED HISTORICAL | Run `32383296540`: Release build PASS; official tests 269/269 PASS; current dirty branch has no CI evidence |
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
  `a3435bc`. No tag/notarization/release. Publication remained pending at this
  entry.

## Формат backup-записи

`ID | состав | путь | UTC-время | размер | SHA-256 | retention | разрешение | restore evidence | статус`

Статус `VERIFIED` допустим только после успешной отдельной restore-проверки.

## Формат commit/release-записи

`ID | diff scope | branch | commit ID/message | clean status | tag | package path/size/SHA-256 | signing/notarization | release notes | rollback target | разрешение | статус`

Пустые поля не подразумевают успех: до появления проверяемого evidence операция
остаётся `NOT RUN`, `PENDING` или `BLOCKED`.
