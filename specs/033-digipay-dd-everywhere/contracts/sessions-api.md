# Contract C1: Sessions API changes

Base: `/v1/pay`. Everything not listed here is unchanged. Error bodies keep the existing `{ "error": "<code>", ... }` shape.

## POST /v1/pay/sessions (changed)

Auth unchanged: `dgp_` API key, `dps_` Dashboard session, or POS key.

**New request field**

| Field | Type | Default | Meaning |
|---|---|---|---|
| `buyerChoice` | bool? | false | Ask for the buyer's DGB/DD choice at checkout. It's honoured only when every condition in research R5 holds; otherwise it's ignored without an error, so the same client code works for every store. |

**Rules**
- `currency:"DD"` is unchanged: it needs `amountCentsDd` between 100 and 10,000,000 and `DigiDollarEnabled`, and it isn't affected by the gate.
- When `buyerChoice` is honoured and the store's default currency is `DD`, the session starts on DD. If the node is unavailable, it falls back to starting on DGB (no 503).
- The POS sends an `Idempotency-Key` on every charge from now on. Behaviour is unchanged for clients that don't.

**Response** (`SessionDto`, also returned by GET): new fields

| Field | Type | Notes |
|---|---|---|
| `buyerChoice` | bool | |
| `lockedAt` | string? (ISO-8601) | |
| `options` | `OptionDto[]` | Empty when `buyerChoice` is false. |
| `extra` | `{ currency, amount, txid, seenAt }`? | `amount` in satoshis (DGB) or cents (DD). |

`OptionDto`: `{ currency, address, amountSatoshis, amountCentsDd?, usdRate?, rateAt?, uri }`. `uri` uses the existing BIP21 builder for the option's currency.

`currency`, `address`, `amountSatoshis`, `amountCentsDd` and `uri` on the session itself still describe the currency in effect (research R1).

## GET /v1/pay/sessions/{id} (public, changed)

Response `{ session, merchantName }` gains:

| Field | Type | Notes |
|---|---|---|
| `ddAvailable` | bool | Only present when `session.buyerChoice`. False when the store has DigiDollar off, the gate is off, or `DdAvailability` (30 s cache) reports the node down. |
| `ddReason` | string? | `dd_disabled` or `unavailable` when `ddAvailable` is false. |

## POST /v1/pay/sessions/{id}/options/dd (new, anonymous)

Creates or returns the session's DD option. Rate limit: `checkout-option`, 30 a minute per client address (`TrustedClient.AddressOf`, so Pay.Web must forward the buyer's address with the trusted-client headers).

| Result | Status | Body |
|---|---|---|
| Option exists or was created | 200 | `OptionDto` |
| Unknown session | 404 | `{ error:"not_found" }` |
| Session has no buyer choice, is locked, isn't Pending, or has expired | 409 | `{ error:"dd_option_not_allowed", reason:"no_choice" \| "locked" \| "not_pending" \| "expired" }` |
| DD now off for the store, or gate off | 409 | `{ error:"dd_option_unavailable", reason:"dd_disabled" }` |
| No USD rate for `fiatCurrency` | 409 | `{ error:"dd_option_unavailable", reason:"no_rate" }` |
| Converted amount outside 100 to 10,000,000 cents | 409 | `{ error:"dd_option_unavailable", reason:"out_of_range", minCents, maxCents }` |
| Node unavailable | 503 | `{ error:"digidollar_node_unavailable" }` |

The endpoint is idempotent: concurrent calls produce one option. That's enforced by the unique `(SessionId, Currency)` index, and the loser re-reads the existing row.

## Checkout SignalR (`/hubs/checkout`, changed)

`StatusChanged` payload `CheckoutStatusUpdate` gains `Currency` (string), `LockedAt` (string?) and `AmountCentsDd` (long?). Older clients ignore them.

## Errors surfaced in Pay.Web (`ApiError`)

| Code | Message shown |
|---|---|
| `digidollar_node_unavailable` | "DigiDollar is temporarily unavailable. Try again, or take this payment in DGB." |
| `dd_option_unavailable` / `no_rate` | "DigiDollar isn't available for this payment right now." |
| `dd_option_unavailable` / `out_of_range` | "This amount is outside DigiDollar's limits ($1.00 to $100,000.00)." |
