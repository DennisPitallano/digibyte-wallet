# Contract: stores API (DigiDollar key)

These routes are owner-only, with a `dgp_*` merchant key or a Dashboard session, as for other store settings. Store-bound POS keys are refused.

## S1. Preview a key

`POST /v1/pay/stores/{storeId}/dd-key/preview`

```json
{ "key": "tpubDC…" }
```

**200**:

```json
{ "network": "testnet", "firstAddress": "TD1p…", "keyFingerprint": "a1b2c3d4" }
```

`keyFingerprint` is the first 8 hex characters of `DdKeyId`, used only for display.

**400** `dd_key_invalid`, with `reason`:

| reason | When |
|---|---|
| `malformed` | Not base58check, or the wrong length |
| `private_key` | `xprv`/`tprv` or any private extended key. The response never contains the input. |
| `wrong_network` | The key's network isn't the store's network |
| `not_account_key` | Depth ≠ 3 |

Nothing is stored, and the input is never logged.

## S2. Set or replace the key

`PUT /v1/pay/stores/{storeId}/dd-key`

```json
{ "key": "tpubDC…", "confirmFirstAddress": "TD1p…" }
```

**200**: the store DTO (S4).

**Errors**:

- **400** `dd_key_invalid` (as in S1).
- **409** `first_address_mismatch` when `confirmFirstAddress` isn't the derived index-0 address.

**Effects**:

- Sets `DdAccountKey`, `DdKeyId` and `DdAccountKeySetAt`.
- Creates the `DdKeySequence` row if it's new. An existing row is reused, so its sequence continues.
- Clears `DdDonationAddress` if the key changed.
- Writes the audit event `store.dd_key_set` (or `store.dd_key_replaced`) with the fingerprint only.
- Open payments are untouched.

## S3. Remove the key

`DELETE /v1/pay/stores/{storeId}/dd-key`

**204**.

**Effects**:

- Clears the key fields and sets `DigiDollarEnabled = false`.
- Clears `DdDonationAddress`.
- Writes the audit event `store.dd_key_removed`.
- Open DD payments keep being watched until they finish.

## S4. Store DTO (GET/PATCH `/v1/pay/stores/{id}`, `/v1/pay/me`)

Added:

```json
{
  "ddKey": { "set": true, "network": "testnet", "fingerprint": "a1b2c3d4", "firstAddress": "TD1p…", "setAt": "2026-11-27T10:00:00Z",
             "unpaidRun": 12, "unpaidRunWarning": false }
}
```

- `ddKey` is null when there's no key.
- The raw key is **not** returned in the DTO. The merchant already has it in their wallet, and returning it widens exposure.
- `unpaidRunWarning` is true when `unpaidRun` > 500 (R11).

Removed: `ddPayoutAddress`, `ddPayoutAddressPending`, `ddPayoutSchedule`, `ddHeldCents`, and the other spec 034 payout fields.

## S5. Turning DigiDollar on

`PATCH /v1/pay/stores/{id}` with `{ "digiDollarEnabled": true }` on a store with no key gives **409** `dd_key_required`. This replaces `payout_address_required`.

## S6. Donation address

`GET /v1/pay/stores/{id}/donation-address?currency=DD` returns the stored `DdDonationAddress`. If none is stored yet, it allocates the next index from the key's sequence (`DdAddress.Kind = donation`) and stores the result.

**409** `digidollar_not_enabled_for_store` when DD is off or there's no key.

## Removed routes (spec 034)

- `/v1/pay/payouts/*`
- `/v1/pay/stores/{id}/payout-address*`
- the step-up routes
- `/v1/pay/operator/payouts/*` (pause, resume, health)
- `DELETE /v1/pay/stores/{id}` no longer returns `dd_held` or makes a final payout
