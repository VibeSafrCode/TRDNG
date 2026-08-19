# macOS scalping terminal — Bybit MVP Product Brief

Дата: 2026-07-29  
Статус: Bybit/BTCUSDT/macOS/one-way согласованы; технический прототип начат.
Trackpad-first wireframe предложен. Детали кластерных интервалов, hotkeys и
финальный визуальный дизайн ещё требуют обратной связи.

## Подтверждённые решения

- Платформа: macOS.
- Референс: наблюдаемые рабочие сценарии профессиональных скальперских
  терминалов.
- Первая биржа: Bybit — подтверждено пользователем.
- Первый рынок: Bybit USDT perpetual (`category=linear`) — подтверждено
  пользователем.
- Первый инструмент: `BTCUSDT` — подтверждено пользователем.
- Целевая машина первого этапа: MacBook M1, 8 ГБ RAM, SSD 256 ГБ.
- Первый дистрибутив: Apple Silicon; Intel Mac пока не входит в MVP — рабочая
  граница, выведенная из указанного пользователем M1, но требующая финального
  подтверждения.
- Целевой контур: реальные позиции и реальная торговля.
- Приоритетная аналитика: кластеры и объёмы.
- Позиционный режим: one-way — подтверждено пользователем.
- Основное устройство ввода: MacBook trackpad.
- Пользователь не хочет финансовых max-order/max-position/daily-loss limits.
- Пользователь будет поэтапно дополнять требования.

## Предлагаемый первый рынок

Согласованный первый вертикальный срез:

- Bybit USDT perpetual (`category=linear`);
- один инструмент — `BTCUSDT`;
- Unified Trading Account;
- Apple Silicon, с обязательным контролем памяти и размера локальной истории;
- публичный market data → Demo Trading → production.

Spot, inverse, USDC и дополнительные символы следует оставить за пределами
первого среза.

## Почему production не является первым этапом

Цель включает реальную торговлю, но безопасный путь состоит из трёх ворот:

1. **Read-only:** корректность стакана, trades, кластеров и отображения.
2. **Demo:** размещение/отмена ордеров, позиции, исполнения, reconnect и
   reconciliation без риска для капитала.
3. **Production:** отдельное включение после acceptance tests и явного
   подтверждения пользователя.

Bybit официально предоставляет Demo Trading API. Demo использует
`api-demo.bybit.com`; private WebSocket — `stream-demo.bybit.com`, а публичные
рыночные данные остаются mainnet. WebSocket order entry в demo не поддерживается,
поэтому demo-ордера нужно отправлять через REST.

Источник:
<https://bybit-exchange.github.io/docs/v5/demo>

## MVP: must / should / later

### Must

- Подключение к публичному Bybit linear WebSocket.
- `BTCUSDT` order book с обработкой snapshot/delta.
- Лента публичных сделок.
- Агрегированные объёмы buy/sell по цене и времени.
- Базовые кластеры из потока trades.
- Позиция: side, size, entry, mark, liquidation, unrealized/realized PnL.
- Limit и Market order.
- Cancel одного ордера и Cancel All.
- Reduce-only закрытие позиции.
- Локальное хранение layouts/preferences.
- API key/secret только в macOS Keychain.
- Demo/Production — два явно различимых режима.
- Kill switch и защита от повторной отправки.
- Журнал order intent → acknowledgement → execution.

### Should

- Горячие клавиши Buy/Sell/Cancel/Close.
- Presets размера.
- Post-only.
- TP/SL.
- Показ click-to-ack latency.
- Восстановление после разрыва соединения.
- Детектор рассинхронизации стакана.
- Настраиваемые tick aggregation и фильтр крупных объёмов.

### Later

- Несколько символов одновременно.
- Spot/inverse/USDC.
- Другие биржи.
- Исторические кластеры.
- Комбинированные графики.
- Запись экрана и звука.
- Полный визуальный паритет с любым сторонним терминалом.

## Карта данных Bybit

### Public market data

- WebSocket linear:
  `wss://stream.bybit.com/v5/public/linear`
- Order book topic:
  `orderbook.{depth}.{symbol}`
- Для linear доступны depth 1/50/200/1000 с разной частотой.
- При новом `snapshot` локальный стакан нужно полностью сбросить и построить
  заново; `delta` изменяет локальное состояние.
- RPI orders не видны в публичном API, поэтому отображаемый стакан не является
  абсолютно полным представлением всей ликвидности.

Источники:

- <https://bybit-exchange.github.io/docs/v5/ws/connect>
- <https://bybit-exchange.github.io/docs/v5/websocket/public/orderbook>
- <https://bybit-exchange.github.io/docs/v5/market/orderbook>

### Instrument rules

Перед отправкой ордера приложение обязано получать и применять:

- `tickSize`;
- quantity step;
- min/max order quantity;
- min notional;
- leverage limits;
- текущий trading status.

Bybit предупреждает, что отдельные лимиты размеров периодически меняются.
Нельзя зашивать их в приложение.

Источник:
<https://bybit-exchange.github.io/docs/v5/market/instrument>

### Orders

Первый набор:

- `POST /v5/order/create`;
- amend;
- cancel;
- cancel-all;
- realtime open orders;
- execution history.

