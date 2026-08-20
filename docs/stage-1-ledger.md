# TRDNG Stage 1 — sprint ledger

Назначение: краткий append-only журнал исполнения утверждённого плана
[`stage-1-plan.md`](stage-1-plan.md). Scope и порядок спринтов здесь не
переопределяются.

Допустимые статусы доказательств: `VERIFIED`, `PENDING`, `NOT RUN`, `BLOCKED`.

## S0 — CLOSED_WITH_ACCEPTED_RISK

Baseline зафиксирован: 2026-08-02 11:31 +05:00.

| Объект | Подтверждённый факт | Статус |
|---|---|---|
| Workspace | Корень `/Users/safr.nikita/Documents/AI OS SAFR/02 Projects/TRDNG` доступен; исходники, тестовые проекты, документы и локальные артефакты присутствуют | VERIFIED |
| Platform | SDK `10.0.302`, macOS arm64 | VERIFIED |
| Solution | `Trdng.slnx` перечисляет четыре проекта `src` и один тестовый проект | VERIFIED |
| Git baseline | В корне нет подтверждённой Git metadata; branch, HEAD, status и diff определить нельзя | BLOCKED |
| Tests | Release: 43/43 passed | VERIFIED |
| Release build | Завершён без ошибок | VERIFIED |
| Fresh package | Единственный `artifacts/TRDNG.app` обновлён из свежего self-contained arm64 publish | VERIFIED |
| Package integrity | SHA-256 main DLL в publish и `.app` совпали; strict ad-hoc signature check прошёл | VERIFIED |
| App smoke / visual QA | Startup и live QA пройдены; оба публичных потока показали `LIVE` | VERIFIED |
| Visual evidence | [`evidence/s0-package-startup.png`](evidence/s0-package-startup.png) и [`evidence/s0-package-live.png`](evidence/s0-package-live.png) существуют | VERIFIED |
| Initial memory sample | `ps` RSS около 165 MB; macOS footprint около 3.34 GB, включая около 3.18 GB untagged `VM_ALLOCATE` | VERIFIED |
| Later memory sample | PID 13979: RSS 429216 KB, VSZ 444010272 KB | VERIFIED |
| Process cleanup | Ассистент завершил PID 13979 через SIGTERM; повторный `ps` не нашёл TRDNG/Avalonia | VERIFIED |

### S0 acceptance

| Slice | Результат | Основание |
|---|---|---|
| S0.1 Baseline | PASS | SDK/platform, 43/43 tests, Release build, fresh package и integrity checks |
| S0.2 Visual QA | PASS | Startup/live screenshots; оба публичных потока `LIVE` |
| S0.3 Memory guard | WAIVED / ACCEPTED_WITH_RISK | Founder разрешил пропустить расследование; причина `VM_ALLOCATE` не локализована |

### S0 blockers и отклонения

- Commit gate заблокирован до отдельного решения Founder о Git metadata.
- Принят остаточный memory risk: причина высокого physical footprint и
  `VM_ALLOCATE` не локализована.
- Триггер возврата к расследованию — повторный высокий physical footprint. Тогда
  собрать RSS + footprint + `vmmap` и безопасно остановить процесс.
- Backup, restore, commit и release не выполнялись и не заявляются выполненными.

## S1.1 Canonical instruments — COMPLETED / PASS

- Owner: ведущий.
- Token mode: `LOW`.
- Evidence: [`s1.1-evidence.md`](s1.1-evidence.md); перечень изменённых файлов
  находится там же.
- Release tests: 53/53 passed, 0 failed, 0 skipped.
- Release build `Trdng.Desktop`: success, 0 warnings, 0 errors.
- Реализованы canonical `BASE/QUOTE`, изоляция `Spot`/`Perpetual`, mappings и
  capability states для MEXC/Gate/Bybit, стартовые APT/USDT и BTC/USDT.
- MEXC Perpetual private trading остаётся `Blocked`; доступность торговли нигде
  не заявлена.

## S1.2 MEXC public foundation — COMPLETED / PASS

- Owner: ведущий.
- Scope: MEXC public Spot foundation.
- Token mode: `STANDARD`, одобрен Founder 2026-08-10.
- Filesystem access blocker: `RESOLVED`; Ассистент подтвердил чтение
  `src`/`tests`/`docs`.
