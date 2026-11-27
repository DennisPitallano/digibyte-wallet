# Data model: DigiDollar on the merchant's own keys

This is the model on top of spec 033's schema (`AddDdEverywhere`). Spec 034's `AddDdPayouts` migration is removed and replaced by `AddDdMerchantKeys`.

## PayStore (changed)

| Field | Type | Notes |
|---|---|---|
| `DdAccountKey` | string? (≤ 200) | **New.** The canonical `xpub`/`tpub` string at `m/86'/coin'/0'`. Null when no key is set. Never a private key (R2). |
| `DdKeyId` | string? (64 hex) | **New.** SHA-256 of `network + ":" + DdAccountKey`, which is the `DdKeySequences` id. Null with no key. |
| `DdAccountKeySetAt` | DateTime? | **New.** |
| `DigiDollarEnabled` | bool | Existing. It can only be true when `DdAccountKey` is set. Removing the key sets it false. |
| `DdDonationAddress` | string? | Existing. It now comes from the key's sequence. It's cleared when the key changes and allocated again on the next request. |
| `BuyerChoiceEnabled` | bool | Existing (033). |
| `DdPayoutAddress`, `DdPayoutAddressPending*`, `DdPayout*`, `DdPayoutAddressRequiredExempt` | | **Removed** (034). |

**Validation**:

- The key's network matches `Network` (strictly).
- The key has depth 3.
- The key isn't private.
- The first address was confirmed on save (contract S2).

## DdKeySequence (new)

Each row is one distinct DD account key across DigiPay, shared by every store that has it.

| Field | Type | Notes |
|---|---|---|
| `Id` | string (64 hex), PK | `DdKeyId` (see PayStore). |
| `Network` | string | `mainnet` / `testnet`. |
| `NextIndex` | int | The next unused receive index. It only ever increases, and starts at 0. |
| `CreatedAt` | DateTime | |

The raw key isn't stored here. Stores carry it.

## DdAddress (reshaped)

DigiPay gives out one row per DD address. This is the source of truth that indexes aren't reused.

| Field | Type | Notes |
|---|---|---|
| `Address` | string, PK | The DD address (`DD…` / `TD…`). |
| `DdKeyId` | string | FK to `DdKeySequence`. |
| `Index` | int | Receive index `0/Index`. Unique together with `DdKeyId`. |
| `Kind` | string | `payment` (explicit DD session), `option` (buyer's DD option) or `donation`. |
| `StoreId` | string | The store it was given to. |
| `SessionId` | string? | For `payment` and `option`. |
| `CreatedAt` | DateTime | |
| `FirstPaidAt` | DateTime? | Set by the monitor when DD first arrives. Used for the long-unpaid-run warning (R11). |

Spec 034's `DdAddress` ledger links and backfill are removed.

## PaySession / PaySessionOption (unchanged shape)

- `Address` / `AmountCentsDd` / status fields are as in 033.
- DD addresses now come from `DdAddressAllocator` rather than the node.
- `PaidTxid` and confirmations come from explorer history (R5).

## Removed entities (034)

`DdLedgerEntry`, `DdPayout`, `DdPayoutItem`, `PayoutAddressChange`, `StepUpGrant`, `MerchantDdSettings`, `OperatorSetting`, and `WebhookDelivery.PayoutId`.

## State and lifecycle

```text
Store DD key:  none ──set (preview + confirm)──▶ set ──replace──▶ set (new DdKeyId; donation address cleared)
                 ▲                                │
                 └──────────── remove ────────────┘   (DigiDollarEnabled := false)

DigiDollarEnabled: false ──turn on (needs key)──▶ true ──turn off / key removed──▶ false

DD index for a key: NextIndex n ──allocate──▶ address(n) recorded, NextIndex n+1   (never decreases)
```

Open payments keep their `Address` whatever happens to the store's key. The monitor watches addresses, not keys.

## Migration `AddDdMerchantKeys`

- Add the `PayStore` columns `DdAccountKey`, `DdKeyId`, `DdAccountKeySetAt`.
- Create `DdKeySequences`.
- Reshape `DdAddresses`, or create it if `AddDdPayouts` never ran.
- Set `DigiDollarEnabled = false` on stores with no key, and clear `DdDonationAddress` on them. There are none in production. This affects only dev and testnet stores (spec edge case).
- Add the matching SQLite shim in `Program.cs` for tests.