Limit требует цену; Market на стороне Bybit преобразуется в IOC limit с
защитой от чрезмерного проскальзывания. `orderLinkId` должен быть уникальным и
использоваться как клиентский idempotency/correlation identifier.

Источники:

- <https://bybit-exchange.github.io/docs/v5/order/create-order>
- <https://bybit-exchange.github.io/docs/v5/rate-limit>

### Private state

Нужны private streams:

- orders;
- executions;
- positions;
- wallet.

REST snapshot должен сверяться с WebSocket state при старте и после reconnect.
Источник:
<https://bybit-exchange.github.io/docs/v5/ws/connect>

## Архитектурные границы

```text
Bybit public WS ──> normalizer ──> order-book state ──> DOM renderer
                              └──> trade aggregator ──> volume/clusters

Keychain ──> request signer ──> risk gate ──> order gateway ──> Bybit REST/WS
                                              │
Bybit private WS ──> account reconciler <─────┘
                          │
                          └──> orders / fills / positions / PnL
```

Компоненты:

- `BybitMarketDataAdapter`
- `OrderBookEngine`
- `TradeClusterAggregator`
- `BybitTradingAdapter`
- `RiskGate`
- `AccountReconciler`
- `CredentialVault`
- `ReplayRecorder`
- `SkiaDomRenderer`

## Production safety gates

Production-переключатель недоступен, пока не пройдены:

- snapshot/delta replay без расхождений;
- reconnect и повторный snapshot;
- clock drift и истёкший `recvWindow`;
- duplicate click / retry после timeout;
- partial fill;
- cancel, который пересёкся с fill;
- stale position после reconnect;
- недостаточная маржа;
- неверный tick/qty step;
- rate limit и временный IP ban;
- аварийный Cancel All;
- crash/restart с восстановлением открытых orders/positions.

Обязательные ограничения первой production-версии:

- allowlist символов;
- максимальный размер позиции;
- максимальный notional одного ордера;
- максимальный дневной убыток;
- cooldown после серии отказов;
- запрет production по умолчанию;
- визуально различимые Demo и Production;
- подтверждение для первого production-включения;
- API key без permission на вывод средств.

## Нефункциональные критерии

Предварительные цели, требующие согласования:

- UI rendering: стабильные 60 FPS как минимум;
- order-book update: без пропуска последовательности;
- market-data reconnect: автоматически;
- пользовательский click → локальный order intent: менее 16 мс;
- click → Bybit acknowledgement: измерять, но не обещать фиксированное значение;
- никакой silent retry для неоднозначного order result;
- секреты отсутствуют в SQLite, JSON, logs и crash reports.

## Открытые продуктовые решения — блокируют дальнейшее UI/trading развитие

1. Как строится кластер: временной интервал, price step, bid/ask split,
   footprint или один delta/volume столбец.
2. Какие hotkeys и order-size presets нужны в первом релизе.
3. Нужны ли leverage, TP/SL и trailing stop в MVP.
4. Принять или изменить предложенный wireframe и состав колонок DOM.
5. Оставляем Avalonia или выбираем другой UI stack после wireframe/benchmark.

Финансовые risk limits пользователь отключил. Технические integrity guards
(duplicate suppression, stale-feed lockout, ambiguous-timeout handling,
instrument validation и production-mode distinction) остаются обязательными.

## Правила последовательности Bybit order book

Официальный контракт Bybit **не утверждает**, что `u` увеличивается на единицу
для каждого сообщения. Поэтому переход `u=10 → u=12` сам по себе не является
доказательством пропущенного delta и не должен вызывать ложный resync.

Надёжные правила:

- после subscribe ждать `snapshot`;
- любой новый `snapshot` полностью заменяет локальный стакан;
- `u=1` означает snapshot после restart сервиса и также полностью заменяет
  локальный стакан;
- после disconnect немедленно удалить локальное live-состояние, reconnect и
  снова ждать snapshot;
- delta до snapshot не применять;
- старые/повторные delta игнорировать;
- parse/protocol error переводит session в resync, а не оставляет потенциально
  повреждённый DOM;
- `seq` показывает относительный порядок генерации данных и позволяет сравнивать
  разные уровни стакана, но документация не объявляет его gap-free sequence.

Источник:
<https://bybit-exchange.github.io/docs/v5/websocket/public/orderbook>

TRDNG проектируется независимо: сторонний исходный код и готовые UI-модули не
используются.

## Ограничения целевого Mac

Для M1/8/256 первый релиз должен:

- не держать неограниченную историю trades/clusters в RAM;
- использовать кольцевые буферы для live data;
- сбрасывать длительную историю на диск порциями;
- иметь настраиваемый срок хранения и очистку локальных записей;
- не загружать стаканы/графики невидимых символов;
- ограничить GPU texture/resource cache;
- собираться нативно под `osx-arm64`, без Rosetta;
- измерять resident memory, allocation rate и размер базы при нагрузочном тесте.

## Следующий согласуемый шаг

После подтверждения рынка и symbol подготовить:

- wireframe главного окна;
- точную модель кластеров и объёмов;
- risk limits;
- протокол acceptance tests;
- решение о source-port или новой Avalonia solution.

Только после согласования этих пунктов начинать реализацию.
