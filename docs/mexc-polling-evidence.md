# MEXC perpetual polling evidence

Date: 2026-08-31. Status: `MEASURED / BOUNDED MVP ACCEPTED`.

## Production path measured

The tracked credential-free probe uses the production
`MexcContractInstrumentMetadataClient` and
`MexcContractPublicOrderBookClient`. It resolves the official BTC contract
multiplier, then requests exactly 20 bounded public depth snapshots with the
current 750 ms delay and exits. It records aggregate counters only; no payload,
credential or private endpoint is logged.

Command:

```sh
./.tools/dotnet/dotnet run \
  --project tools/Trdng.MexcPollingProbe/Trdng.MexcPollingProbe.csproj \
  -c Release --no-build --no-restore
```

Observed result:

- symbol: `BTC_USDT`;
- snapshots: 20/20;
- reconnects: 0;
- elapsed: 26,982 ms;
- observed rate: 44.47 requests/minute (about 0.74 snapshots/second);
- probe working set after completion: 70,893,568 bytes;
- process exit: 0 / PASS.

## Decision

The transport is bounded to one request at a time, a five-second HTTP timeout,
a four-MiB maximum response and no overlapping retry loop. At the observed
rate, MEXC contributes less than one update per second to the latest-wins UI
path, whose total render cadence is capped at 10 Hz. It is therefore accepted
for the current public-data MVP and does not explain the prior unbounded UI
queue.

The measured effective rate is not accepted as release-grade execution data for
fast trading. A future product sprint must select and prove an official MEXC
WebSocket continuity contract before enabling real MEXC execution. This cleanup
does not add that transport and does not use credentials, private APIs or
orders.
