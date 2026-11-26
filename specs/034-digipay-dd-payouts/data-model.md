# Data Model: DigiDollar payouts to merchants

Pay.Api data model for [plan.md](plan.md).
- **Amounts:** DD in integer cents (`long`).
- **Times:** UTC.
- **Ids:** the existing prefix-plus-random-string scheme.
- **Migrations:** one Postgres migration, `AddDdPayouts`, plus matching SQLite shims in `Program.cs`.

## PayStore (changed)

| Field | Type | Rules |
|---|---|---|
| `DdPayoutAddress` | string? (≤ 128) | Address in effect. Valid DD for `Network` (`DigiDollarAddressValidator.IsValid(addr, Network)`). Not any `DdAddress.Address`. |
| `DdPayoutAddressSetAt` | DateTime? | When the address took effect. |
| `DdPayoutSchedule` | string (≤ 16), default `"confirmed"` | `confirmed` \| `daily` \| `threshold`. |
| `DdPayoutDailyTime` | TimeOnly? | Required when the schedule is `daily`. Default 00:00. |
| `DdPayoutTimeZone` | string? (IANA) | Set from the merchant's browser time zone when they choose `daily`. `UTC` when not set. |
| `DdPayoutThresholdCents` | long? | Required when the schedule is `threshold`. At least 100 and at most 10,000,000. |
| `DdPayoutsAutoPausedAt` | DateTime? | Set after 3 failures in a row (FR-022). Cleared when the merchant confirms the address, changes it, or a retry succeeds. |
| `DdPayoutConsecutiveFailures` | int | Reset to 0 when a payout is `sent`. |
| `DdPayoutsPausedByOperatorAt` | DateTime? | Pause for this store only (FR-021). |
| `DdPayoutRequestedAt` | DateTime? | Set by "Pay out now" or "Retry now". Cleared when the planner picks it up. |

**Other rules:**
- **`Network` changes:** if `DdPayoutAddress` isn't valid for the new network, the change is refused (409 `payout_address_network_mismatch`).
- **Turning DD on** (`DigiDollarEnabled` false → true) needs a `DdPayoutAddress` or a pending change (FR-031). Stores that already have DD on are grandfathered.
- **Deleting a store** needs held DD under 100 cents, or a final payout (FR-015). Any remainder is moved by a pair of `transfer` ledger entries to the merchant-level owner.

## DdAddress (new)

Every DD address DigiPay has created, with its owner.

| Field | Type | Rules |
|---|---|---|
| `Address` | string (PK) | |
| `MerchantId` | string | Required. |
| `StoreId` | string? | Null once the store is deleted, or for an orphan found during backfill. |
| `Kind` | string | `payment` \| `donation`. |
| `SessionId` | string? | Set for `payment`. |
| `Network` | string | |
| `CreatedAt` | DateTime | |

Created when a DD session is created (`PaymentsEndpoints`), when a donation address is created (033), and by the backfill (research R5).

## DdLedgerEntry (new)

Append-only. The balance for an owner is the sum of entries over (`MerchantId`, `StoreId`).

| Field | Type | Rules |
|---|---|---|
| `Id` | `dle_…` | |
| `MerchantId`, `StoreId?` | | Owner. |
| `Type` | string | `credit` \| `debit` \| `debit_reversal` \| `transfer_out` \| `transfer_in` \| `reversal_absorbed`. |
| `Source` | string? | For credits: `payment` \| `overpayment` \| `late` \| `extra_refund` \| `donation`. |
| `AmountCents` | long | Positive. The sign comes from `Type`. |
| `Txid`, `Address` | string? | For credits, unique together (`UX_DdLedger_Txid_Address`) so a receive is never credited twice. |
| `SessionId` | string? | |
| `PayoutId` | string? | For `debit` and `debit_reversal`. |
| `Confirmations` | int | Credits only. Updated by the scanner until at least 6. |
| `ConfirmedAt` | DateTime? | Set when confirmations first reach 6. From then the credit can be paid out. |
| `CreatedAt` | DateTime | |

**Classifying a credit against its session** (in order, up to the amount still due):
- received before `ExpiresAt` → `payment`
- above the amount due → `overpayment`
- after `ExpiresAt` → `late`
- in the currency the buyer didn't lock to, on a buyer-choice payment (033) → `extra_refund`
- `Kind = donation` → `donation`

**Reversal**: a credit whose transaction later has 0 confirmations or can't be found:
- **Not yet paid out:** the credit is removed from the balance.
- **Already covered by a payout:** a `reversal_absorbed` entry is written, which doesn't change the store balance, and the operator is alerted (FR-023).

**Balance views**:
- **Confirmed held** = Σ confirmed credits + debit_reversal + transfer_in − debit − transfer_out.
- **Still confirming** = Σ credits not yet confirmed.
- **By source** = the confirmed credits not yet covered by a payout, grouped by `Source`, tracked through `DdPayoutItem`.

## DdPayout (new)

| Field | Type | Rules |
|---|---|---|
| `Id` | `dpo_…` | Also the `senddigidollar` comment. |
| `MerchantId`, `StoreId?` | | |
| `Destination` | string | Copied from `DdPayoutAddress` when created (FR-020). |
| `AmountCents` | long | At least 100. |
| `Status` | string | See the transitions below. |
| `WaitReason` | string? | `node_unavailable` \| `fee_reserve` \| `coverage` \| `operator_paused`. |
| `FailureReason` | string? | Plain-language text for the merchant, plus `FailureCode`. |
| `Trigger` | string | `auto` \| `daily` \| `threshold` \| `threshold_age` \| `manual` \| `retry` \| `store_delete`. |
| `Txid` | string? | |
| `Confirmations` | int | |
| `AttemptCount`, `LastAttemptAt` | | |
| `CreatedAt`, `SentAt?`, `ConfirmedAt?`, `FailedAt?` | | |

