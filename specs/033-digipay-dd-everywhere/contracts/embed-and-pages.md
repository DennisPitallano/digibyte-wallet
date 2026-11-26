# Contract C4: Hosted pages and the embed script

## /pay/button (tip page, `Donate.razor`)

| Query | Status | Meaning |
|---|---|---|
| `to` | existing | DGB address. Still validated with `AddressValidator`. |
| `amount` | existing | Suggested **DGB** amount. Never shown as DD. |
| `name`, `label` | existing | |
| `store` | **new** | Store id. When it's present, the page loads `donation-options` (C2) and can offer DD. `to` becomes optional and the options response wins. |
| `currency` | **new** | `DGB` (default) or `DD`. The currency the page opens on. It's ignored when DD isn't offered. |
| `ddAmount` | **new** | Suggested DD amount in dollars (2 decimals, 1.00 to 100,000.00). Otherwise no amount is shown for DD. |

Behaviour:
- With no `store`, the page behaves exactly as today.
- With `store`, it shows a DGB/DD switch only when `ddAddress` is present. If `ddAvailable` is false, the DD tab is disabled with "DigiDollar is temporarily unavailable".
- Every DD address is checked with `DigiDollarAddressValidator.IsValid(address, network)` before it's rendered. An address that fails isn't shown, and the page falls back to DGB.
- The DD QR uses `digibyte:{ddAddress}?amount={usd}&currency=USD`, the same form as DD sessions, with `amount` only when `ddAmount` is set.

## digipay.js `DigiPay.button(opts)`

New options, mirrored by `data-` attributes on auto-mount:

| Option | Attribute | Maps to |
|---|---|---|
| `store` | `data-store` | `store` |
| `currency` | `data-currency` | `currency` |
| `ddAmount` | `data-dd-amount` | `ddAmount` |

`address` is no longer required when `store` is given. The default button text stays as it is. The Dashboard snippet generator sets `label` to "Tip in DGB or DD" when it includes `store` for a store with DD on. The script itself can't know DD's state.

## Hosted checkout (`/pay/{id}`, `Checkout.razor`)

These rules are the UI side of C1.
- The switch shows when `buyerChoice && !lockedAt`, and DD is shown only if a DD option exists or `ddAvailable`.
- Switching to DD calls `POST /sessions/{id}/options/dd`. On a `409`/`503` the switch shows the message and stays on DGB.
- A converted DD amount shows a rate line: "₱1,250.00 at ₱58.10 per US dollar".
- Once `lockedAt` is set, the switch is replaced by the locked currency.
