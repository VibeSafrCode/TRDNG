# TRDNG — актуальный технический, security- и product-аудит для Codex

Дата: 2026-08-25<br>
Репозиторий: `VibeSafrCode/TRDNG`<br>
Ветка: `main`<br>
Проверенный HEAD: `af86d2f969d75c84cc8518860be50e23b0776faf`<br>
Сообщение HEAD: `docs: record masked credential verification`<br>
Основной стек: C# / .NET 10 / Avalonia / macOS arm64<br>
Статус продукта: pre-release, Stage 1

---

## 0. Как использовать этот документ

Этот файл предназначен для передачи Codex в качестве:

1. актуального описания состояния TRDNG;
2. реестра технических и security-рисков;
3. очереди небольших PR;
4. ограничений перед подключением реальной торговли;
5. первого конкретного задания, с которого Codex должен продолжить разработку.

Codex не должен пытаться реализовать весь документ одним PR.

Перед каждой задачей Codex обязан:

- проверить текущую ветку и HEAD;
- сравнить HEAD с `af86d2f969d75c84cc8518860be50e23b0776faf`;
- прочитать:
  - `README.md`;
  - `CONTRIBUTING.md`;
  - `SECURITY.md`;
  - `docs/source-of-truth.md`;
  - `docs/ARCHITECTURE.md`;
  - `docs/stage-1-plan.md`;
  - `docs/stage-1-ledger.md`;
  - `docs/operations-ledger.md`;
- не выполнять реальные приватные запросы к биржам;
- не использовать настоящие API-ключи;
- не добавлять production order endpoint без отдельного разрешения владельца;
- не выполнять push, merge, release или изменение GitHub settings без отдельного разрешения;
- сохранять fail-closed поведение проекта.

---

# 1. Достоверность и границы аудита

## 1.1. Что было проверено

Аудит выполнен заново по содержимому актуального GitHub-репозитория.

Проверены:

- структура solution и проектов;
- архитектурные документы;
- roadmap Stage 1;
- evidence/ledger-документы;
- `Trdng.Core`;
- публичные адаптеры Bybit, Gate и MEXC;
- MEXC private-read и `/api/v3/order/test` foundation;
- Keychain abstraction и macOS interop;
- dry-run, risk, confirmation, simulation journal и recovery;
- Avalonia Desktop UI и lifecycle;
- CI;
- Dependabot;
- `.gitignore`;
- security policy;
- packaging metadata;
- текущее состояние ветки `main`;
- фактическая видимость и protection state репозитория;
- GitHub Actions run для проверенного HEAD.

## 1.2. Что не было выполнено

Этот аудит является статическим анализом через GitHub API.

Не выполнялись:

- локальный clone/checkout;
- независимый `dotnet restore`;
- независимый build/test;
- запуск GUI;
- macOS Keychain smoke;
- live public market-data soak;
- authenticated exchange request;
- `/api/v3/order/test`;
- production order;
- money action;
- code signing/notarization;
- анализ локального app bundle;
- проверка настоящих ключей;
- активный penetration test;
- push или изменение кода.

Причина: среда создания аудита не имела рабочего сетевого clone-доступа к GitHub и не являлась доверенным macOS runtime проекта.

## 1.3. Подтверждённое CI-состояние

Для точного HEAD `af86d2f...` GitHub Actions run `32383991367` завершился успешно:

- restore: PASS;
- Release build: PASS;
- deterministic test step: PASS.

Документ `docs/in-app-keychain-entry-evidence.md` сообщает о полном наборе `254/254` тестов для принятого Keychain slice. Само число `254/254` этим аудитом независимо не перепроверено.

## 1.4. Важное различие

Внутренние evidence-документы корректно разделяют:

- `PASS`;
- `NOT RUN`;
- `BLOCKED`;
- `WAIVED / ACCEPTED_WITH_RISK`.

Этот принцип необходимо сохранить.

Нельзя превращать:

- compile PASS в runtime PASS;
- deterministic fixture PASS в live exchange PASS;
- codesign verification в notarization PASS;
- `/order/test` foundation в разрешение на production order;
- отсутствие найденного секрета в доказательство отсутствия секрета во всей Git-истории.

---

# 2. Executive summary

TRDNG уже представляет собой не UI-макет, а достаточно зрелую исследовательскую основу локального multi-venue торгового терминала.

В проекте есть:

- динамический каталог инструментов;
- каноническая модель инструмента;
- отдельные стаканы по биржам;
- Bybit public perpetual market data;
- Gate public futures market data;
- MEXC public Spot market data;
- trades и кластеры там, где соответствующий канал реализован;
- визуальная оценка ликвидности;
- cross-venue presentation;
- dry-run order intents;
- официальные instrument filters;
- локальные risk caps;
- STOP;
- двухэтапное подтверждение;
- simulation journal;
- восстановление симуляционного состояния;
- macOS Keychain;
- отдельные MEXC read-only и order-test profiles;
- MEXC read-only account/open-orders foundation;
- owner-gated `/api/v3/order/test` foundation.

Архитектурная направленность в целом правильная:

```text
Core domain
    ↑
Venue adapters
    ↑
Desktop composition/UI
```

Главный положительный результат: в текущей сборке real trading не реализован, а приватный MEXC request builder использует явный endpoint allowlist.

Однако проект пока нельзя переводить к S4 — одной реальной MEXC Spot сделке.

Перед любым production order необходимо закрыть следующие блокеры:

1. Ограничить размер входящих WebSocket-сообщений Bybit и Gate.
2. Ограничить рост внутренних стаканов и текущих cluster buckets.
3. Вернуть memory anomaly в статус blocker и локализовать её.
4. Убрать API keys/secrets из долгоживущих immutable `string`-свойств ViewModel.
5. Ввести server-independent, crash-safe execution journal и reconciliation.
6. Добавить single-instance protection.
7. Отделить production execution capability от default desktop assembly.
8. Существенно расширить production risk policy.
9. Пройти настоящий signed packaged macOS acceptance для Keychain и read-only private path.
10. Настроить repository governance: репозиторий фактически public, `main` не защищён, хотя документация описывает private/protected модель.

Рекомендованный следующий кодовый спринт:

> Bounded Input & Memory Safety — без реальных ключей и без торговых endpoint.

Не рекомендуется начинать следующий спринт с `/api/v3/order`.

---

# 3. Текущее устройство проекта

## 3.1. Solution

```text
Trdng.Core
├── Instruments
├── MarketData
├── Clusters
├── Orders
└── Credentials

Trdng.Bybit
└── public perpetual market data

Trdng.Gate
└── public futures market data

Trdng.Mexc
├── public Spot market data
├── official metadata
├── read-only private requests
├── signing
├── time synchronization
└── /api/v3/order/test foundation

Trdng.Desktop
├── Avalonia UI
├── application composition
├── market lifecycle
├── dry-run UI
├── simulation UI
└── Keychain credential UI

Trdng.Core.Tests
└── deterministic unit/contract tests
```

## 3.2. Версии и build contract

- SDK pinned через `global.json`: `.NET 10.0.302`;
- `rollForward`: `latestPatch`;
- pre-release SDK запрещён;
- target framework проектов: `net10.0`;
- Desktop: Avalonia;
- credential storage: macOS Keychain;
- текущий packaged target: `osx-arm64`;
- minimum macOS из `Info.plist`: 13.0;
- bundle ID: `com.trdng.terminal`;
- package version: `0.1.0 (1)`.

## 3.3. Product boundary

Поддержанные пути согласно `SECURITY.md`:

- public market data;
- local simulation.

Foundation, но не live-accepted:

- authenticated MEXC reads;
- `/api/v3/order/test`.

Явно исключены:

- production order placement;
- cancellation;
- withdrawal;
- transfer;
- automatic retry ambiguous private operations;
- smart routing;
- simultaneous multi-venue execution;
- MEXC Futures private trading.

---

# 4. Что реализовано хорошо

## 4.1. Domain model

Положительно:

- `decimal` используется для price/quantity/notional;
- есть `CanonicalInstrument`;
- разделены `MarketProduct`, `TradingVenue`, `OrderSide`, `OrderSizingMode`;
- venue symbol не является каноническим ID;
- filters и metadata не подменяются предполагаемыми значениями;
- missing official field приводит к fail-closed состоянию;
- Spot и Perpetual не смешиваются;
- стаканы разных venues остаются отдельными.

## 4.2. Public market-data sessions

Положительно:

- reconnect loop;
- snapshot/delta state;
- continuity handling;
- resync при нарушении MEXC sequence;
- cancellation;
- disposable clients;
- throttling UI publication;
- MEXC имеет 1 MiB safety limit;
- MEXC Spot использует REST snapshot + WebSocket deltas;
- Bybit/Gate не скрывают venue-specific semantics.

## 4.3. Private MEXC foundation

Положительно:

- фиксированный HTTPS host;
- redirects отключены;
- cookies отключены;
- endpoint allowlist;
- production-order endpoint отсутствует;
- response body ограничен 1 MiB;
- `recvWindow` ограничен;
- API key header валидируется;
- canonical query/body;
- secret используется как bytes;
- signing input очищается;
- response errors маппятся в ограниченные states;
- timeout отдельно от caller cancellation;
- owner authorization single-use;
- order-test result признаётся успешным только для точного пустого JSON-object;
- candidate fingerprint отделён от wire request fingerprint;
- ambiguous private write retry отсутствует.

## 4.4. Credential handling

Положительно:

- отдельный Keychain service;
- отдельные identities для read-only и order-test;
- bounded secret size;
- `SecretLease` очищает mutable bytes;
- native boundary errors классифицированы;
- audit не хранит значение секрета;
- revoke и replacement требуют STOP;
- rollback partial pair предусмотрен;
- read-only и order-test profiles не смешиваются.

## 4.5. Dry-run и simulation

Положительно:

- dry-run intent не является execution request;
- client order ID;
- validation result;
- risk decision;
- short confirmation TTL;
- exact prepared payload;
- single-use confirmation;
- STOP;
- local simulation order lifecycle;
- recovery в `Unknown/Reconciliation`, а не в ложный success;
- bounded journal capacity;
- checksum обнаруживает случайную порчу;
- tests на critical state transitions.

## 4.6. Репозиторий и CI

Положительно:

- pinned GitHub Actions by full SHA;
- минимальные CI permissions;
- live tests отключены в CI;
- deterministic restore/build/test;
- Dependabot включён;
- credential and dump patterns находятся в `.gitignore`;
- `SECURITY.md`;
- `CONTRIBUTING.md`;
- evidence discipline;
- точный HEAD имеет успешный Actions run.

---

# 5. Приоритетный реестр проблем

Обозначения:

- `P0-current` — риск существует уже в текущем public/simulation продукте;
- `P0-before-S4` — обязательно закрыть до любой реальной сделки;
- `P1` — ближайшие спринты;
- `P2` — улучшение качества/масштабирования;
- `OWNER` — действие в GitHub/организации, которое Codex не может завершить кодом.

| ID | Приоритет | Область | Краткий вывод |
|---|---:|---|---|
| GOV-001 | P0-current / OWNER | GitHub | Репозиторий фактически public, документация описывает private |
| GOV-002 | P0-current / OWNER | GitHub | `main` фактически не защищён |
| NET-001 | P0-current | WebSocket | Bybit/Gate собирают сообщения без общего size cap |
| MEM-001 | P0-current | Runtime | Memory anomaly 3.4 GB physical footprint не исправлена |
| BOOK-001 | P0-current | Market data | `OrderBookEngine` не ограничивает число уровней |
| SECRET-001 | P0-before-S4 | Credentials/UI | API keys и secrets живут в immutable `string` ViewModel |
| EXEC-001 | P0-before-S4 | Architecture | Реальная торговля должна быть физически отделена от default build |
| EXEC-002 | P0-before-S4 | Safety | Нет crash-safe exactly-once-like execution workflow |
| RISK-001 | P0-before-S4 | Risk | Dry-run risk policy недостаточна для реальных денег |
| INSTANCE-001 | P0-before-S4 | Runtime | Нет доказанного single-instance gate |
| EVID-001 | P0-before-S4 | Acceptance | Нет real-key acceptance private read/order-test |
| HTTP-001 | P1 | HTTP | Public metadata/snapshot читаются без общего byte limit |
| LOG-001 | P1 | Logging | MEXC public client может передавать полный text payload в diagnostics |
| RECONNECT-001 | P1 | Reliability | Reconnect фиксирован 1 сек, без backoff/jitter |
| UI-001 | P1 | Architecture | `MainViewModel` владеет слишком большим числом контуров |
| ASYNC-001 | P1 | Lifecycle | Fire-and-forget initialization и `async void` UI boundaries |
| SHUTDOWN-001 | P1 | Lifecycle | Async cleanup подписан на sync exit event |
| CRED-001 | P1 | Keychain | Replacement сначала удаляет старую pair |
| JOURNAL-001 | P1 | Storage | Simulation checksum не tamper-evident |
| JOURNAL-002 | P1 | Performance | Journal append выполняет повторное чтение файла |
| JOURNAL-003 | P1 | Concurrency | Нужны file lock и single writer |
| AUTH-001 | P1 | Authorization | Simulation/order-test tokens должны использовать CSPRNG policy |
| ERROR-001 | P1 | Privacy | В UI могут передаваться raw exception messages |
| SIGN-001 | P1 | Privacy | Signed GET URI содержит signature; нужен системный redaction gate |
| CATALOG-001 | P1 | Input | Нет глобального bound на число catalog entries и длину всех symbols |
| CLUSTER-001 | P1 | Memory | Текущий cluster bucket не имеет явного max unique-price count |
| FLOW-001 | P1 | Backpressure | Event-based feeds не имеют общего bounded delivery layer |
| CI-001 | P1 | CI | Только Ubuntu, нет macOS security/package gate |
| CI-002 | P1 | Supply chain | Нет CodeQL/secret/dependency-review/SBOM gate |
| RELEASE-001 | P1 | Release | Нет принятой Developer ID + notarization release pipeline |
| DISCLOSE-001 | P1 / OWNER | Security | Private vulnerability reporting не настроен |
| DEP-001 | P1 | Dependencies | 5 Dependabot major/update PR требуют управляемой проверки |
| BUILD-001 | P2 | Build | Нет централизованных `Directory.Build.props`/package management |
| TEST-001 | P2 | Tests | Все тесты собраны в одном project |
| TIME-001 | P2 | Testability | Используются разные clock abstractions вместо `TimeProvider` |
| OBS-001 | P2 | Diagnostics | Нет единого structured/redacted diagnostic event contract |
| UX-001 | P2 | UX | Execution readiness и причины block можно сделать понятнее |

---

# 6. P0: немедленные и предторговые блокеры

## 6.1. GOV-001 — repository visibility расходится с документацией

### Подтверждено

GitHub metadata показывает:

```text
visibility: public
private: false
license: null
```

Одновременно:

- `docs/GITHUB-OPERATING-MODEL.md` говорит, что владелец выбрал private repository;
- `CONTRIBUTING.md` описывает private-by-default workflow;
- security reporting предлагает private channel, потому что private GitHub reporting ещё не настроен.

### Риск

- disclosure architecture и future execution design;
- ошибочное предположение команды, что материалы не публичны;
- потенциальное попадание operational evidence в public;
- невозможность полагаться на private-only disclosure flow;
- отсутствие лицензии создаёт неясность прав на публичный код.

### Действие владельца

Выбрать один вариант.

#### Вариант A — private

- немедленно вернуть repository visibility в private;
- проверить forks/caches/releases;
- запустить full Git-history secret scan;
- проверить GitHub audit/security alerts;
- при любых сомнениях ротировать exchange credentials;
- обновить evidence фактическим состоянием.

#### Вариант B — public

