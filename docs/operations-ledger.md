# TRDNG — backup and commit/release ledger

Этот журнал фиксирует только проверяемые операции. Он не разрешает их
выполнение. Backup, restore, Git init, commit, push, tag и release требуют
отдельного явного разрешения Founder.

## Текущее состояние

| Gate | Состояние | Evidence / причина |
|---|---|---|
| Backup | NOT RUN | Разрешение Founder на backup не предоставлено |
| Restore verification | NOT RUN | Backup отсутствует; разрешение на restore не предоставлено |
| Local Git metadata | VERIFIED | Branch `main`; HEAD `0d8ed0b` |
| Baseline commit | VERIFIED | `dbb7fc1` — `chore: establish TRDNG stage 1 baseline` |
| S1.5 commit | VERIFIED | `f1ab367` — `feat: complete stage 1 three-venue books` |
| S2.1 commit | VERIFIED | `0d8ed0b` — `feat: add dry-run market order model` |
| S2.2 commit | PENDING AUDIT CLOSURE | S2.2 changes остаются uncommitted |
| Rollback reference | VERIFIED / NOT EXECUTED | Baseline перед S2.2: `0d8ed0b`; это не backup, откат не выполнялся |
| GitHub / push | NOT RUN | Remote не настроен; публикация не выполнялась |
| Tag / release | NOT RUN | Не разрешены; проверяемых идентификаторов нет |

## Формат backup-записи

`ID | состав | путь | UTC-время | размер | SHA-256 | retention | разрешение | restore evidence | статус`

Статус `VERIFIED` допустим только после успешной отдельной restore-проверки.

## Формат commit/release-записи

`ID | diff scope | branch | commit ID/message | clean status | tag | package path/size/SHA-256 | signing/notarization | release notes | rollback target | разрешение | статус`

Пустые поля не подразумевают успех: до появления проверяемого evidence операция
остаётся `NOT RUN`, `PENDING` или `BLOCKED`.
