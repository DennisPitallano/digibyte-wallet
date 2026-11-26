# Data Model: DigiDollar on every DigiPay payment surface

**Feature**: [spec.md](spec.md) | **Research**: [research.md](research.md)

All entities live in `src/DigiByte.Pay.Api/Data/`. Production uses an EF Core migration (`yyyyMMddHHmmss_AddDdEverywhere`). Dev and tests (SQLite, `EnsureCreated`) need the matching hand-written column shims in `Program.cs`, as earlier DD columns did. `DdAddress` comes from spec 034 and must already exist (research R14).

## PaySession (changed)

| Field | Change | Rules |
|---|---|---|
| `Currency` | `init` → `set` | The currency **in effect**. It changes once, when a buyer-choice session locks (R1). Still `"DGB"` or `"DD"`. |
| `Address` | (already settable) | The address in effect. It switches to the locked option's address at lock. |
| `AmountSatoshis` | `init` → `set` | Set to 0 when the session locks to DD. |
| `AmountCentsDd` | (already `long?`) | Set from the DD option when it locks to DD. |
| `AddressIndex` | unchanged | Keeps the DGB option's xpub index. Address allocation stays monotonic. |
| `BuyerChoice` | **new** bool, default false | True only when every condition in R5 held at creation. Never changes afterwards. |
| `LockedAt` | **new** `DateTime?` | Set when funds are first seen on any option of a buyer-choice session. Null for sessions without buyer choice. |
| `ExtraCurrency` | **new** `string?` | `"DGB"` or `"DD"`: the currency of funds received on the option the session didn't lock to. |
| `ExtraAmount` | **new** `long?` | Satoshis for DGB, cents for DD. Cumulative across ticks. |
| `ExtraTxid` | **new** `string?` | First txid seen. |
| `ExtraSeenAt` | **new** `DateTime?` | |

**State transitions** (buyer choice only; the existing status flow is unchanged):

```text
Pending (unlocked, options watched) ──funds seen on option X──▶ Seen (Currency=X, LockedAt=now)
Pending (unlocked) ──expiry, no funds──▶ Expired (as today)
Seen/Paid/Confirmed/Expired-in-late-window ──funds on non-X option──▶ same status, Extra* set, webhook session.extra_payment
```

**Invariants**
- `BuyerChoice = false` ⇒ no `PaySessionOption` rows, and `LockedAt` and `Extra*` stay null.
- `LockedAt != null` ⇒ `Currency` equals the currency of the option whose address equals `Address`.
- At most one option per currency per session (unique index `SessionId, Currency`).
- `ExpiresAt` is never extended by switching currency (spec edge case).

## PaySessionOption (new)

One row per currency a buyer-choice session can be paid in.

| Field | Type | Rules |
|---|---|---|
| `SessionId` | string (FK → PaySession, cascade) | Part of the unique key with `Currency`. |
| `Currency` | string | `"DGB"` or `"DD"`. |
| `Address` | string | DGB: the address allocated at creation. DD: from `GenerateAddressAsync`, also written to `DdAddress` (`Kind = payment`, `SessionId`). |
| `AmountSatoshis` | long | DGB amount, worked out as today (from `FiatAmount` and `DgbPriceAtCreation`). 0 for DD. |
| `AmountCentsDd` | long? | DD only. 100 to 10,000,000. `ceil(FiatAmount × 100 / UsdRate)`. |
| `UsdRate` | decimal? | DD only. Units of `FiatCurrency` per 1 USD at quote time; 1 for USD. |
| `RateAt` | DateTime? | DD only. When the rate was taken. |
| `CreatedAt` | DateTime | |

Created as follows:
- **DGB row**: when the session is created.
- **DD row**: on the buyer's first switch (`POST /sessions/{id}/options/dd`), or at creation when the store default is DD (R5).

Rows are never updated, so the quoted amount stays fixed until expiry (clarification Q1).

## PayStore (changed)

| Field | Change | Rules |
|---|---|---|
| `DefaultCurrency` | allowed values widened | Now also accepts `"DD"`, but only when `DigiDollarEnabled` and the gate are on. Readers treat a stored `"DD"` as `"USD"` while DD is off (R7). |
| `BuyerChoiceEnabled` | **new** bool, default true | FR-029. Only has an effect while `DigiDollarEnabled` is on. |
| `DdDonationAddress` | **new** `string?` | Created once, on demand (R9). Also written to `DdAddress` (`Kind = donation`). Never rotated, never cleared when DD is turned off, so donations sent later are still credited. |

## PayMerchant (changed)

| Field | Change | Rules |
|---|---|---|
| `DdChoiceNoticePending` | **new** bool, default false | The migration sets it to true where the merchant has any store with `DigiDollarEnabled`. Cleared for good by the dismiss endpoint (R12). |

## DdAddress (from spec 034, used here)

| Created by 033 | `Kind` | `SessionId` | `StoreId` |
|---|---|---|---|
| DD option on a buyer-choice session | `payment` | session id | session's store |
| DD session started on DD because the store default is DD | `payment` | session id | session's store |
| Store DD donation address | `donation` | null | store |

034's ledger scanner classifies DD that arrives on a `payment` address whose session locked to DGB as `extra_refund`.

## Read models (not stored)

- **Shift totals** (`PosEndpoints`): add `collectedDdCents`. Paid and Confirmed sessions where `Currency == "DD"` are summed from `ReceivedSatoshis`, which holds cents for DD. Expired and Underpaid totals get the same split.
- **Daily stats** (`DailyStats`): add `revenueDdCents` per day. DGB revenue stays DGB-only.
- **CSV export**: an `amount` column in the session's own unit, plus a `currency` column. DD amounts come from cents.