**Status transitions:**

```text
waiting ──(conditions clear)──▶ sending
sending ──(txid)──▶ sent ──(≥6 conf)──▶ confirmed
sending ──(clear reject)──▶ failed            [debit_reversal]
sending ──(insufficient funds)──▶ waiting     [debit_reversal]
waiting ──(destination no longer current)──▶ failed (destination_superseded)
sending ──(ambiguous)──▶ checking
checking ──(found in /sends)──▶ sent
checking ──(not found ≥10 min, node healthy)──▶ failed   [debit_reversal]
```

**Status rules:**
- `waiting` holds no debit. The planner creates the debit when it moves the payout to `sending`.
- `failed` is final for that payout. A retry creates a new payout.
- A `waiting` payout is re-checked against the current address before it moves to `sending`. A payout whose destination is no longer current is closed as `failed`/`destination_superseded` and doesn't count toward the automatic pause.
- An insufficient-funds rejection returns the payout to `waiting` (`coverage`) instead of `failed`.
- At most one payout per store can be `sending` or `checking`.

## DdPayoutItem (new)

Links a payout to the confirmed credits it covers, for FR-026, FR-028 and FR-029.

| Field | Type |
|---|---|
| `PayoutId` | string |
| `LedgerEntryId` | string |
| `AmountCents` | long (can be part of a credit when a payout is capped) |

## PayoutAddressChange (new)

| Field | Type | Rules |
|---|---|---|
| `Id` | `pac_…` | |
| `StoreId`, `MerchantId` | | Owner. Can be store-less for the merchant-level balance (FR-039). |
| `OldAddress?`, `NewAddress` | string | `NewAddress` is validated as for `PayStore.DdPayoutAddress`. |
| `Status` | string | `pending` → `applied` \| `cancelled`. |
| `RequestedAt`, `EffectiveAt` | DateTime | `EffectiveAt` = `RequestedAt` + 48h, or `RequestedAt` for a first address on a store holding nothing. |
| `RequestedBySessionId` | string | |
| `CancelledAt?`, `CancelledBySessionId?`, `AppliedAt?` | | |
| `AcknowledgedAt?` | DateTime | When the merchant dismissed the banner (FR-010). |

At most one `pending` change per owner.

## StepUpGrant (new)

| Field | Type | Rules |
|---|---|---|
| `Id` | `sug_…` | |
| `MerchantId`, `SessionId` | | Bound to both. |
| `Purpose` | string | `payout-address`. |
| `DigiIdAddress` | string | Must equal `PayMerchant.DigiIdAddress`. |
| `CreatedAt`, `ExpiresAt` | | 5-minute lifetime. |
| `ConsumedAt?` | DateTime | Single use. |

## MerchantDdSettings (new)

Payout settings for a merchant's DD held outside any store: DD from deleted stores, and remainders under 1.00 DD moved there on store deletion (FR-015, FR-039). One row per merchant, created the first time a store holding DD is deleted.

| Field | Type | Rules |
|---|---|---|
| `MerchantId` | string (PK) | |
| `Network` | string | Taken from the deleted stores' DD addresses. The payout address must be valid for it. |
| `DdPayoutAddress`, `DdPayoutAddressSetAt`, `DdPayoutSchedule`, `DdPayoutDailyTime`, `DdPayoutTimeZone`, `DdPayoutThresholdCents`, `DdPayoutsAutoPausedAt`, `DdPayoutConsecutiveFailures`, `DdPayoutsPausedByOperatorAt`, `DdPayoutRequestedAt` | | Same types and rules as on `PayStore`. |

The planner, sender and address service treat a `PayStore` and a `MerchantDdSettings` row the same way, through one owner-settings view. `PayoutAddressChange` and `DdPayout` rows with `StoreId = null` belong to this owner.

## OperatorSetting (new, key/value)

`payouts.paused_at` (DateTime?) and `payouts.paused_reason` (string?).

## WebhookDelivery (changed)

| Field | Type | Rules |
|---|---|---|
| `PayloadJson` | string? | The stored body for non-session events. `WebhookRetrier` replays it exactly. |
| `PayoutId` | string? (≤ 32) | |

`EventName` allows the new names (≤ 60 characters already).

## AuditEvent (unchanged schema; new actions)

| Action | TargetType | Notes |
|---|---|---|
| `payout_address.requested`, `.cancelled`, `.applied` | `Store` | `metadata`: `{old, new, effectiveAt, changeId}` |
| `payout.stepup_failed` | `Merchant` | `metadata`: `{reason}` (address mismatch, expired, abandoned) |
| `payout_settings.update` | `Store` | `metadata`: `{schedule, dailyTime, timeZone, thresholdCents}` |
| `payout.sent`, `.confirmed`, `.failed`, `.checking` | `Payout` | Actor `System`, except when started by a manual trigger. |
| `payout.manual`, `payout.retry` | `Payout` | |
| `payouts.paused`, `payouts.resumed` | `Store` / `System` | Store auto-pause, or operator pause. |
