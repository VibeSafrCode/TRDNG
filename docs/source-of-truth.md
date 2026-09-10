# TRDNG — source of truth

Назначение: указатель на канонические источники без дублирования их содержания.

## Приоритет

1. Явное актуальное решение Founder.
2. Утверждённый [`stage-1-plan.md`](stage-1-plan.md) — scope и порядок Stage 1.
3. [`stage-1-ledger.md`](stage-1-ledger.md) — фактический статус и evidence.
4. [`operations-ledger.md`](operations-ledger.md) — backup и commit/release
   evidence.
5. Специализированные briefs, provenance и runbooks — требования и контекст.

При конфликте нижестоящий документ не изменяет вышестоящий: drift фиксируется в
sprint ledger и передаётся Founder/ведущему на решение.

## Карта

| Область | Канонический источник |
|---|---|
| Stage 1 scope, sprint order, acceptance | [`stage-1-plan.md`](stage-1-plan.md) |
| Статус спринта, фактические изменения, tests/build/package/memory evidence, risks/blockers | [`stage-1-ledger.md`](stage-1-ledger.md) |
| Backup, restore, commit, tag, package и release evidence | [`operations-ledger.md`](operations-ledger.md) |
| Isolated recovery verification | [`recovery-restore-evidence.md`](recovery-restore-evidence.md) |
| S1.7 + cleanup closure gates | [`closure-cleanup-evidence.md`](closure-cleanup-evidence.md) |

## Правила доказательности

- Команда без результата или артефакт без происхождения не являются evidence.
- Backup требует manifest, checksum и restore evidence; commit/release требуют
  проверяемые идентификаторы и scope.
- Секреты, токены, ключи и реальные приватные payload в документы не вносятся.
- Неподтверждённое маркируется `PENDING`, `NOT RUN` или `BLOCKED`.