- явно утвердить public posture;
- выбрать license или proprietary source-available notice;
- удалить из docs ложное утверждение private;
- включить GitHub Private Vulnerability Reporting;
- определить public disclosure process;
- провести повторный content/history review.

Codex может исправить документы, но не должен сам менять visibility.

---

## 6.2. GOV-002 — ветка `main` не защищена

### Подтверждено

GitHub branch metadata:

```text
protected: false
protection.enabled: false
required status checks: off
```

### Риск

- direct push может обойти CI;
- force-push/delete protection отсутствует;
- merge Dependabot PR может пройти без обязательного review;
- будущий execution code может попасть в `main` без gate.

### Действие владельца

Настроить branch protection/ruleset:

- pull request required;
- successful `ci` required;
- conversation resolution required;
- force-push forbidden;
- deletion forbidden;
- signed commits или vigilant mode — по решению владельца;
- code owner/reviewer gate, когда будет второй trusted identity;
- restrict bypass;
- require linear history либо documented squash policy.

До этого каждый Codex change должен идти через отдельную branch и ручной diff review.

---

## 6.3. NET-001 — Bybit и Gate не ограничивают полный WebSocket message size

### Подтверждено

Файлы:

- `src/Trdng.Bybit/MarketData/BybitPublicOrderBookClient.cs`;
- `src/Trdng.Gate/MarketData/GatePublicMarketDataClient.cs`.

Оба клиента:

- арендуют receive buffer;
- добавляют fragments в `ArrayBufferWriter<byte>`;
- ждут `EndOfMessage`;
- не проверяют суммарный `WrittenCount`.

MEXC уже содержит `MaxWebSocketMessageBytes = 1 MiB`.

### Риск

Удалённый endpoint, proxy, protocol error или malformed fragmented message способен вызвать неограниченный рост managed buffer.

Это особенно важно на фоне уже зафиксированной memory anomaly.

### Исправление

Ввести reusable bounded accumulator.

Пример контракта:

```csharp
public sealed class BoundedMessageAccumulator
{
    public int MaximumBytes { get; }
    public int WrittenCount { get; }

    public void Append(ReadOnlySpan<byte> fragment);
    public ReadOnlyMemory<byte> Complete();
    public void Reset();
}
```

Требования:

- max bytes задаётся явно;
- до записи проверяется overflow-safe условие;
- превышение даёт typed protocol exception;
- buffer очищается;
- connection закрывается/reconnect/resync;
- raw payload не включается в exception;
- одинаковая политика для Bybit/Gate/MEXC;
- можно иметь разные caps по venue, но общий hard maximum.

### Acceptance tests

- одно маленькое сообщение;
- сообщение из нескольких fragments;
- ровно limit;
- limit + 1;
- overflow arithmetic;
- oversized text;
- oversized binary;
- после reject accumulator можно использовать заново;
- raw payload не попадает в error;
- client reconnects/resyncs;
- memory allocation не растёт пропорционально бесконечному stream.

---

## 6.4. MEM-001 — memory anomaly остаётся принятой с риском, но не решённой

### Подтверждено

`docs/s0-evidence.md`:

- примерно 3 минуты 48 секунд runtime;
- RSS вырос от ~165 MB до ~429 MB;
- `footprint`: 3.4 GB;
- ~3.1 GB swapped;
- ограниченный soak был аварийно остановлен;
- status: `WAIVED / ACCEPTED_WITH_RISK`, не PASS;
- причина не локализована.

### Вывод

Для public-data исследовательской сборки это принятый риск.

Для real-money execution это blocker.

### План локализации

#### Шаг 1 — воспроизводимый soak harness

Сценарии:

1. UI idle без сетевых клиентов;
2. один venue;
3. два venues;
4. три venues;
5. catalog refresh;
6. market switching каждые N секунд;
7. clusters on/off;
8. credentials UI open/closed;
9. simulation playback.

#### Шаг 2 — метрики

Снимать каждые 10 секунд:

- managed heap;
- GC allocations/sec;
- gen0/gen1/gen2;
- LOH;
- RSS;
- physical footprint;
- private bytes;
- number of order-book levels;
- UI collections count;
- render/frame rate;
- WebSocket message count/bytes;
- catalog entries;
- journal size;
- number of event subscriptions.

#### Шаг 3 — инструменты

На доверенной macOS машине:

- `dotnet-counters`;
- `dotnet-gcdump`;
- `dotnet-trace`;
- `vmmap`;
- `footprint`;
- Instruments Allocations/Leaks;
- Avalonia rendering diagnostics.

#### Шаг 4 — budgets

До S4 утвердить:

- startup footprint;
- 15-minute bound;
- 2-hour bound;
- no monotonic growth;
- max book levels;
- max clusters;
- max UI collection entries;
- max log events;
- max journal records.

Пример начального gate, требующий подтверждения на реальном устройстве:

```text
2-hour public-data soak:
- no monotonic retained managed growth;
- physical footprint remains below agreed threshold;
- no unbounded swap growth;
- no increase of event handlers after market switching;
- reconnect cycles return to baseline.
```

Точный threshold должен принять владелец на основании целевого Mac.

---

## 6.5. BOOK-001 — `OrderBookEngine` не имеет capacity boundary

### Подтверждено

`src/Trdng.Core/MarketData/OrderBookEngine.cs` хранит bid/ask в двух `SortedDictionary<decimal, decimal>`.

Delta:

- добавляет любой положительный price level;
- удаляет только при quantity `0`;
- не ограничивает число уровней;
- не проверяет subscribed depth;
- не проверяет crossed book;
- не проверяет максимальный gap/price-domain.

### Риск

- malformed remote feed может накопить большое число уникальных цен;
- venue protocol drift;
- silent memory growth;
- ложный cross-venue result;
- pressure на UI capture;
- потенциальный вклад в memory anomaly.

### Исправление

Добавить `OrderBookCapacityPolicy`.

Пример:

```csharp
public sealed record OrderBookCapacityPolicy(
    int MaximumLevelsPerSide,
    int MaximumLevelsPerUpdate,
    decimal? MaximumPrice = null);
```

Правила:

- snapshot entries <= max;
- delta entries <= max per update;
- итоговые sides <= max;
- цены/quantity валидны;
- best bid < best ask для live snapshot;
- violation не «обрезается молча»;
- session переходит в `ResyncRequired`;
- no partial apply: сначала validate, затем apply;
- capture depth <= policy max.

Для venue depth:

- хранить не меньше реально необходимого;
- не смешивать UI depth и protocol synchronization depth;
- MEXC 1000-level sync может требовать больший internal cap, чем UI 30;
- cap определяется официальным subscribed contract.

### Acceptance tests

- snapshot boundary;
- delta boundary;
- no partial mutation after reject;
- crossed book;
- duplicate price in one update;
- invalid symbol;
- stale sequence;
- memory remains bounded across randomized deltas.

---

## 6.6. SECRET-001 — masked UI всё равно хранит secrets в immutable strings

### Подтверждено

`MainViewModel` содержит:

- `ReadOnlyApiKey: string`;
- `ReadOnlySecret: string`;
- `OrderTestApiKey: string`;
- `OrderTestSecret: string`.

XAML использует `PasswordChar`, что скрывает отображение, но не меняет тип данных.

Evidence честно фиксирует:

> UI framework necessarily holds immutable input strings briefly in memory.

### Риск

- immutable strings нельзя надёжно zero;
- секрет может жить до GC;
- crash dump/heap dump;
- binding diagnostics;
- случайное `ToString`;
- clipboard/input-method history;
- future telemetry.

### Целевая модель

Не хранить secret в long-lived ViewModel.

Варианты:

1. отдельный modal credential dialog;
2. custom secure input control;
3. direct extraction into mutable `char[]`/UTF-8 `byte[]`;
4. немедленная передача в `CredentialPairController`;
5. немедленная очистка control и buffers;
6. ViewModel хранит только state: empty/dirty/submitting/stored/error.

Пример boundary:

```csharp
public interface ICredentialEntrySource
{
    CredentialInputLease Acquire();
}
```

