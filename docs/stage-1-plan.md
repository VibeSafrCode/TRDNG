# TRDNG Stage 1 — token-efficient sprint plan

Текущий S0 evidence: [s0-evidence.md](s0-evidence.md). S0.1 и S0.2 пройдены;
S0.3 заблокирован опасным physical-footprint/swap ростом и требует HIGH gate.

Цель: самый короткий безопасный путь от read-only macOS-терминала к одной
контролируемой MEXC Spot сделке. `LOW` разрешён по умолчанию; `STANDARD/HIGH`
требует отдельного разрешения Founder через Ассистента.

| Спринт | Пользовательский результат | Scope | Сознательно не делаем | Зависимости / решение Founder | Приёмка и доказательства | Токены | Пауза |
|---|---|---|---|---|---|---|---|
| S0.1 Baseline | Приложение воспроизводимо собирается | SDK, clean build/test, один `artifacts/TRDNG.app`, backup текущего пакета | Новые функции | Нет | Версия SDK; clean Release 0 warnings; все тесты; app запускается | LOW | Да |
| S0.2 Visual QA | Founder видит именно свежую сборку | Перепаковка единственного `.app`, smoke UI, screenshot, проверка stale/official tick | Редизайн | Разрешение локального запуска | Screenshot совпадает с исходником; live public books; нет старого XAML | LOW | Да |
| S0.3 Memory guard | Терминал не съедает память MacBook Air | 30–60 min soak, RSS sampling, bounded collections, duplicate-process guard | Оптимизация без измерений | Порог RSS/длительность от Founder | График RSS; один процесс; нет устойчивого роста; расследована аномалия 40+ GB | STANDARD | Да |
| S1.1 Canonical instruments | Один актив имеет однозначную идентичность | `BTC/USDT`, product enum, canonical ID, venue symbol mapping, capability model | UI selector, торговля | Начальный default asset | Unit tests mapping/unsupported combinations | LOW | Да |
| S1.2 MEXC public foundation | MEXC появляется как честный третий источник | Public metadata + Spot book adapter, continuity/reconnect/stale, normalized snapshot | Private API, futures orders | Официальные public endpoints | Parser fixtures; gap/resync tests; public smoke | STANDARD | Да |
| S1.3 Asset selector | Пользователь выбирает актив один раз | Один быстрый selector, хорошие defaults, три независимых venue cards | Watchlists, десятки фильтров | Короткий стартовый каталог | UX screenshot; выбор атомарно переключает все поддерживаемые стаканы | STANDARD | Да |
| S1.4 SPOT / FUTURES | Нельзя перепутать продукт | Явный toggle, отдельные capabilities/symbols/subscriptions; unavailable state | Автоподмена рынка | Какие пары входят в MVP | Tests: spot/perp isolation; UI всегда показывает product | STANDARD | Да |
| S1.5 Three-book acceptance | Один экран показывает MEXC/Gate/Bybit | Общая шкала, три раздельных стакана, unsupported/unavailable, MacBook layout | Слияние ликвидности, smart routing | Приоритет ширины/плотности | Live soak; stale/disconnect; resize/trackpad visual QA | STANDARD | Да |

