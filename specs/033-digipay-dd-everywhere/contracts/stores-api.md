# Contract C2: Stores, merchant and donation APIs

## PATCH /v1/pay/stores/{id} (changed)

| Field | Change |
|---|---|
| `defaultCurrency` | Now accepts `"DD"`. It's rejected with `400 { error:"digidollar_not_enabled_for_store" }` unless the store has `digiDollarEnabled` and `DigiPay:DdSurfacesEnabled` is on. |
| `buyerChoiceEnabled` | **New** bool?. Absent means keep. Allowed whatever DD's state; it only has an effect while DD is on. Audit change key `buyer_choice`. |
| `digiDollarEnabled` | Unchanged here. Spec 034 adds the payout-address requirement. |

Store DTOs (`GET /v1/pay/stores`, `/v1/pay/me`) gain `buyerChoiceEnabled`, `ddDonationAddress` and `defaultCurrencyEffective`. The last one is `"USD"` when `defaultCurrency` is `"DD"` but DD is off.

## GET /v1/pay/me (changed)

Gains `ddSurfacesEnabled` (bool, from the gate) and `ddChoiceNoticePending` (bool).

## POST /v1/pay/me/notices/dd-choice/dismiss (new)

`dps_` Dashboard session only. Clears `DdChoiceNoticePending`. Returns 204. It's idempotent.

## GET /v1/pay/stores/{id}/donation-address (changed)

| Query | Behaviour |
|---|---|
| none or `currency=DGB` | Unchanged. |
| `currency=DD` | Needs DD on and the gate on, else `400 digidollar_not_enabled_for_store`. Returns `{ address, mode:"dd" }`, creating `DdDonationAddress` on first call (`503 digidollar_node_unavailable` if that fails). |

## GET /v1/pay/public/stores/{id}/donation-options (new, anonymous)

Rate limit: `checkout-option`. Response 200:

```json
{
  "storeName": "Shop",
  "network": "mainnet",
  "dgbAddress": "dgb1q…",
  "ddAddress": "DD…",
  "ddAvailable": true,
  "ddReason": null
}
```

- `dgbAddress` is the existing donation address. It's null if the store has none yet; this endpoint never derives one, which keeps it read-only.
- `ddAddress` is present only when DD is on, the gate is on, and `DdDonationAddress` exists.
- `ddAvailable` is false with `ddReason:"unavailable"` when `DdAvailability` reports the node down.
- An unknown store returns 404.
- The response never reveals any other store data.