`CredentialInputLease`:

- содержит mutable buffers;
- `IDisposable`;
- zeroes on dispose;
- не имеет `ToString`;
- не поддерживает serialization;
- запрещает logging.

Дополнительно:

- clipboard disabled;
- paste — отдельное продуктово-security решение;
- reveal button по умолчанию отсутствует;
- no autocomplete;
- no control state persistence;
- clear on focus loss/close;
- block screen capture — только если возможно и оправдано;
- use Keychain access control/user presence для будущего live profile.

### Важное ограничение

На managed desktop UI невозможно обещать абсолютное отсутствие временной string representation. Цель — минимизировать lifetime/copies и не хранить секрет в application state.

---

## 6.7. EXEC-001 — real execution должно быть физически отделено

### Текущее сильное свойство

`MexcSignedRequestBuilder` разрешает только:

- `/api/v3/account`;
- `/api/v3/openOrders`;
- `/api/v3/order/test`.

Production `/api/v3/order` отсутствует.

Это важнее UI-toggle: опасный endpoint физически не создан.

### Требование к S4

Не добавлять real order в существующий `Trdng.Mexc` builder просто под:

- bool flag;
- hidden button;
- environment variable;
- debug menu;
- in-memory STOP.

Рекомендуемая структура:

```text
Trdng.Mexc
    public + read-only + order-test only

Trdng.Execution.Contracts
    execution-neutral plans/results/reconciliation

Trdng.Execution.Mexc
    production write adapter
    absent from default build/package

Trdng.Desktop.Safe
    current default app, no execution reference

Trdng.Desktop.Execution
    separate explicitly built artifact
```

Варианты поставки:

- отдельный `.csproj`/assembly;
- отдельный solution filter;
- отдельный package profile;
- отдельный bundle ID;
- отдельная подпись/release channel;
- separate feature manifest generated at build;
- execution assembly checksum displayed in readiness screen.

Default app должна оставаться неспособной отправить реальный ордер даже при компрометации UI-state.

### Architecture test

Добавить тест, который падает, если:

- default Desktop references `Trdng.Execution.*`;
- string `/api/v3/order` появляется вне execution project/fixtures;
- withdrawal/transfer/cancel routes появляются вне allowlisted projects;
- execution project попадает в safe package.

---

## 6.8. EXEC-002 — нужен crash-safe order workflow

Для реальной сделки недостаточно:

```text
click -> HTTP POST -> UI success
```

Нужна state machine:

```text
Draft
  -> PreflightAccepted
  -> OwnerAuthorized
  -> PersistedForSend
  -> Sending
  -> Accepted
  -> Rejected
  -> Unknown
  -> Reconciling
  -> ReconciledAccepted
  -> ReconciledRejected
```

### Обязательные свойства

- exact order plan fingerprint;
- client order ID;
- persistent intent перед network write;
- fsync/atomic append;
- single-use authorization;
- no automatic retry after ambiguous timeout;
- unknown result engages STOP;
- second order blocked while unknown exists;
- restart resumes reconciliation;
- read-only account/open orders/order lookup reconcile outcome;
- user sees `UNKNOWN`, а не `FAILED`;
- idempotency/reconciliation key persistent;
- external response is not sole source of truth.

### Real execution record

```text
OrderExecutionRecord
- local sequence
- clientOrderId
- venue
- instrument
- product
- side
- sizing mode
- requested value
- expected exposure
- metadata fingerprint
- book/reference fingerprint
- risk snapshot
- authorization fingerprint
- prepared at
- sent at
- result state
- exchange order id if known
- reconciliation state
- previous record hash/HMAC
```

---

## 6.9. RISK-001 — dry-run risk policy нельзя напрямую использовать для production

Текущая risk policy полезна для simulation, но реальная торговля требует как минимум:

### Instrument/market

- exact venue capability;
- exact account market type;
- current official symbol status;
- order type permission;
- side permission;
- quantity/notional filters;
- min/max;
- step;
- precision;
- price band;
- trading halt/maintenance;
- fresh metadata.

### Market state

- fresh order book;
- sequence live;
- spread under limit;
- expected slippage;
- liquidity available for requested size;
- reference price age;
- cross-venue divergence not used as execution truth;
- executable venue price, not unrelated venue.

### Account

- credentials profile is live-trade-specific;
- no withdrawal permission;
- IP restriction where supported;
- available balance;
- account mode;
- existing open orders;
- existing position;
- leverage/margin mode for derivatives;
- fee buffer;
- clock synchronized;
- permission verified.

### Local limits

- max single notional;
- max single base quantity;
- max daily notional;
- max daily loss;
- max order count;
- cooldown;
- allowed instruments;
- allowed venues;
- allowed sides;
- allowed product;
- no concurrent order;
- no unresolved order;
- STOP;
- owner confirmation;
- application version allowlist;
- execution adapter checksum allowlist.

### After send

- no retry ambiguous request;
- reconcile;
- STOP on uncertainty;
- audit;
- notification;
- manual acknowledgement.

---

## 6.10. INSTANCE-001 — один journal/Keychain/execution owner должен иметь один process

До execution нужен single-instance gate.

macOS options:

- named lock file with exclusive `flock`;
- local Unix domain socket ownership;
- launch-services single instance behavior, дополненный process lock;
- PID metadata только как diagnostic, не как lock.

Требования:

- второй instance не запускает market/execution services;
- показывает безопасное сообщение;
- stale lock проверяется корректно;
- crash освобождает OS lock;
- journal single writer;
- execution grant принадлежит конкретному process instance;
- owner authorization invalidated when instance changes.

---

## 6.11. EVID-001 — private-path acceptance ещё не выполнен

Документация подтверждает:

- Keychain UI accepted;
- masked state verified;
- deterministic tests pass;
- package launches;
- authenticated private requests NOT RUN;
- `/order/test` NOT RUN;
- money action absent.

До S4 нужен отдельный gate:

1. trusted packaged app;
2. current exact commit;
3. no secrets in environment/chat/logs;
4. read-only MEXC key:
   - account;
   - open orders;
   - permission errors;
   - time sync;
   - restart;
5. separate order-test key:
   - one exact tiny candidate;
   - one owner approval;
   - `/order/test`;
   - no retry;
   - exact empty object evidence;
6. revoke test key;
7. review Keychain access after package replacement;
8. record only masked evidence.

Даже успешный `/order/test` не является разрешением на `/api/v3/order`.

---

# 7. P1: безопасность, надёжность и поддерживаемость

## 7.1. HTTP-001 — bounded HTTP reader должен быть общим

Некоторые public clients используют `GetByteArrayAsync`:

- Bybit metadata/catalog;
- MEXC metadata/catalog;
- MEXC depth snapshot;
- другие venue metadata paths.

Private MEXC client уже имеет безопасный streaming reader с лимитом.

### Рекомендация

Создать общий transport helper:

```csharp
public static Task<byte[]> ReadBoundedAsync(
    HttpClient client,
    HttpRequestMessage request,
    int maximumBytes,
    CancellationToken cancellationToken);
```

Правила:

- `ResponseHeadersRead`;
- Content-Length precheck;
- stream read max+1;
- max duration;
- success/error body caps отдельно;
- pooled buffer;
- safe error code;
- no full body in exception/log;
- endpoints fixed or allowlisted;
- redirect policy explicit;
- response media type validation;
- JSON depth/document options.

Caps должны быть endpoint-specific:

- time: очень малый;
- single metadata: малый;
- full catalog: больше, но bounded;
- depth snapshot: по официальному maximum.

---

## 7.2. LOG-001 — не передавать полный MEXC text frame в diagnostics

`MexcPublicOrderBookClient` формирует:

```text
text {full decoded frame}
```

### Риски

- огромный diagnostic string;
- control characters/log injection;
- UI pressure;
- retention remote payload;
- accidental sensitive future payload if client expands.

### Исправление

Разрешённые diagnostics:

```text
MEXC_WS_CONNECTED
MEXC_WS_TEXT_ACK
MEXC_WS_DELTA
MEXC_WS_SNAPSHOT_APPLIED
MEXC_WS_RESYNC_REQUIRED
MEXC_WS_MESSAGE_REJECTED_SIZE
```

Metadata:

- byte length;
- message type;
- allowlisted method/status;
- sequence numbers;
- hash только при необходимости;
- no full payload.

`DiagnosticReceived` лучше заменить typed event:

```csharp
public sealed record MarketDataDiagnostic(
    TradingVenue Venue,
    MarketDataDiagnosticCode Code,
    int? PayloadBytes,
    long? Sequence);
```

---

## 7.3. RECONNECT-001 — exponential backoff и jitter

Сейчас reconnect примерно через одну секунду.

Нужно:

```text
1s, 2s, 4s, 8s, 15s, 30s cap + jitter
```

Сбросить backoff после stable-live interval.

Отдельно классифицировать:

- transient network;
- rate limit;
- DNS;
- TLS;
- unsupported protocol;
- invalid metadata;
- invalid message;
- sequence gap;
- permanent endpoint denial.

Для protocol-invalid нельзя бесконечно reconnect каждую секунду без circuit breaker.

UI должен показывать:

- reconnecting;
- retry delay;
- permanent blocked;
- stale;
- manual retry.

---

## 7.4. UI-001 — разделить `MainViewModel`

Текущий ViewModel одновременно владеет:

- startup;
- public catalog;
- selection;
- Bybit session;
- Gate session;
- MEXC session;
- UI book rows;
- clusters;
- liquidity;
- cross-venue presentation;
- dry-run;
- risk;
- simulation;
- journal;
- credentials;
- private MEXC state;
- timers;
- disposal.

### Целевая композиция

```text
MainShellViewModel
├── InstrumentSelectorViewModel
├── VenueBoardViewModel
│   ├── MexcVenueViewModel
│   ├── GateVenueViewModel
│   └── BybitVenueViewModel
├── DryRunViewModel
├── CredentialProfilesViewModel
├── SimulationTimelineViewModel
└── DiagnosticsViewModel
```

Services:

```text
IMarketSessionCoordinator
IPublicCatalogService
IMarketPresentationScheduler
IDryRunApplicationService
ICredentialApplicationService
ISimulationRepository
IExecutionReadinessService
```

Не переносить domain logic в дочерние ViewModel.

---

## 7.5. ASYNC-001 — убрать constructor fire-and-forget

В конструкторе:

```csharp
_ = InitializeAsync();
_ = RefreshCredentialStatusAsync();
```

Проблемы:

- exception может стать unobserved;
- startup не await-ится;
- трудно тестировать;
- disposal может пересечься с initialization;
- UI может показывать частично готовое состояние.

### Исправление

```csharp
public interface IAsyncInitializable
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
```

`App`:

1. создаёт composition root;
2. показывает loading shell;
3. await initialization;
4. при error показывает safe error state;
5. close cancels initialization;
6. no client starts after disposal.

---

## 7.6. SHUTDOWN-001 — controlled async shutdown

`desktop.Exit += async ...` является `async void` boundary.

### Риск

Application process может завершиться до полного:

- cancel clients;
- dispose sockets;
- flush journal;
- clear secrets;
- invalidate authorizations;
- engage STOP.

### Исправление

- intercept window closing;
- cancel close;
- `await ShutdownAsync(timeout)`;
- затем разрешить exit;
- idempotent shutdown;
- global exception handler engages STOP;
- hard timeout;
- final journal marker;
- no blocking `.Result` на UI thread.

Все `async void` handlers оставить только как thin framework boundary:

```csharp
private async void Handler(...)
{
    try { await ViewModel.Command.ExecuteAsync(...); }
    catch (Exception error) { AppErrorBoundary.Report(error); }
}
```

Лучше использовать `AsyncRelayCommand`.

---

## 7.7. CRED-001 — atomic credential replacement

Текущий accepted contract предупреждает:

> old pair removed before new pair; failure can leave unconfigured.

Это fail-closed, но неудобно и повышает operational risk.

### Целевая схема

Keychain identities versioned:

```text
readonly-api-key:v2:<id>
readonly-secret:v2:<id>
active-readonly-profile
```

Flow:

1. STOP engaged;
2. write candidate pair under new version;
3. read-back status;
4. optional harmless validation;
5. atomically switch active profile reference;
6. delete old version;
7. rollback active reference on failure;
8. audit masked states.

Active profile reference может храниться:

- в Keychain;
- в signed local settings file without secrets.

Нельзя хранить active secret itself вне Keychain.

---

## 7.8. JOURNAL-001 — checksum не защищает от намеренной подмены

Current SHA-256 checksum detects accidental corruption, но локальный attacker может изменить payload и checksum.

Для simulation это приемлемо.

Для execution нужен:

- hash chain;
- HMAC key в Keychain;
- monotonic sequence;
- previous-record hash;
- app/version fingerprint;
- atomic append;
- `fsync`;
- restrictive mode;
- no symlink;
- trusted directory ownership;
- file identity check;
- corruption => STOP + reconciliation;
- no automatic truncation.

Не называть это non-repudiation: локальный HMAC защищает integrity в пределах threat model, но владелец устройства контролирует ключ.

---

## 7.9. JOURNAL-002 — O(n) append и replay

Текущий file journal, по inspected implementation, повторно читает/проверяет значительную часть файла при append/recovery.

Для bounded simulation это допустимо.

Для execution:

- streaming append;
- index/last sequence cache;
- file lock;
- bounded record size;
- periodic checkpoint;
- archive/rotation;
- replay tests;
- injected crash between write/fsync/rename;
- partial tail recovery;
- no silent record loss;
- backup/export.

---

## 7.10. AUTH-001 — authorization token policy

Simulation controller использует GUID token. Order-test probe также допускает injected token factory и проверяет format.

Для real execution:

- `RandomNumberGenerator.GetBytes`;
- 256-bit entropy;
- compare fixed-time where applicable;
- token digest persisted;
- one active token;
- exact plan fingerprint;
- process instance ID;
- TTL;
- consumed before send;
- restart invalidates unconsumed UI grant;
- no token in logs/UI evidence;
- owner re-auth/user presence.

---

## 7.11. ERROR-001 — stable error codes вместо raw exception text

Public clients передают `exception.Message` в state detail.

Даже public errors могут содержать:

- host details;
- proxy details;
- local path;
- malformed remote content;
- platform messages.

Нужен contract:

```text
NETWORK_UNAVAILABLE
DNS_FAILED
TLS_FAILED
PROTOCOL_INVALID
MESSAGE_TOO_LARGE
SEQUENCE_GAP
METADATA_INVALID
RATE_LIMITED
TIMEOUT
UNKNOWN_SAFE
```

Полная exception:

- только local developer diagnostic;
- redacted;
- не в обычном UI;
- не в evidence;
- не в telemetry.

---

## 7.12. SIGN-001 — защита signed request URI

MEXC signed GET включает `signature` в query.

Это соответствует venue API, но требует запрета:

- default `HttpClient` logging body/URI;
- diagnostic listener с full request URI;
- proxy dump;
- crash report breadcrumbs с URL;
- exception message с request URI;
- screenshot/debug UI URL.

Добавить tests:

- `MexcSignedRequest.ToString()` redacted;
- exception path не включает signature;
- typed audit не включает URL;
- logging handler получает redacted representation;
- no request headers/URI in evidence.

---

## 7.13. CATALOG-001 — bounds для external catalog

Добавить:

- max entries per venue;
- max total entries;
- max asset code length;
- max venue symbol length;
- max cursor length;
- max pages;
- max JSON depth;
- max rejected entries before fail;
- no silent conflict merge;
- deterministic duplicate policy.

`CanonicalInstrument` уже запрещает non-ASCII, но не имеет явного max length.

Практический max должен быть выше текущих official symbols, но конечным.

---

## 7.14. CLUSTER-001 — bounded cluster bucket