- Initial bridge: snapshot принимается при delta `[from,to]`, включающей
  snapshot sequence, либо при `from == snapshot + 1`; истинный gap
  `snapshot + 2` вызывает resync.
- Live acceptance: 1/1 PASS за 4 s; получены реальные APTUSDT metadata и
  двухсторонний Spot book.
- Release tests: 76/76 passed, 0 failed, 0 skipped.
- Release build: success, 0 warnings, 0 errors.
- MEXC Spot market-data capability для APT/BTC: `Available`; UI, private API и
  trading отсутствуют.

## S1.3 Asset selector — IN_PROGRESS / IMPLEMENTATION PASS / VISUAL GATE OPEN

- Owner: ведущий.
- Token mode: `STANDARD`, одобрен Founder.
- Scope: asset selector и три venue cards.
- Текущий product явно обозначается как `PERPETUAL`.
- MEXC для `PERPETUAL` отображается unavailable до S1.4.
- Evidence: [`s1.3-evidence.md`](s1.3-evidence.md).
- Release tests: 81/81 passed; Release build: 0 warnings, 0 errors.
- `artifacts/TRDNG.app` main DLL SHA-256:
  `1060171dde376272cc1c7e1f59dc003a361780dfc2e10fa67207e1a8299fe7a9`;
  strict codesign verification: PASS.
- App с точным hash запускался; RSS 180624 KB на 9 s; процесс закрыт, зависших
  TRDNG/Avalonia процессов нет.
- Visual QA/screenshot: `BLOCKED`. Computer Use отсутствовал, `screencapture`
  заблокирован; TCC не менялся.
- Owner override / proceed decision: Founder разрешил продолжить без Screen
  Recording. Visual gate S1.3 остаётся `OPEN` как долг, S1.3 не закрыт, но долг
  больше не блокирует код.

## S1.4 SPOT / FUTURES — IN_PROGRESS / IMPLEMENTATION-BUILD-PACKAGE PASS

- Owner: ведущий.
- Token mode: `STANDARD`, одобрен Founder.
- Scope остаётся без изменений в [`stage-1-plan.md`](stage-1-plan.md).
- Evidence: [`s1.4-evidence.md`](s1.4-evidence.md).
- Implementation, Release build и package: PASS; build завершён с 0 warnings и
  0 errors.
- Независимый in-process `Fact` / `Theory` / `InlineData` fallback: 85/85 PASS,
  0 failed/skipped. Это не официальный `dotnet test`.
- Official VSTest: `OPEN / BLOCKED` локальным socket sandbox.
- Final app main DLL SHA-256:
  `c7e1ff56176dba254ca5cdc9be35b7a3d3bb95151e17455b7cb99a8d329f0f67`;
  strict codesign verification: PASS.
- Exact-app startup/RSS/stop: `OPEN / BLOCKED`; запуск не состоялся из-за
  approval-review timeout.
- Visual QA/screenshot: `OPEN / BLOCKED`; Founder ранее разрешил продолжать без
  Screen Recording, долг остаётся незакрытым.
- S1.4 полностью не закрыт. Новый scope не назначен.

## S1.5 Three-book acceptance — IMPLEMENTATION-BUILD-PACKAGE PASS / VISUAL OPEN

- Founder одобрил `STANDARD`; evidence: [`s1.5-evidence.md`](s1.5-evidence.md).
- Стабильные независимые колонки: `MEXC → Gate → Bybit`; product isolation и
  capability/empty states реализованы без merge/smart routing.
- Аудитом исправлены и проверены: MEXC tracker reset; stale exclusion из shared
  scale с transition-only rebuild; stale exclusion из consensus/no-data reset.
- Новые deterministic tests только компилируются; runtime не подтверждён.
  Official VSTest `BLOCKED` после одной попытки: sandbox local socket,
  `SocketException (13)`.
- Full Release build: 0 warnings / 0 errors. Existing app обновлён; DLL SHA-256
  `24576d88e1e6e4c6c3c9da340539018a48f585c0f596a91a8c5a2e2664ae378f`;
  strict codesign: PASS.
- GUI/visual/resize/trackpad/soak: `OPEN / NOT RUN` по границе спринта.
- Local Git baseline: `dbb7fc1` (`chore: establish TRDNG stage 1 baseline`),
  branch `main`; GitHub/push отсутствуют.