| S2.1 Dry-run order model | Команда всегда адресована одной видимой бирже | Active venue, BUY/SELL MARKET intent, qty/notional semantics, filters, client order ID | Сеть/private keys | Default qty mode и confirmation UX | Deterministic unit/property tests; no transport dependency | STANDARD | Да |
| S2.2 Risk gates | Ошибка ввода не становится сделкой | Hard notional/qty limits, active venue/product banner, confirmation, kill switch | Реальные ордера | Founder утверждает лимиты | Negative tests; kill switch blocks every path; audit event | STANDARD | Да |
| S2.3 Dry-run reconciliation | Пользователь видит предсказуемый жизненный цикл | Simulated ack/fill/reject/timeout, audit trail, restart recovery | Биржевой transport | Retention policy | Replay/restart/idempotency tests | HIGH | Да |
| S3.1 Keychain boundary | Секреты не живут в UI/файлах/чате | Keychain abstraction, masked connection state, revoke flow | Запрос ключей в чате, withdrawal | Manual key creation: no withdrawal, IP allowlist if available | Security review; logs contain no secrets; Keychain integration test | STANDARD | Да |
| S3.2 MEXC Spot read-only private | Баланс и ограничения сверяются безопасно | Signed account/open-orders reads, time sync, permission errors | Place order | Тестовый API key вводит Founder локально | Test fixtures + authenticated read smoke; reconnect/reconciliation | HIGH | Да |
| S3.3 MEXC `/order/test` | Весь ордерный payload проверен без сделки | `POST /api/v3/order/test`, signature, filters, audit, UI result | `POST /api/v3/order` | Founder подтверждает test account/limits | Official test endpoint success + reject cases; no balance change | HIGH | Да |
| S4 Tiny MEXC Spot | Первая минимальная сделка с Mac контролируема | Один заранее согласованный символ, BUY или SELL, лимит, owner-gate, immediate reconciliation | Повтор, batch, futures, automation | Отдельное явное разрешение Founder с суммой/символом/стороной | Preflight; execution ID; balance/order reconciliation; kill switch | HIGH | Да, обязательно |
| S5.1 Bybit/Gate demo | Те же гарантии на других площадках | Private read + demo/testnet/dry-run, затем spot/perp по capabilities | Production до acceptance | Аккаунты и лимиты Founder | Contract tests; reconnect; ambiguous-result handling | HIGH | Да |
| S5.2 Production gate | Контролируемое включение каждой venue/product пары | Отдельный capability flag и smoke protocol | Глобальный trading toggle | Отдельное разрешение на каждую пару | Audit/reconciliation/kill-switch evidence | HIGH | Да |
| S6 Release acceptance | Цельный быстрый macOS-продукт | Apple/Jobs UX pass, latency/memory soak, signed/notarized app, backup, docs, changelog, commit gate | Feature expansion | Release checklist Founder | Visual QA, performance report, restore test, clean commit | HIGH | Да |

S1.5 implementation/build/package выполнены 2026-08-10; runtime visual acceptance
и штатный VSTest остаются открыты. Факты: [`s1.5-evidence.md`](s1.5-evidence.md).

S2.1 implementation/build/package выполнены 2026-08-10 строго как simulation-only;
runtime tests и visual acceptance открыты. Факты: [`s2.1-evidence.md`](s2.1-evidence.md).

S2.2 implementation/build/package выполнены 2026-08-11; production limits остаются
unconfigured, runtime/visual открыты. Факты: [`s2.2-evidence.md`](s2.2-evidence.md).

S2.3 implementation/build/package выполнены 2026-08-11 только для локальной
simulation lifecycle; независимый аудит пройден без новых блокеров,
runtime/visual открыты; проверенный changeset фиксируется локальным S2.3 commit
без GitHub/публикации. Факты:
[`s2.3-evidence.md`](s2.3-evidence.md).

## Постоянный официальный blocker

## S3.1 status — implementation/build/package pass; native runtime open

Core credential vault и macOS Security.framework boundary реализованы без
реальных credentials/private API. Masked UI не принимает и не показывает
секреты. Независимый security/acceptance audit и diff check прошли; Release
build/package/codesign прошли; native synthetic Keychain smoke, VSTest и GUI не
запускались. Проверенный changeset фиксируется локальным S3.1 commit без
GitHub/push; S3.2 не начат. Факты:
[`s3.1-evidence.md`](s3.1-evidence.md).

## S3.2 status — read-only foundation implemented / authenticated runtime open

Deterministic MEXC Spot V3 signing, server-time freshness, Keychain leases,
GET-only account/openOrders transport and masked states are implemented without
real credentials or authenticated calls. Evidence:
[`s3.2-evidence.md`](s3.2-evidence.md).

## S3.3 status — order-test foundation implemented / authenticated runtime open

Dedicated single-use MEXC Spot `POST /api/v3/order/test` foundation is behind
exact confirmation, official-filter validation and simulation risk evidence. No
real credential or authenticated request was used; production order/cancel/
withdraw routes remain absent. The future smoke requires a distinct
`SPOT_DEAL_WRITE`, no-withdrawal, preferably IP-bound key; it is not the S3.2
read-only key. Evidence: [`s3.3-evidence.md`](s3.3-evidence.md).

## Pre-key readiness status — accepted / metadata blocker remains

Official exchangeInfo metadata is mapped fail-closed into side-specific
order-test readiness; missing official proof remains `NeedsMetadata`. A passive
preflight and secure local provisioning runbook are implemented without keys or
private calls. Evidence:
[`pre-key-readiness-evidence.md`](pre-key-readiness-evidence.md).

## Pre-key validation probe — implemented / owner action required

A separate fail-closed candidate and single-use owner authorization can use only
MEXC `/api/v3/order/test` as a no-execution validation oracle. It does not alter
production filters or authorize S4. No key/private request was used. Evidence:
[`pre-key-validation-probe-evidence.md`](pre-key-validation-probe-evidence.md).

## In-app Keychain entry — implementation complete / audit open