`TradeClusterAggregator` ограничивает число completed clusters, но текущий bucket хранит `SortedDictionary` уникальных price buckets без max.

Добавить:

- max price levels per current cluster;
- max trades per interval;
- overflow state;
- metrics;
- malformed timestamp policy;
- bounded gap handling;
- optional coarser aggregation if configured;
- no silent uncontrolled growth.

---

## 7.15. FLOW-001 — backpressure между feed и UI

Event callbacks удобны, но при burst/reconnect могут:

- flood dispatcher;
- сохранять stale update;
- увеличивать allocation churn;
- конкурировать с market switch.

Целевой flow:

```text
venue protocol loop
    -> validated domain updates
    -> bounded channel
    -> latest snapshot projection
    -> coalesced UI scheduler
    -> UI at configured FPS
```

Правила:

- core sequence processing не теряет update; overflow => resync;
- UI presentation может drop stale snapshots;
- latest-generation wins;
- market switch invalidates old generation;
- bounded queue;
- per-venue metrics;
- no UI collection rebuild at feed frequency.

---

# 8. CI, supply chain и release

## 8.1. Текущее CI

Сейчас один Ubuntu job выполняет:

- restore;
- Release build;
- deterministic tests.

Actions pinned by SHA — хорошо.

## 8.2. Рекомендуемый CI split

```text
build-linux
unit-tests
protocol-fixtures
architecture-tests
security-analysis
macos-build
macos-interop-tests
package-unsigned
performance-microbench
```

### Linux

- restore locked mode;
- build;
- unit tests;
- analyzers;
- format;
- coverage;
- architecture tests.

### macOS

- build;
- tests;
- Keychain interop diagnostic with temporary synthetic item;
- app bundle assembly;
- plist validation;
- codesign structure check without production secrets;
- launch smoke where feasible.

Live exchange tests:

- не запускать в PR CI;
- only credential-free public smoke в отдельном manual workflow;
- private test only on trusted owner machine;
- no real order in CI.

## 8.3. Security gates

Добавить:

- GitHub secret scanning + push protection;
- Private Vulnerability Reporting;
- CodeQL C#;
- dependency review action;
- `dotnet list package --vulnerable`;
- SBOM;
- artifact checksums;
- provenance/attestation;
- forbidden-string endpoint scan;
- Git-history secret scan вне обычного PR;
- signed release tags.

## 8.4. Build policy

Добавить `Directory.Build.props`:

```xml
<PropertyGroup>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
  <Deterministic>true</Deterministic>
  <InvariantGlobalization>false</InvariantGlobalization>
  <AnalysisLevel>latest-recommended</AnalysisLevel>
</PropertyGroup>
```

Проверить совместимость analyzer warnings отдельным PR, не включать всё одной массовой правкой.

Добавить:

- `Directory.Packages.props`;
- `packages.lock.json`;
- locked restore in CI;
- central versions;
- explicit package update policy.

## 8.5. Dependabot

Сейчас открыты пять Dependabot PR.

Видимые примеры:

- `xunit.runner.visualstudio 3.1.4 -> 4.0.0`;
- `Microsoft.NET.Test.Sdk 17.14.1 -> 18.9.0`.

Это major upgrades.

Правило:

- не auto-merge;
- отдельная compatibility branch;
- compare release notes;
- Linux CI;
- macOS test;
- test discovery count before/after;
- no skipped tests;
- verify IDE runner;
- update one ecosystem layer at a time.

Patch/minor updates можно группировать после появления полного gate.

## 8.6. Release S6

Нужна отдельная controlled workflow:

1. accepted `main`;
2. version/tag;
3. clean restore;
4. full tests;
5. macOS publish;
6. app bundle;
7. hardened runtime;
8. minimal entitlements;
9. Developer ID signing;
10. notarization;
11. stapling;
12. Gatekeeper smoke;
13. SBOM;
14. checksums;
15. release manifest;
16. manual owner approval;
17. publish artifact.

Secrets для signing/notarization не должны быть доступны untrusted PR.

Current `Info.plist` содержит basic bundle metadata, но не является release pipeline.

---

# 9. Архитектура следующего этапа

## 9.1. Не превращать Desktop UI в execution engine

Целевой dependency direction:

```text
Trdng.Execution.Contracts
    contains immutable plans, states, risk results

Trdng.Execution.Application
    orchestrates preflight, authorization, send, reconciliation

Trdng.Execution.Mexc
    venue adapter

Trdng.Desktop
    displays and invokes application service
```

UI не должен:

- собирать signed request;
- читать secret напрямую;
- решать risk;
- определять accepted/rejected;
- retry;
- менять reconciliation state.

## 9.2. Execution capability matrix

```text
Venue/Product        Public Book  Read-only  Order Test  Real Order
Bybit Perpetual      yes          no         no/demo     no
Gate Perpetual       yes          no         no/demo     no
MEXC Spot            yes          foundation foundation no
MEXC Perpetual       no/blocked   no         no          no
```

Matrix должна генерироваться из code capabilities, а не из UI strings.

## 9.3. Capability object

```csharp
public sealed record VenueCapability(
    TradingVenue Venue,
    MarketProduct Product,
    bool PublicMarketData,
    bool PrivateRead,
    bool ValidationOrder,
    bool ProductionOrder,
    string EvidenceRevision);
```

ProductionOrder default `false`.

Изменение на `true` требует:

- отдельный project;
- evidence ID;
- accepted release;
- owner gate.

---

# 10. Реализация S4: одна tiny MEXC Spot сделка

## 10.1. Preconditions

До кода `/api/v3/order`:

- P0 закрыты;
- 2-hour memory soak pass;
- repository protected;
- safe build physical boundary;
- read-only private acceptance pass;
- order-test acceptance pass;
- live-trade API key provisioned separately;
- withdrawal disabled;
- IP restriction where supported;
- official current MEXC docs revalidated;
- exact instrument filters confirmed;
- exact maximum loss/notional approved;
- recovery/reconciliation tested;
- signed/notarized execution artifact.

## 10.2. Separate live credential profile

Не использовать:

- read-only key;
- order-test key.

Создать:

```text
live-spot-api-key
live-spot-secret
```

Требования:

- Spot trade permission only;
- no withdrawal;
- no transfer;
- IP bound if available;
- separate Keychain identity;
- preferably Keychain user-presence access control;
- versioned credential profile;
- masked status only;
- revoke procedure.

## 10.3. Exact execution plan

```csharp
public sealed record ProductionOrderPlan(
    Guid LocalOrderId,
    string ClientOrderId,
    CanonicalInstrument Instrument,
    TradingVenue Venue,
    OrderSide Side,
    OrderSizingMode SizingMode,
    decimal Value,
    decimal EstimatedQuoteExposure,
    string MetadataFingerprint,
    DateTimeOffset MetadataObservedAt,
    string MarketStateFingerprint,
    DateTimeOffset MarketStateObservedAt,
    RiskDecisionSnapshot Risk,
    DateTimeOffset ExpiresAt,
    string PlanFingerprint);
```

После owner confirmation plan immutable.

## 10.4. Owner authorization

- exact plan presentation;
- exact venue;
- exact symbol;
- exact side;
- exact value;
- estimated quote exposure;
- max slippage;
- fee caveat;
- key profile;
- STOP state;
- TTL;
- no hidden defaults.

Owner authorization:

- single-use;
- short-lived;
- local user presence;
- bound to plan fingerprint;
- consumed before network call;
- journaled before send.

## 10.5. Network send policy

- one HTTP send;
- no redirect;
- no cookies;
- fixed host;
- exact endpoint;
- exact parameters;
- `newClientOrderId`;
- bounded response;
- timeout;
- no retry;
- no full URI log;
- no secret/signature log.

## 10.6. Result classification

```text
2xx + valid response       Accepted
known venue rejection      Rejected
network before send        NotSent, only if provable
timeout/reset after write  Unknown
invalid body               Unknown
process crash              Unknown until reconciliation
```

Never map timeout to Rejected.

## 10.7. Reconciliation

After `Unknown`:

- engage STOP;
- query by client order ID / open orders / account using official supported endpoint;
- no new order;
- record every observation;
- owner sees unresolved state;
- only deterministic evidence resolves;
- manual close requires reason and cannot pretend exchange rejection without evidence.

## 10.8. S4 acceptance

- exact one instrument;
- exact one side/value;
- extremely small approved cap;
- one real order maximum;
- no retry;
- no second order;
- no cancellation automation;
- no cross-venue;
- full evidence;
- account/balance verification after;
- key revoked or disabled after experiment if intended;
- Founder explicitly accepts result.

---

# 11. Реализация S5: Bybit и Gate

Не копировать MEXC assumptions.

## 11.1. Bybit/Gate отдельные gates

Для каждого venue:

1. current official docs;
2. demo/testnet availability;
3. account mode;
4. authentication;
5. time sync;
6. instrument filters;
7. position mode;
8. leverage/margin;
9. reduce-only;
10. order lifecycle;
11. idempotency/client ID;
12. rate limits;
13. error map;
14. reconciliation;
15. separate credentials;
16. isolated adapter;
17. owner acceptance.

## 11.2. Никакого smart routing на этом этапе

Даже после независимого acceptance нескольких venues:

- UI может показывать comparison;
- execution venue выбирает owner;
- no automatic venue choice;
- no simultaneous orders;
- no synthetic merged book as execution truth.

Smart routing — отдельный значительно более поздний продукт с собственным risk model.

---

# 12. Дополнительные функции, которые улучшат проект

## 12.1. Record/replay market data

Самое полезное следующее исследовательское расширение.

Пользователь получает:

- повтор конкретной рыночной сессии;
- проверку UI без live network;
- воспроизводимые bugs;
- сравнение алгоритмов liquidity/cluster;
- deterministic regression.

Формат:

```text
session metadata
venue
instrument
protocol revision
received timestamp
exchange timestamp
sanitized raw frame or canonical event
sequence
checksum
```

Требования:

- bounded files;
- rotation;
- no private payload;
- no API keys;
- explicit opt-in;
- separate from execution journal.

## 12.2. Execution readiness dashboard

До реальной торговли показать:

```text
APP BUILD             SAFE / EXECUTION
MEMORY SOAK           PASS / BLOCKED
REPOSITORY GATE       PASS / BLOCKED
KEYCHAIN READONLY     STORED
KEYCHAIN ORDER TEST   STORED
KEYCHAIN LIVE         MISSING
TIME SYNC             READY
METADATA              FRESH
BOOK                  LIVE
OPEN UNKNOWN ORDERS   0
STOP                   ON
```

Кнопка execution недоступна, пока все required gates не PASS.

## 12.3. Redacted diagnostic bundle

Пользователь может экспортировать:

- app version;
- OS version;
- architecture;
- venue states;
- safe error codes;
- connection transitions;
- memory counters;
- journal integrity status;
- no secrets;
- no signed URLs;
- no raw private payload;
- no Keychain values.

Перед export — preview.

## 12.4. Watchlists и alerts

Безопасная ценность до live trading:

- watchlist;
- spread/divergence alert;
- liquidity-building alert;
- stale feed alert;
- large trade cluster alert;
- local desktop notification;
- cooldown/dedupe;
- no automatic trading.

## 12.5. Paper portfolio

Отделить от simple order simulation:

- virtual balances;
- fee model;
- fill model;
- slippage model;
- partial fills;
- PnL;
- session performance;
- reproducible replay.

Paper result должен явно называться simulation, не prediction.

## 12.6. Workspace profiles

- selected instruments;
- density;
- cluster interval;
- risk profile;
- notification preferences;
- no credentials in workspace file;
- versioned settings;
- safe migration;
- reset/import/export.

---

# 13. Вопросы, которые проект должен задать себе

## 13.1. Репозиторий должен быть public или private?

Рекомендация: до решения по лицензии, disclosure и execution code — private.

## 13.2. Кто является пользователем?

Сейчас архитектура выглядит как single-owner local desktop app.

Нужно зафиксировать:

- один владелец;
- несколько local users;
- будущая коммерческая дистрибуция;
- cloud account или полностью local.

От этого зависят auth, update, telemetry, licensing и support.

## 13.3. Нужна ли реальная торговля уже в следующем этапе?

Возможно, больший product value сейчас дадут:

- replay;
- alerts;
- better analytics;
- stable multi-venue public data;
- paper trading.

Реальный execution резко повышает security и operational cost.

## 13.4. Что является источником истины order state?

Ответ должен быть:

- local persistent intent;
- exchange evidence;
- reconciliation state.

Не UI label и не один HTTP response.

## 13.5. Что происходит после ambiguous timeout?

Ответ:

- `Unknown`;
- STOP;
- reconciliation;
- no retry;
- no next order.

## 13.6. Каков максимально допустимый ущерб?

До S4 владелец должен явно утвердить:

- maximum single notional;
- maximum daily notional;
- maximum loss;
- allowed instrument;
- allowed side;
- allowed venue;
- expiry;
- number of attempts.

## 13.7. Что произойдёт при двух запущенных приложениях?

До execution ответ должен быть: второй instance fail-closed.

## 13.8. Что произойдёт после crash сразу после POST?

Приложение должно восстановить persisted `Sending/Unknown` и начать read-only reconciliation.

## 13.9. Что произойдёт при stale metadata, но live book?

Order blocked.

## 13.10. Что произойдёт при live metadata, но stale book?

Order blocked.

## 13.11. Что произойдёт при credential replacement посередине?

STOP, no active execution grant, old or new pair deterministic; no mixed pair.

## 13.12. Как доказать отсутствие execution capability в safe build?

Architecture test + assembly manifest + package inspection.

## 13.13. Как обновлять приложение?

Нужны:

- signed/notarized update;
- rollback;
- versioned settings/journal;
- no silent downgrade;
- release notes;
- artifact checksum;
- owner approval.

## 13.14. Что считается telemetry?

До отдельного opt-in:

- никакого remote telemetry;
- только local redacted diagnostics;
- no market raw data/private responses/secrets.

## 13.15. Какие метрики определяют качество?

- feed uptime;
- resync rate;
- stale rate;
- UI latency;
- dropped UI snapshots;
- memory;
- allocation;
- catalog rejection;
- order simulation determinism;
- reconciliation completeness;
- crash-free sessions.

---

# 14. Рекомендуемая очередь PR

## PR-00 — baseline and audit lock

Цель:

- подтвердить новый HEAD;
- запустить clean restore/build/test;
- сохранить test count;
- подтвердить clean worktree;
- создать `docs/audits/2026-08-25-baseline.md`;
- не менять runtime.

Codex не должен переписывать существующие evidence results.

---

## PR-01 — bounded WebSocket message envelope

Файлы:

- Bybit public client;
- Gate public client;
- MEXC public client;
- новый shared bounded accumulator;
- deterministic tests.

Scope:

- full-message limit;
- fragmented messages;
- safe error code;
- no raw payload;
- reconnect/resync;
- no other refactor.

Это рекомендуемая первая кодовая задача.

---

## PR-02 — bounded order book and cluster memory

Scope:

- `OrderBookCapacityPolicy`;
- max levels/update;
- validate-before-apply;
- resync state;
- cluster bucket cap;
- metrics;
- randomized/property tests.

Не менять visual behavior без необходимости.

---

## PR-03 — bounded HTTP transport

Scope:

- shared bounded HTTP reader;
- public metadata/catalog;
- MEXC snapshot;
- content length and streaming;
- endpoint-specific caps;
- tests.

---

## PR-04 — memory observability and soak harness

Scope:

- local counters;
- deterministic feed replay;
- market-switch soak;
- allocation budgets;
- no remote telemetry;
- runbook.

Нельзя назвать memory PASS без реального macOS soak.

---

## PR-05 — async application lifecycle

Scope:

- awaitable initialization;
- cancellation;
- controlled shutdown;
- centralized exception boundary;
- STOP on fatal error;
- tests.

