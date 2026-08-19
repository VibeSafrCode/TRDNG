# TRDNG Stage 1 — S0 evidence

Дата: 2026-08-02. Статус: **S0.3 WAIVED / ACCEPTED_WITH_RISK по решению
Founder; это не PASS. Профилирование и исправление отложены.**

## S0.1 Baseline

- Workspace: `/Users/safr.nikita/Documents/AI OS SAFR/02 Projects/TRDNG`.
- macOS 26.5.2 (25F84), Apple Silicon `arm64`.
- Project-local .NET SDK: `10.0.302`.
- Полный test suite: **43/43 passed**, 0 failed, 0 skipped.
- Release build: **success**, 0 warnings, 0 errors.
- Единственный app bundle: `artifacts/TRDNG.app`, version `0.1.0 (1)`, bundle ID
  `com.trdng.terminal`.

## S0.2 Package and visual QA

- Выполнен self-contained publish `osx-arm64`.
- Обновлён только существующий `artifacts/TRDNG.app`; второй `.app` не создавался.
- SHA-256 `Trdng.Desktop.dll` в publish и app совпадает:
  `068cb0e839b507bf64a57217a7cb4d87e79ad2501f3efbad75f33f4ec61db512`.
- Пакет получил локальную ad-hoc подпись; `codesign --verify --deep --strict`
  завершён успешно.
- Визуально подтверждена новая сборка: присутствуют `РАСХОЖДЕНИЕ`,
  `ЭВРИСТИКА`, `ОБЩАЯ ШКАЛА · TICK 0.0001 · OFFICIAL`; Bybit и Gate перешли
  в `LIVE`, данные стаканов появились.
- Доказательства:
  - `docs/evidence/s0-package-startup.png`;
  - `docs/evidence/s0-package-live.png`.

## S0.3 Memory anomaly

Проверялся процесс свежего пакета:

- PID: `13979`;
- elapsed при детальном снимке: `03:48`;
- ранний `ps` RSS: `165248 KB`; VSZ: `437252080 KB`;
- поздняя проверка Ассистента RSS: `429216 KB`; VSZ: `444010272 KB`;
- `footprint`: **3.4 GB physical footprint**, peak 3.4 GB;
- `vmmap`: `VM_ALLOCATE` 4.0 GB virtual, около 3.1 GB swapped, 14,495
  regions; process total 6.7 GB virtual, около 499.9 MB resident и 3.3 GB
  swapped.

Вывод: прежние «40+ GB» нельзя трактовать как RSS — VSZ действительно очень
велик. Однако аномалия не является только безобидным virtual reservation:
`footprint` и swap достигли опасного для MacBook Air 8 GB уровня за несколько
минут. Ограниченный soak остановлен по аварийному порогу. Ассистент завершил PID
`13979` через SIGTERM и подтвердил отсутствие оставшихся TRDNG/Avalonia
процессов.

Причина не локализована достаточно надёжно для безопасной правки S0. Возможные
источники (UI collection churn, rendering/native allocation или сочетание с
UI automation) остаются гипотезами, не диагнозом.

## Acceptance summary

- Build/tests: **PASS**.
- Единственный свежий app запускается: **PASS**.
- Visual QA относится к новой сборке: **PASS**.
- Memory bounded/no dangerous growth: **WAIVED / ACCEPTED_WITH_RISK**.
- Зависшие процессы: **нет**.

## Следующий gate

Если аномалия повторится, немедленно снять RSS, `footprint` и `vmmap`, завершить
приложение при опасном physical footprint и вернуть blocker. Не ждать 40 GB.
Дальнейшее профилирование сейчас запрещено решением Founder.