Two isolated MEXC credential pairs can now be entered and revoked inside the app
through the existing audited macOS Keychain boundary. STOP, explicit replacement,
pair-level status, rollback and redaction remain fail closed. No authenticated
request or real credential was used. Evidence:
[`in-app-keychain-entry-evidence.md`](in-app-keychain-entry-evidence.md).

## S1.6 — dynamic public catalog / accepted and published

Official credential-free catalogs now provide a bounded search beyond the two
starter shortcuts. MEXC Spot and Gate/Bybit USDT Perpetual remain product-
isolated; only exact catalog-proven venue symbols create public clients. No
private or trading scope changed. Evidence: [`s1.6-evidence.md`](s1.6-evidence.md).

MEXC Futures public books допустимы. Private futures trading остаётся
`BLOCKED/UNAVAILABLE`: официальная Contract API помечает place/cancel как under
maintenance/closed. Поддержку нельзя включать, пока официальный API и
`apiAllowed` конкретного контракта не подтверждены повторно.

## Самый короткий путь до первой безопасной сделки

`S0.1 → S0.2 → S0.3 → S1.1 → S1.2 → S1.3 → S1.4 → S1.5 → S2.1 → S2.2 → S2.3 → S3.1 → S3.2 → S3.3 → отдельное разрешение Founder → S4`.

Нельзя сокращать Keychain, hard limits, audit, reconciliation, kill switch и
`/order/test`. Сокращать можно каталог активов, число настроек и визуальные
режимы: для первой сделки достаточно одного MEXC Spot символа и одного заранее
согласованного объёма.

## UX и release-gates

- Один главный сценарий: актив → продукт → сравнение стаканов → одна активная
  биржа → одна команда.
- Active venue, product, quantity и approximate notional видны рядом с кнопкой.
- Технические ошибки переводятся в понятное состояние; подробности остаются в
  audit log.
- Каждый спринт заканчивается backup/документацией и чистым проверяемым commit;
  смешивать незавершённые спринты запрещено.

## Обязательный финальный gate каждого спринта

Перед acceptance, commit и публикацией исполнитель обязан выполнить один
объединённый closure-pass:

1. Проверить технический долг: незавершённые runtime/GUI/CI проверки, временные
   обходы, deprecated dependencies, производительность, безопасность, packaging
   и compatibility debt. Каждый пункт получает статус `FIXED`, `ACCEPTED` либо
   `OPEN` с точным следующим шагом.
2. Проверить документальный долг: README, архитектура, план, factual ledger,
   evidence и operations ledger должны соответствовать текущему коду и реально
   выполненным проверкам. Неподтверждённое остаётся `NOT RUN`.
3. Обновить пакет для внешнего аудита в `docs/audit/`: актуальная задача,
   продуктовые границы, проверенный commit/diff, тестовые доказательства,
   открытые риски и вопросы аудитору. Ключи, подписи запросов и другие секреты
   туда не попадают.
4. Выполнить diff/link/secret-safety проверки пакета и только затем запрашивать
   независимый аудит и разрешение на commit/release.

Рабочий checklist: [`audit/SPRINT_CLOSURE_CHECKLIST.md`](audit/SPRINT_CLOSURE_CHECKLIST.md).

## Audit PR-02 status — accepted / merged

Order-book state, MEXC pre-snapshot buffering and current cluster intervals now
have explicit fail-closed capacity boundaries. Venue sessions require resync
instead of silently truncating data. Visual and trading behavior were not
expanded. Evidence: [`pr02-bounded-memory-evidence.md`](pr02-bounded-memory-evidence.md).
GitHub CI `33298030728` passed the Release build and 309/309 tests; pull request
`#7` merged as `c8e2362cb2b9c06cfaf4c914225ca7f5ceb9c757`.

## Audit PR-03 status — accepted / merged

Public Bybit/Gate/MEXC metadata, catalogs and the MEXC REST depth snapshot now
use one pooled bounded reader with endpoint-specific caps, JSON media-type gate,
safe error codes, five-second production timeout and explicit no-redirect/no-
cookie policy. Exact implementation commit: `89e645e`. Local Release build and
package/codesign PASS; the one official local VSTest attempt remained sandbox
IPC-blocked. A test-only counter correction then passed GitHub CI `33299985960`
with 323/323 official tests. Pull request `#8` merged as
`8ba6fbfb2a15ee2a8f9fb2a6d4fbdf4f2991fdf7`. Evidence:
[`pr03-bounded-http-evidence.md`](pr03-bounded-http-evidence.md). PR-04 memory
soak remains a separate HIGH sprint.