---

## PR-06 — secret input boundary

Scope:

- убрать секреты из long-lived VM strings;
- secure entry lease;
- immediate clearing;
- clipboard policy;
- tests;
- no private call.

---

## PR-07 — atomic Keychain profile replacement

Scope:

- versioned pair;
- candidate write/readback;
- active reference switch;
- rollback;
- profile migration;
- tests.

---

## PR-08 — ViewModel decomposition

Scope:

- shell + child VMs;
- services;
- no domain rule move;
- characterization tests;
- small incremental extraction.

---

## PR-09 — journal v2 and single-instance gate

Scope:

- file lock;
- single process;
- hash chain/HMAC foundation;
- streaming append;
- fsync;
- replay;
- crash injection;
- migration/backup.

Пока применить к simulation; execution reuse позже.

---

## PR-10 — CI and repository code controls

Code changes:

- `Directory.Build.props`;
- central package management;
- analyzers;
- format;
- coverage;
- architecture tests;
- CodeQL workflow;
- dependency review;
- macOS build job;
- SBOM.

Owner separately enables branch protection and repository security settings.

---

## PR-11 — execution-neutral contracts

Scope:

- `Trdng.Execution.Contracts`;
- state machine;
- immutable order plan;
- risk snapshot;
- no network adapter;
- no `/api/v3/order`;
- tests.

---

## PR-12 — execution package isolation

Scope:

- separate project/package profile;
- architecture reference tests;
- safe build manifest;
- execution build remains disabled;
- no real endpoint.

---

## PR-13 — trusted private-read acceptance tooling

Scope:

- redacted readiness screen;
- safe manual smoke workflow;
- no secret output;
- no automatic call;
- owner-run only.

---

## PR-14 — accepted `/order/test` smoke

Only after explicit owner approval.

Scope:

- one exact candidate;
- one call;
- no retry;
- masked evidence;
- key revoke;
- no production order code.

---

## PR-15 — S4 preflight/reconciliation dry implementation

Scope:

- production-grade risk policy;
- persistent unknown state;
- reconciliation fixtures;
- fake venue adapter;
- crash tests;
- no real MEXC order endpoint.

---

## PR-16 — S4 real MEXC adapter

Создавать только после отдельного security review и owner gate.

Scope должен быть минимальным:

- exact one market order endpoint;
- one venue/product;
- no cancel;
- no withdrawal;
- no transfer;
- no retry;
- separate execution artifact;
- full reconciliation;
- tiny hard cap.

---

# 15. Первый готовый промт для Codex

```text
Работаем в VibeSafrCode/TRDNG.

Опорный audit HEAD:
af86d2f969d75c84cc8518860be50e23b0776faf

Сначала:
1. Проверь текущую ветку и HEAD.
2. Прочитай полностью:
   - README.md
   - CONTRIBUTING.md
   - SECURITY.md
   - docs/source-of-truth.md
   - docs/ARCHITECTURE.md
   - docs/stage-1-plan.md
   - docs/stage-1-ledger.md
   - TRDNG_FRESH_AUDIT_CODEX_PLAN_2026-08-25.md
3. Не выполняй push, PR, merge, package release, private request,
   /api/v3/order/test или money action.
4. Не используй настоящие credentials.
5. Не добавляй /api/v3/order, cancel, withdrawal, transfer или smart routing.

Выполни только PR-01:
Bounded WebSocket message envelope.

Root cause:
BybitPublicOrderBookClient и GatePublicMarketDataClient добавляют WebSocket
fragments в ArrayBufferWriter без проверки общего размера. MEXC имеет отдельный
1 MiB cap, но реализация не общая.

Требования:
- создать минимальный reusable bounded message accumulator/reader;
- суммарный payload должен иметь явный hard limit;
- arithmetic должен быть overflow-safe;
- limit должен проверяться до увеличения buffer;
- ровно limit разрешён, limit+1 отклонён;
- oversized message не должен попасть в parser;
- raw payload не должен попадать в exception, diagnostic или UI;
- после reject connection должна fail closed и перейти в reconnect/resync;
- accumulator должен очистить состояние;
- применить единый контракт к Bybit, Gate и MEXC без изменения venue protocol;
- сохранить текущую cancellation/disposal семантику;
- не выполнять несвязанный рефакторинг MainViewModel;
- не менять order/risk/credential code.

Тесты:
- single-frame text;
- fragmented text;
- fragmented binary;
- exact boundary;
- boundary + 1;
- empty fragments;
- buffer reset;
- no partial parse;
- no raw payload in error;
- reconnect/resync behavior;
- cancellation.

Проверка:
- dotnet restore Trdng.slnx
- dotnet build Trdng.slnx --configuration Release --no-restore
- dotnet test tests/Trdng.Core.Tests/Trdng.Core.Tests.csproj \
    --configuration Release --no-build --no-restore
- git diff --check
- поиск новых secret-bearing строк и production order endpoints

Финальный ответ Codex:
- baseline HEAD;
- изменённые файлы;
- выбранный limit и обоснование;
- exact error behavior;
- тесты и результаты;
- что не запускалось;
- compatibility risks;
- rollback;
- не начинай PR-02.
```

---

# 16. Definition of Done для каждого PR

- [ ] Scope соответствует одному PR из этого документа.
- [ ] Нет real private call без отдельного owner gate.
- [ ] Нет настоящих credentials.
- [ ] Нет `/api/v3/order` вне отдельно одобренного S4 PR.
- [ ] Нет automatic retry ambiguous private write.
- [ ] External payload bounded.
- [ ] Collections/state bounded либо есть объяснённый invariant.
- [ ] STOP fail-closed.
- [ ] Cancellation проверена.
- [ ] Concurrent/replay behavior проверен.
- [ ] Raw remote payload не попадает в UI/log.
- [ ] Secrets/signatures не попадают в UI/log/test/evidence.
- [ ] Deterministic tests добавлены.
- [ ] Release build проходит.
- [ ] Test count не уменьшился без объяснения.
- [ ] Diff review выполнен.
- [ ] Evidence честно разделяет PASS/NOT RUN/BLOCKED.
- [ ] Rollback описан.
- [ ] Документация актуализирована.
- [ ] Push/merge/release выполнены только после owner approval.

---

# 17. Immediate owner actions

Эти действия не следует поручать Codex как обычную code-задачу.

1. Решить, должен ли репозиторий быть public.
2. Если нет — немедленно вернуть private.
3. Включить branch protection/ruleset для `main`.
4. Включить GitHub Private Vulnerability Reporting.
5. Подтвердить secret scanning и push protection.
6. Проверить Git history на secrets после public exposure.
7. Выбрать license либо proprietary notice, если repository остаётся public.
8. Не auto-merge пять Dependabot PR.
9. Зафиксировать memory anomaly как blocker до S4.
10. Утвердить численные S4 risk limits.
11. Не создавать live-trading key до прохождения private read и order-test acceptance.
12. Проверить restore внешнего Git bundle/archive — документация пока говорит, что restore NOT RUN.

---

# 18. Итоговая рекомендация

TRDNG имеет сильное безопасное направление: текущий код физически не содержит production order flow, отдельные credential profiles и owner-gated order-test foundation реализованы аккуратнее, чем в типичном раннем прототипе.

Сейчас важно не разрушить это преимущество быстрым добавлением одной кнопки реальной торговли.

Правильная последовательность:

```text
bounded external input
-> bounded memory
-> memory soak
-> lifecycle
-> secret input
-> journal/single instance
-> repository/release gates
-> private read acceptance
-> order-test acceptance
-> execution-neutral reconciliation
-> separate execution artifact
-> одна tiny S4 сделка
```

До завершения этой последовательности продукт следует позиционировать как:

- multi-venue public market-data terminal;
- analytical terminal;
- dry-run/paper execution research environment;
- private-path foundation without live acceptance.

Это уже самостоятельная полезная стадия продукта.

Реальная торговля должна появиться только как отдельно принятая capability, а не как естественное продолжение существующей кнопки dry-run.
