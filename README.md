# TRDNG — macOS multi-venue scalping terminal

TRDNG is an independent macOS terminal under active Stage 1 development. It keeps each venue book separate while presenting one canonical asset and product across MEXC, Gate and Bybit. The current build is for research, public market data and strictly local simulation. **Production trading is not implemented or enabled.**

## Current Stage 1 capabilities

- Public books: Bybit and Gate perpetuals; MEXC Spot.
- Canonical APT/USDT and BTC/USDT selection with strict Spot/Perpetual isolation.
- Snapshot/delta continuity, reconnect/resnapshot, stale handling and separate venue liquidity indicators.
- Shared comparison only for fresh comparable books—never merged liquidity or smart routing.
- Dry-run market intents, official-filter validation, simulation-only risk limits, STOP, two-step confirmation, journaled simulation and restart reconciliation.
- macOS Keychain boundary with separate MEXC read-only and order-test identities.
- MEXC read-only account/open-orders foundation; no authenticated smoke has run.
- Single-use MEXC `/api/v3/order/test` validation probe. It requires a trade-enabled key, creates no order, and never enables production filters or trading.

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

Stage 1 is incomplete. Runtime VSTest and GUI evidence gaps remain in the ledger. MEXC Futures private trading is officially blocked; production `/api/v3/order`, cancel, withdrawal, transfer, smart routing and multi-venue execution are absent or forbidden.

No license has been selected. Until the owner chooses one, do not assume permission to redistribute or reuse the source.