- S1.5 source changes остаются uncommitted. Commit — `PENDING OWNER GATE`:
  Founder разрешил только первый контрольный commit. Rollback baseline —
  `dbb7fc1`, это не backup; commit/rollback не выполнялись.

## S2.1 Dry-run order model — IMPLEMENTATION-BUILD-PACKAGE PASS / RUNTIME OPEN

- Founder одобрил `STANDARD`; evidence: [`s2.1-evidence.md`](s2.1-evidence.md).
- Реализованы single-venue MARKET intent, explicit qty/notional semantics,
  capability/filter validation, deterministic client ID и simulation-only UI.
- Аудитом исправлены capability policy, strict parser, client ID policy,
  quote-step validation, filter coherence и rejected/active venue reporting.
- Test assembly компилируется; runtime tests `NOT RUN`, official VSTest не
  повторялся из-за известного IPC blocker. Full Release build: 0 warnings / 0
  errors.
- Existing app SHA-256:
  `e34c0409dedb04f824ca448c31f2fa5c282af31ddefb555f5c0c3c12d94a848a`;
  strict codesign: PASS. GUI/visual: `OPEN / NOT RUN`.
- S1.5 baseline перед S2.1: `main` commit `f1ab367`; S2.1 diff оставлен
  uncommitted. S2.1 commit: `PENDING OWNER GATE`.

## S2.2 Risk gates — IMPLEMENTATION-BUILD-PACKAGE PASS / RUNTIME OPEN

- Founder одобрил `STANDARD`; evidence: [`s2.2-evidence.md`](s2.2-evidence.md).
- Реализованы fail-closed caps/fresh-price policy, startup STOP, exact TTL
  confirmation, single-use/replay guard и bounded in-memory audit.
- Production limits `UNCONFIGURED`; UI profile явно `SIMULATION ONLY`.
- Аудитом исправлены exact validation evidence, Production denial,
  overflow/max-age, BUY ask / SELL bid и nullable no-intent audit.
- Test assembly компилируется; runtime/VSTest: `NOT RUN`. Full Release build: 0
  warnings / 0 errors.
- Existing app SHA-256:
  `699c9427ec653e7f8d39c921948cbc571e0cca3092c66a722930f71bb25d56f7`;
  strict codesign: PASS. GUI/visual: `OPEN / NOT RUN`.
- Baseline `main 0d8ed0b`; S2.2 diff оставлен uncommitted. Commit:
  `PENDING AUDIT CLOSURE`.

## S2.3 Dry-run reconciliation — IMPLEMENTATION-BUILD-PACKAGE PASS / RUNTIME OPEN

- Founder одобрил `HIGH`; evidence: [`s2.3-evidence.md`](s2.3-evidence.md).
- Реализованы lifecycle/idempotency/Unknown reconciliation, deterministic fault
  adapter, crash-safe bounded simulation journal и restart recovery со STOP.
- Scope строго local simulation: STOP/selection очищают active pointer, сохраняя
  history; journal I/O/corruption блокирует текущую сессию fail-closed.
- Truncated tail обрезается до append; sequence меняется после successful append.
  Reconciliation evidence structured, exact и fresh; recovered pending становится
  `Unknown` со STOP; duplicate audit не мутирует journal.
- Независимый аудит кода пройден без новых блокеров.
- Test assembly compile: PASS; official VSTest: `NOT RUN` из-за известного IPC
  blocker. Full Release build: 0 warnings / 0 errors.
- Existing app SHA-256:
  `2c023b538098612f01e0514742c5002c4755901e5f5a701aa6be766eb7ccdaea`;
  strict codesign: PASS. GUI/visual и file-journal runtime smoke: `NOT RUN`.
- Нет keys/private API/network orders/money.
- Baseline `main 4d32ccb`; локальный S2.3 commit фиксирует этот проверенный
  changeset; точный hash хранится в истории Git. GitHub/push не выполняются.
- S3.1 не начинать до отдельного решения Founder.

## S3.1 macOS Keychain boundary — IMPLEMENTATION-BUILD-PACKAGE PASS / NATIVE RUNTIME OPEN

- Founder одобрил `STANDARD`; evidence: [`s3.1-evidence.md`](s3.1-evidence.md).
- Реализованы Core vault contract, нативный `Security.framework` adapter,
  masked presentation и bounded redacted audit без credential-entry UI.
