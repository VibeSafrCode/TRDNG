# MacMakeMoney_618 (MMM_618) — macOS multi-venue scalping terminal

MacMakeMoney_618 (`MMM_618`) is an independent macOS terminal under active Stage 1 development. The repository and internal code namespace remain `TRDNG` for compatibility. The terminal keeps each venue book separate while presenting one canonical asset and product across MEXC, Gate and Bybit. The current build is for research, public market data and strictly local simulation. **Production trading is not implemented or enabled.**

## Current Stage 1 capabilities

- Public books: MEXC Spot plus MEXC, Gate and Bybit USDT perpetuals. MEXC
  perpetual data currently uses bounded public REST polling; private futures
  trading remains blocked.
- Dynamic exact public catalogs with bounded search, BTC/USDT as the startup
  selection, APT/USDT as a shortcut, and strict Spot/Perpetual isolation.
- Independent full-height venue books with per-book auto/manual depth, trackpad
  adjustment, visible-side volume scaling and customizable bar colors.
- Snapshot/delta continuity, reconnect/resnapshot, stale handling and separate venue liquidity indicators.
- Shared comparison only for fresh comparable books—never merged liquidity or smart routing.
- Dry-run market intents, official-filter validation, simulation-only risk limits, STOP, two-step confirmation, journaled simulation and restart reconciliation.
- macOS Keychain boundary with separate MEXC read-only and order-test identities.
- MEXC read-only account/open-orders foundation; a later owner-run acceptance
  succeeded after local key replacement, while archival runtime evidence remains
  intentionally masked.
- Single-use MEXC `/api/v3/order/test` validation probe. The first authenticated
  probe was rejected and remains diagnostically open; this endpoint creates no
  order and never enables production filters or trading.

No API keys are included. Never put credentials in source, issues, chat, shell arguments, environment variables, logs or screenshots. See [SECURITY.md](SECURITY.md).

## Repository map

- `src/Trdng.Core` — exchange-neutral instruments, books, simulation and safety.
- `src/Trdng.Bybit`, `src/Trdng.Gate`, `src/Trdng.Mexc` — venue adapters.
- `src/Trdng.Desktop` — Avalonia macOS application.
- `tests/Trdng.Core.Tests` — deterministic domain and protocol tests.

See [architecture](docs/ARCHITECTURE.md), [Stage 1 plan](docs/stage-1-plan.md), and [factual ledger](docs/stage-1-ledger.md).

## Local build

`global.json` pins the SDK. Install a compatible .NET SDK, then run:

```sh
dotnet --version
dotnet restore Trdng.slnx
AVALONIA_TELEMETRY_OPTOUT=1 dotnet build Trdng.slnx -c Release --no-restore
dotnet test tests/Trdng.Core.Tests/Trdng.Core.Tests.csproj -c Release --no-build
```

Live public-market smokes are excluded from normal tests and CI. Private calls, Keychain integration smoke and any production route require separate owner gates and are not contributor workflows.

## Boundaries

Stage 1 is incomplete. The deterministic runtime suite is currently 367/367 PASS; the remaining S1.7 evidence gaps are screenshot-based visual acceptance and quiet-host 15/30/120-minute guarded soaks. MEXC Futures private trading is officially blocked; production `/api/v3/order`, cancel, withdrawal, transfer, smart routing and multi-venue execution are absent or forbidden.

No license has been selected. Until the owner chooses one, do not assume permission to redistribute or reuse the source.
