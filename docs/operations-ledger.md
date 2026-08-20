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
| Current main / origin | VERIFIED | `3e9d9e2cfc1ab0c3dffc54aa6cb3646e4c374966`; worktree clean |
| GitHub publication | VERIFIED | Private, no-license terminal repository published |
| CI acceptance | VERIFIED | Run `32235655100`: Release build PASS; official tests 245/245 PASS |
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

## Формат backup-записи

`ID | состав | путь | UTC-время | размер | SHA-256 | retention | разрешение | restore evidence | статус`

Статус `VERIFIED` допустим только после успешной отдельной restore-проверки.

## Формат commit/release-записи

`ID | diff scope | branch | commit ID/message | clean status | tag | package path/size/SHA-256 | signing/notarization | release notes | rollback target | разрешение | статус`

Пустые поля не подразумевают успех: до появления проверяемого evidence операция
остаётся `NOT RUN`, `PENDING` или `BLOCKED`.