- Test assembly compile и full Release build: PASS, 0 warnings / 0 errors.
- Native synthetic Keychain smoke выполнен ровно один раз: `STORE=Error`, cleanup
  `REVOKE=Error`, exit `2`; bytes не печатались и были zeroed в `finally`.
  Повтор не выполнялся. Native acceptance: `BLOCKED`.
- Interop-only diagnostic (без SecItem) прошёл: Security symbols и CF objects
  исправны. Добавлены allowlisted failure code/stage; остаются signed-host context
  и file-based/data-protection query contract hypotheses. Evidence:
  [`s3.1-native-diagnostic.md`](s3.1-native-diagnostic.md). Publish не выполнялся.
- Signed-app smoke №1 доказал `InvalidParameter/SecItem` для store и cleanup
  revoke. Разрешённо удалён `kSecAttrAccessible` из default file-based add query;
  Data Protection/access groups/entitlements не добавлялись. Финальный signed-app
  smoke с новой identity вернул ту же категорию и exit `2`. S3.1 остаётся
  `BLOCKED`, publish/commit не выполнялись, S3.2 не начат.
- Exact query audit без SecItem: status/read/add dictionaries имеют ожидаемые
  counts, exact keys, CF types и retained lifetimes; P/Invoke совпадает с local
  Apple SDK headers. Result `SUCCESS=True/CODE=PASS`. Следующий discriminator —
  native Swift/Objective-C reference harness vs C# wrapper; он не запускался.
- Phase R: минимальный signed Objective-C reference `.app` выполнен ровно один
  раз и вернул `ADD=INVALID_PARAMETER`, cleanup `DELETE=INVALID_PARAMETER`, exit
  `2`. Следовательно, blocker воспроизводится вне .NET; adapter/shim не заменялся,
  publish/commit/S3.2 не выполнялись.
- Независимый внешний gate вне Codex sandbox supersede предыдущий вывод: текущий
  signed C# harness дал Store/Read(match)/Revoke PASS, exit `0`; native Objective-C
  reference также PASS. Root cause прежнего `InvalidParameter` — Codex sandbox.
  S3.1 native acceptance: `PASS`; shim не нужен.
- Existing app SHA-256:
  `949922dae0747d504cc62aaaa09d54426b94173b5166db2f4c067287aafc0760`;
  strict codesign: PASS.
- Security-audit blockers closed: revoke semantics, cached native symbols,
  16 KiB cap with fail-closed native read pointers, allowlisted audit codes and
  STOP-gated `Stored` two-step revoke with 8 s TTL.
- Independent security/acceptance audit: PASS after two corrections. Desktop
  status/revoke use `AuditedCredentialVault`; all four actions have bounded
  allowlisted audit, and undefined venue identities are rejected.
- Diff check: PASS.
- Нет real credentials/private API/network orders/money. S3.2 не начат.
- Baseline `main cdfa8f2`; local S3.1 commit фиксирует этот проверенный
  changeset, точный hash хранится в Git history. GitHub/push не выполняются.

### Acceptance update

## S3.2 MEXC Spot read-only private foundation — IMPLEMENTED / AUTH NOT RUN

- Official Spot V3 contract checked 2026-08-14; evidence:
  [`s3.2-evidence.md`](s3.2-evidence.md).
- Canonical HMAC signing, bounded fresh time offset, typed Keychain leases,
  GET-only account/openOrders client and masked audit/UI added.
- Repeat-audit corrections: streaming max+1 cap, owned no-redirect production
  handler, checked/thread-safe time state, thread-safe bounded audit, strict ASCII
  API-key/header gate and typed signing/build exception boundary.
- No credentials, authenticated smoke, POST/DELETE, orders, money or S3.3.
- Test assembly compile: PASS. Official VSTest: NOT RUN (known IPC blocker).
- Final Release build/publish: PASS, 0 errors; sandbox-only `NU1900` warning.
- Existing app SHA-256:
  `200d96bcf1267340eda91c93f1df0b3e05b71249b812aa014809373b550dd7bd`;
  strict codesign: PASS.
- Independent repeat audit: ACCEPTED. S3.2 committed locally at
  `d3151dc2d4619a71f60195ffc773047549db1270`; worktree clean after commit; no
  GitHub/push. Runtime VSTest, authenticated smoke, GUI and real-key evidence
  remain open.

