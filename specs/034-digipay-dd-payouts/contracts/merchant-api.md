# Contract C1/C2: Merchant payout endpoints (Pay.Api)

All routes are under `/v1/pay` and use the `authenticated` rate-limit group. Store routes return 404 for stores owned by another merchant (`LoadOwnedAsync`). Store-bound POS keys get 403, as everywhere else.

Who can call what:
- **Session**: a `dps_` Dashboard token only. A `dgp_` API key gets 403 `session_required` (FR-008).
- **Read**: a `dps_` session or a `dgp_` merchant API key (FR-033).

## C1: Step-up and payout address (session-only)

### `GET /payouts/step-up/challenge` (Session)

Starts a Digi-ID re-sign-in for the purpose `payout-address`. The challenge is bound to the caller's merchant and session.

```json
200 { "nonce": "…", "uri": "digiid://…", "expiresAt": "…" }
```

The wallet signs it and posts to the existing `POST /auth/callback`. That callback:
- sees the purpose
- requires the signing address to equal `merchant.DigiIdAddress`. Otherwise it returns 403 `stepup_address_mismatch` and writes the audit entry `payout.stepup_failed`.
- creates no merchant and no session

`GET /auth/poll/{nonce}` then returns:

```json
{ "status": "signed", "stepUpGrant": "sug_…" }
```

The grant is single use, lasts 5 minutes, and is bound to the purpose, merchant and session.

### `PUT /stores/{storeId}/payout-address` (Session)

```json
{ "address": "DD1…", "stepUpGrant": "sug_…" }
```

| Result | Body |
|---|---|
| 202 Accepted | `{ change: PayoutAddressChangeDto }` |
| 400 `payout_address_invalid` | `reason`: `format` \| `checksum` \| `wrong_network` \| `dgb_address` \| `digipay_address` |
| 403 `stepup_required` | Grant missing, expired, used, or bound to another session |
| 409 `payout_change_pending` | A change is already pending; cancel it first |

**What happens on success:**
- **When it takes effect:** at once (`effectiveAt = now`) when the store has no address and holds nothing, including nothing still confirming. Otherwise 48 hours later.
- **Records and alerts:** writes the audit entry `payout_address.requested`, sends the webhook `payout_address.change_requested`, and shows the banner.

**Address checks, in order:**
1. Trim. Only letters and digits are allowed.
2. `DigiDollarAddressValidator.TryDecode(addr, null, out net, …)`. A failure is `format` or `checksum`.
3. If `AddressValidator.IsValid` (DGB) passes, it is `dgb_address`.
4. `net != store.Network` → `wrong_network`.
5. The address exists in `DdAddresses` → `digipay_address`.

### `DELETE /stores/{storeId}/payout-address/pending` (Session, no grant)

Returns 200 with `{ change }` (status `cancelled`), or 404 `no_pending_change`. Writes the audit entry `payout_address.cancelled` and sends the webhook `payout_address.change_cancelled`.

### `POST /stores/{storeId}/payout-address/acknowledge` (Session)

```json
{ "changeId": "pac_…" }
```

Returns 204. Clears the banner (FR-010).

### `GET /me/payout-alerts` (Session)

The banner feed (FR-010): every address change for this merchant's stores, or merchant-level balance, that hasn't been acknowledged. Newest first, up to 20.

```json
200 { "items": [ { "changeId": "pac_…", "storeId": "sto_…|null", "storeName": "…|null",
                   "event": "change_requested|change_cancelled|changed",
                   "oldAddress": "DD1…|null", "newAddress": "DD1…",
                   "requestedAt": "…", "effectiveAt": "…", "status": "pending|cancelled|applied" } ] }
```

A change shows its latest event only, so a requested-then-applied change appears once as `changed`. Acknowledging it with `POST …/payout-address/acknowledge` removes it from the feed.

### `POST /stores/{storeId}/payout-address/confirm` (Session)

Clears an automatic pause after failures (FR-022). Returns 200 with `{ store payout status }`.

### Merchant-level balance (FR-039)

The same routes exist under `/me/dd-balance/payout-address…` for DD held for deleted stores.

## C2: Settings, balance and payouts

### `PATCH /stores/{storeId}/payout-settings` (Session)

```json
{ "schedule": "confirmed|daily|threshold", "dailyTime": "HH:mm", "timeZone": "Europe/Berlin", "thresholdCents": 5000 }
```

