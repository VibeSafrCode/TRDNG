# Product direction inspired by Steve Jobs

Date: 2026-07-29

## What the research actually supports

This is not an attempt to claim what Steve Jobs would literally design in
2026. It translates documented product principles into decisions for a
trackpad-first scalping terminal.

### Start with the experience, then choose technology

At WWDC 1997, Jobs argued for starting with the customer experience and working
backward toward technology. For this product, the primary experience is:

> See liquidity, understand aggression, place or cancel one intentional order,
> and verify the resulting position without moving attention between windows.

Source:
<https://andreatelatin.wordpress.com/wp-content/uploads/2011/07/steve_jobs_wwdc_1997.pdf>

### Focus means removing good features

The practical implication is one excellent trading surface before multi-exchange
coverage, screen recording, social features, complex dashboards, or dozens of
chart types.

First surface:

- one instrument;
- one DOM;
- one cluster view;
- one position;
- one obvious connection/trading state.

### Simplicity is achieved by resolving complexity

The product should not hide important trading state. It should move operational
complexity behind clear defaults:

- automatic snapshot/resync;
- instrument filters applied automatically;
- one-way position mode;
- credentials in Keychain;
- reconnect reconciliation;
- compact status instead of a settings-heavy dashboard.

Secondary discussion:
<https://www.smithsonianmag.com/arts-culture/how-steve-jobs-love-of-simplicity-fueled-a-design-revolution-23868877/>

### End-to-end responsibility

Market-data correctness, rendering, gestures, order intent, acknowledgement and
position reconciliation form one experience. They cannot be optimized as
unrelated features.

## Proposed interface

### Visual hierarchy

1. Price and liquidity ladder dominate the window.
2. Current position remains visible without opening a separate page.
3. Cluster detail is adjacent to the same price axis.
4. Watchlist is narrow and removable.
5. Settings and diagnostics appear only on demand.

No permanent ribbon, nested toolbars, decorative cards, gradients, marketing
content, or duplicated account metrics.

### Cluster design

Default:

- `Bid × Ask` at each price;
- subtle horizontal imbalance fill;
- one selected interval;
- total volume and delta shown in the header/hover detail;
- large-volume marks only above a user-selected threshold.

This keeps the raw numbers readable. Color supplements the numbers and never
becomes the only signal.

### Trackpad interaction

Apple recommends preserving conventional pointer behavior and respecting that
users can customize secondary click and gestures:
<https://developer.apple.com/design/human-interface-guidelines/pointing-devices>

Proposed mapping:

| Interaction | Result |
|---|---|
| Two-finger vertical scroll over DOM | Scroll price ladder |
| Pinch over DOM | Change price aggregation |
| Primary click on ask while Trade Mode is armed | Place Buy at selected price |
| Primary click on bid while Trade Mode is armed | Place Sell at selected price |
| Secondary click | Context menu; cancel own order at price when applicable |
| Pointer hover | Reveal exact volume, delta and order detail |
| Escape | Immediately leave Trade Mode |

Custom system-wide swipes are avoided because they can collide with navigation
between pages, full-screen apps and Spaces.

### Trade Mode

To reconcile speed with trackpad mis-click risk:

- application starts with Trade Mode off;
- reconnect, symbol change and account change switch it off;
- when off, clicking a price selects/inspects it;
- when armed, clicking a price sends exactly one order intent;
- a persistent but quiet state indicator makes the mode unambiguous;
- duplicate suppression and idempotent client order IDs always remain active.

This is not a configurable financial risk limit. It is an interaction integrity
rule.

## Explicit user decisions

- Bybit;
- BTCUSDT USDT perpetual;
- MacBook M1, 8 GB RAM, 256 GB SSD;
- one-way position mode;
- trackpad-first interaction;
- no user-defined max order, max position or daily-loss limits requested.

## Non-negotiable technical protections

Even without financial limits, production trading still requires:

- duplicate-order suppression;
- no blind retry after ambiguous timeout;
- local order-intent journal;
- unique client order ID;
- stale market-data lockout;
- Trade Mode reset after reconnect/symbol/account changes;
- instrument tick/quantity/notional validation;
- visible Demo/Production distinction;
- API key without withdrawal permission;
- manual Cancel All / flatten control.