## Pre-key readiness — ACCEPTED / METADATA BLOCKER REMAINS

- Official MEXC Spot V3 exchangeInfo checked 2026-08-19 plus exactly one public
  APTUSDT/BTCUSDT smoke; evidence:
  [`pre-key-readiness-evidence.md`](pre-key-readiness-evidence.md).
- Metadata mapper and passive preflight fail closed on missing/stale/mismatched/
  unsupported evidence. Live payload cannot prove quoteOrderQty support or SELL
  max/step, so eligibility honestly remains `NeedsMetadata`.
- Secure provisioning runbook records exact service/four Keychain accounts and
  preserves read-only vs SPOT_DEAL_WRITE separation.
- No credentials/private network/order/money/S4. Independent audit: ACCEPTED;
  committed locally at `93f67922d7a932412107efb53f98567698a7860b`;
  worktree clean after commit; no GitHub/push. Metadata blocker, runtime VSTest,
  GUI, authenticated smoke and real-key evidence remain open.
- Final Release build/publish: PASS, 0 errors; one sandbox-only `NU1900`.
- Signed app executable SHA-256:
  `43d5a85ca49d3893b29cbfc45d002a53b911eda6d0cb6a3f7f3e1f13651ebc0c`;
  strict codesign: PASS.

## Pre-key validation probe — ACCEPTED / OWNER ACTION REQUIRED

- Separate bounded probe candidate/authorization and exact validation evidence;
  no conversion to production filters, S4 authorization or production route.
- STOP, metadata/reference freshness, tiny quote cap, distinct key state, time
  sync, exact fingerprint, TTL and replay protection fail closed.
- Keychain Access GUI-only runbook documents the exact service and four separate
  accounts without secret-bearing CLI/env/file/chat examples.
- No credentials/private request/order/money/S4. Independent repeat audit:
  ACCEPTED; committed locally at
  `10ca5166b66172bc68599b37e13ab43e2778be98`; worktree clean after commit; no
  GitHub/push. VSTest runtime, GUI and authenticated private probe remain NOT RUN.
- Evidence: [`pre-key-validation-probe-evidence.md`](pre-key-validation-probe-evidence.md).
- Repeat-audit corrections bind execution to an injected atomic STOP, consume
  authorization on every attempt, require exact `{}` success, linearize owner
  authorization and record separate candidate/wire-body fingerprints.

После финального отчёта ведущего сюда вносится только фактическое evidence:
команда/метод, время, результат, exit code или метрика и путь к артефакту.
Отсутствующее доказательство остаётся `NOT RUN` или `PENDING`.

## S3.3 MEXC Spot order-test foundation — IMPLEMENTED / AUTH NOT RUN

- Official Spot V3 contract checked 2026-08-14; evidence:
  [`s3.3-evidence.md`](s3.3-evidence.md).
- Dedicated allowlisted `POST /api/v3/order/test` with exact BUY quote-notional
  and SELL base-quantity mapping is implemented.
- Exact confirmation, bound filter validation, simulation risk evidence and
  single-use authorization are required; unsupported combinations fail closed.
- `/order/test` requires a distinct `SPOT_DEAL_WRITE` Keychain identity pair;
  S3.2 read-only credentials are never read by this path or shared with it.
- No credentials, authenticated smoke, production orders/cancel/withdraw,
  simulation Fill mutation or money path.
- Test assembly compile: PASS. Official VSTest: NOT RUN (known IPC blocker).
- Final Release build/publish: PASS, 0 errors; one sandbox-only `NU1900`.
- Signed app executable SHA-256:
  `72c2f1f644717a8f5c726a80b7ad70c9cda82479b222ec8968b7df4d8362a08a`;
  strict codesign: PASS.
- Independent repeat audit: ACCEPTED. S3.3 committed locally at
  `88f11ba796b8855953c5fec241a47c313ed51dd9`; worktree clean after commit; no
  GitHub/push. Runtime VSTest, authenticated smoke, GUI and real-key evidence
  remain open.

## Terminal repository publication — ACCEPTED

- Private, no-license repository recreated from terminal-only root
  `5780ef66b20143e918e1d88399bfe985b0c1287e`.
