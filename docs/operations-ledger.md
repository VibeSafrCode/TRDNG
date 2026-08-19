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

## Формат backup-записи

`ID | состав | путь | UTC-время | размер | SHA-256 | retention | разрешение | restore evidence | статус`

Статус `VERIFIED` допустим только после успешной отдельной restore-проверки.

## Формат commit/release-записи

`ID | diff scope | branch | commit ID/message | clean status | tag | package path/size/SHA-256 | signing/notarization | release notes | rollback target | разрешение | статус`

Пустые поля не подразумевают успех: до появления проверяемого evidence операция
остаётся `NOT RUN`, `PENDING` или `BLOCKED`.