Returns 200, or 400 `invalid_schedule` with the field that failed. Writes the audit entry `payout_settings.update`. No grant is needed (US5 scenario 5).

### `GET /stores/{storeId}/dd-balance` (Read)

```json
{
  "confirmedCents": 4500,
  "confirmingCents": 1000,
  "bySource": { "payment": 4000, "overpayment": 0, "late": 0, "extraRefund": 500, "donation": 0 },
  "lastCheckedAt": "…",
  "stale": false,
  "payoutAddress": "DD1…",
  "pendingChange": { "id": "pac_…", "newAddress": "DD1…", "effectiveAt": "…" },
  "schedule": { "mode": "confirmed" },
  "holdReason": "below_minimum|waiting_confirmations|schedule|threshold|no_address|change_pending|operator_paused|auto_paused|node_unavailable|fee_reserve|coverage|null",
  "nextPayoutAt": "…|null"
}
```

`stale` is true when the scanner last succeeded more than 5 minutes ago (FR-035).

### `POST /stores/{storeId}/payouts` (Session): Pay out now / Retry now

```json
{ "reason": "manual|retry" }
```

| Result | Body |
|---|---|
| 202 Accepted | `{ requestedAt }`. Picked up on the next worker cycle. |
| 409 | `below_minimum` \| `no_address` \| `change_pending` \| `payout_in_progress` \| `operator_paused` |

### `GET /stores/{storeId}/payouts?status=&cursor=` (Read)

Newest first, 50 per page. Payouts closed with `failureCode = destination_superseded` are left out, because they were re-planned to the current address and never sent.

```json
{ "items": [PayoutDto], "nextCursor": "…" }
```

### `GET /payouts/{payoutId}` (Read)

Returns a `PayoutDto` with `items[]`, each `{ sessionId?, source, amountCents, ledgerEntryId }`.

### `PayoutDto`

```json
{
  "id": "dpo_…", "storeId": "sto_…", "amountCents": 4500, "currency": "DD",
  "destination": "DD1…", "status": "waiting|sending|checking|sent|confirmed|failed",
  "waitReason": null, "failureReason": null, "failureCode": null, "willRetry": true,
  "trigger": "auto", "txid": "…", "confirmations": 6,
  "createdAt": "…", "sentAt": "…", "confirmedAt": "…", "failedAt": null
}
```

### Changes to existing endpoints

- **Session DTO** (`GET /sessions`, `/sessions/{id}`, and the `session.*` webhook body): adds `ddPayout: { payoutId, status } | null` and `ddExtraRefundCents` (FR-028, FR-029). Nothing existing is removed.
- **`PATCH /stores/{id}`**:
  - `digiDollarEnabled: true` with no payout address returns 409 `payout_address_required`. Stores grandfathered under FR-031 are exempt.
  - Changing `network` to one the current payout address doesn't belong to returns 409 `payout_address_network_mismatch`.
- **`DELETE /stores/{id}`**:
  - Returns 409 `dd_held` with `{ confirmedCents, confirmingCents }` while 100 or more cents are held or a payout is in progress.
  - `?finalPayout=true` queues a `store_delete` payout. Any remainder under 100 cents moves to the merchant-level balance.
- **`StoreDto`**: adds `ddPayoutAddress`, `ddPayoutPending`, `ddPayoutSchedule` and `ddPayoutsPaused`.

## Implementation notes (as built)

- `DELETE /stores/{id}?finalPayout=true` answers `202 { status: "payout_queued", confirmedCents }` and queues the final payout. Deleting again once it has gone out (held under 100 cents, nothing in flight) completes the deletion and moves any remainder to the merchant-level balance.
- `PATCH /payout-settings` exists for stores only; the merchant-level balance always pays out as soon as confirmed.
- The merchant-level routes are `GET /me/dd-balance`, `PUT /me/dd-balance/payout-address`, `DELETE /me/dd-balance/payout-address/pending` and `GET /me/dd-balance/payouts`.
- `GET /stores/{id}/dd-balance` also returns `autoPaused`, and `schedule` carries `dailyTime`, `timeZone` and `thresholdCents`.
- The acknowledge route ignores the `{storeId}` segment and matches the change by id and merchant, so the banner can acknowledge merchant-level changes too.