- Portable deterministic-test fix published at
  `3e9d9e2cfc1ab0c3dffc54aa6cb3646e4c374966`.
- CI run `32235655100`: Release build PASS; official tests 245/245 PASS.
- Fresh-clone excluded path/content scans: zero; worktree clean.
- Recovery evidence exists externally; restore remains NOT RUN.

## In-app Keychain entry — ACCEPTED / PUBLISHED

- Two isolated MEXC profiles (`READ-ONLY`, `ORDER TEST`) store exact credential
  pairs only through the existing audited Keychain vault.
- Pair status, STOP gate, explicit replacement, rollback, field/buffer clearing,
  two-step revoke and profile isolation are implemented fail closed.
- No credentials, authenticated private request, order-test call, production
  order or money path was used.
- Targeted test assembly compile and final Release build: PASS, 0 warnings/errors.
  Local VSTest runtime hit the known IPC denial.
- Existing app updated once; DLL SHA-256
  `c203406a68f98632ae6898af8cd892585d811b981aa10c060a3cdbc9706212d4`;
  strict codesign PASS.
- Independent repeat security/code audit: ACCEPTED. Implementation commit
  `0cd39cebff2f7536096e393fab2d8c00490e370d` pushed to private `main`.
  Official GitHub CI `32373293894`: build PASS; tests 254/254 PASS, 0 failed,
  0 skipped. GUI/authenticated private requests/real-key smoke remain NOT RUN.
- Evidence: [`in-app-keychain-entry-evidence.md`](in-app-keychain-entry-evidence.md).

## S1.6 dynamic catalogs + book contrast — ACCEPTED / PUBLISHED

- Credential-free official MEXC Spot and Gate/Bybit USDT Perpetual catalogs now
  drive one bounded canonical search and exact per-venue availability.
- Search is capped at 40 results; product isolation, exact symbols,
  latest-selection lifecycle and unsupported cards remain fail closed.
- Public SOL/USDT smoke: MEXC Spot plus Gate/Bybit Perpetual catalogs and 5×5
  books PASS. No credentials/private/auth/trading action.
- Requested six ask/bid template colors applied; quantity remains white.
- Test assembly and final Release build: PASS, 0 warnings/errors. The one local
  official VSTest attempt hit the known IPC blocker and was not retried.
- Existing app republished once; DLL SHA-256
  `494f69ae16b78a3dfac8a7b7a1f6d28e0db1363ef597cfd213c377383922a596`;
  strict codesign PASS. Evidence: [`s1.6-evidence.md`](s1.6-evidence.md).
- Independent repeat audit: ACCEPTED; no open P1/P2. Implementation commit
  `8a3c963eb3f0cc370c6fe5c4d4469fd0eb1eb364` pushed to private `main`.
  Official GitHub CI `32380818244`: build PASS; 262/262 tests PASS, 0 failed,
  0 skipped. GUI/private/auth/order actions remain NOT RUN.

### Post-publication runtime correction — ACCEPTED / PUBLISHED

- Loading/error are no longer overwritten as stale before a catalog observation;
  expected venue parse/network failures are isolated into an honest partial union.
- One final-method full-catalog smoke: MEXC `INVALID_METADATA` (0), Gate
  `NETWORK` (0), Bybit PASS (724); union 724 and APT/USDT Perpetual initial
  selection PASS. No retry.
- Corrected Release build/package/codesign PASS; current DLL SHA-256
  `31ee8de7f4123f642a9bc1c33f3b2eefb3f7ee42630e320b979abec7f72d7c09`.
  Independent audit accepted the exact correction.
- MEXC closure: one diagnostic proved 1987 eligible, 107 ineligible and 15
  non-canonical entries. Typed per-entry rejection then produced one final
  acceptance result: 1987 valid / 122 rejected, with exact valid symbols retained
  and honest partial state driven by the 15 invalid eligible-looking entries, not
  by 107 normally filtered ineligible entries. Exact duplicate mappings may be
  retained; a conflicting symbol quarantines the whole canonical pair for that
  batch. No retry; root/all-invalid remain venue failures.
- Commit `ae0b6a52a55d7c94d68417f4e68412914ae72e63` was pushed to private `main`.
  Official GitHub CI `32383296540`: Release build PASS, 0 warnings/errors;
  269/269 tests PASS, 0 failed, 0 skipped.
