# Contract: Wallet UI surface for DigiDollar

## Configuration (`DigiByte.Web/wwwroot/appsettings.json` — public, no secrets)

```json
"DigiDollar": { "ViewEnabled": false, "SendEnabled": false }
```

`WriteKey` is removed (research R7).

**Effective visibility**
- `ddVisible = ViewEnabled && network ∈ {mainnet, testnet} && activeWalletHasSeed`
- `ddSendVisible = ddVisible && SendEnabled && backendReachable && deploymentActive`
`ddUnavailableLine = !ViewEnabled && network ∈ {mainnet, testnet} && walletHasSeed && dd.used`

## Routes

| Route | Page | Visible when | Spec |
|---|---|---|---|
| `/digidollar` | `DigiDollar.razor`: balance (confirmed + pending), oracle price + freshness, recent history, Receive/Send actions | `ddVisible` | US1, US3, FR-001/004/009/018 |
| `/digidollar/receive` | `DigiDollarReceive.razor`: fresh address + QR, list of own receive addresses with per-address balance | `ddVisible` | US1, FR-005/007 |
| `/digidollar/send` | `DigiDollarSend.razor`: recipient, USD amount, DGB fee, confirm dialog | `ddSendVisible` | US2, FR-011..017/026/027 |
| `/digidollar/mint`, `/redeem`, `/positions` | **Removed**. Routes return the standard not-found page. | never | FR-028 |

When a route's condition is false (including a flag flipped mid-session), the page redirects
to `/` and shows a one-time toast: `dd.unavailable.toast` (edge case in spec).

## Components

| Component | Where | Behaviour |
|---|---|---|
| `DigiDollarHomeCard` | `Home.razor` | Shows DD balance, the USD value of the DGB balance from the oracle (FR-021), and a tap-through to `/digidollar`. Hidden unless `ddVisible`. Never blocks rendering of DGB content (Principle V). |
| `DigiDollarSendForm` | Send | Recipient, USD amount, a **Send all** button (fills the full spendable balance, builds without change), and the computed DGB fee shown read-only. **No fee selector or custom fee input** (FR-014). |
| `DigiDollarUnavailableLine` | `Home.razor` | When `ddUnavailableLine`: a single neutral line, `dd.flagOff.line` ("DigiDollar is temporarily unavailable"). No balance, no navigation (FR-002). Reads only the plaintext `dd.used` marker. |
| `DigiDollarHealthBanner` | All DD pages | Non-blocking. Variants: `priceUnavailable` (FR-019, no price → "Price unavailable", DGB USD equivalent hidden), `unavailable` (FR-003), `oracleStale` / `err` / `volatility` (FR-019), `nodeLag` (FR-020). |
| `DigiDollarConfirmDialog` | Send | Shows the full recipient address (never truncated), USD amount, DGB fee, an own-address note, the custody line (FR-026), and a degraded-state warning line when `IsDegraded` (FR-027), which names the specific condition: stale oracle, ERR / volatility freeze, or "price unavailable" (`dd.confirm.warn.priceUnavailable`). The primary button is disabled for 1 s after opening to prevent accidental double taps. |

## UI states per page

`Loading` · `Scanning` (restore scan in progress, shows progress instead of `$0.00`, FR-007) ·
`Ready` · `Empty` (scan complete and no DD: shows a CTA to Receive) ·
`NotActive` (`deploymentActive == false`: `dd.notActive`, no receive or send actions, rechecked on
refresh, FR-003) · `UnavailableCached` (backend unreachable: cached balance/history labeled "cached, as of …", Send
disabled with `dd.send.err.offline`, FR-003) · `Unavailable` (no cache available) ·
`NotSupportedForWalletType` (non-seed wallets, research R2)

## Send validation messages (inline; localized keys)

| Condition | Key |
|---|---|
| Not a DD address / DGB address pasted | `dd.send.err.notDdAddress` |
| Wrong network | `dd.send.err.wrongNetwork` |
| < $1.00 | `dd.send.err.belowMin` |
| > $100,000.00 | `dd.send.err.aboveMax` |
| > available | `dd.send.err.insufficientDd` |
| DGB fee not covered | `dd.send.err.insufficientDgbFee` |
| Rate limited | `dd.send.err.slowDown` |
| Backend unreachable | `dd.send.err.offline` |
| Change would be < $1.00 | `dd.send.err.changeBelowMin` (with a "Send all" action) |

A send that fails after the 30-minute absence window raises the notification `dd.send.failed`
(funds released, FR-017).

## Localization

All strings live under the `dd.*` namespace in `wwwroot/locales/{en,es}.json` (the locales that exist in the
repo today). English is authoritative, and any other locale falls back to English until it is translated
(FR-025). Amounts are formatted with the active locale's `NumberFormatInfo` and a `$` symbol.

## Accessibility (FR-024)

Every interactive element has an accessible name. The confirm dialog traps focus and closes on
Esc, and banners use `role="status"` (`role="alert"` for `unavailable`). Contrast matches the
existing Tailwind tokens.
