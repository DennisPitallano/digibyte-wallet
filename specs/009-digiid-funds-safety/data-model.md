# Data Model: Digi-ID Address Funds Safety

None of these types holds a private key, seed or PIN (FR-015). Amounts are in satoshis.

## DigiIdSite (input to a check)

Where an address to check came from. Not persisted by this feature.

| Field | Meaning | Rule |
|---|---|---|
| Domain | Normalised host (lowercase, no scheme, port or path) | Produced by `DigiIdDomain.Normalize`. Same string a login would sign with. |
| Source | `History` or `Manual` | The same domain from both sources is checked once (spec edge case). |

The address is **derived** from the domain with the active wallet. A stored `DigiIdLoginRecord.Address` is never trusted (research R-3).

## DigiIdFundFinding (result of checking one address)

| Field | Meaning | Rule |
|---|---|---|
| Domain | Site the address belongs to | |
| Address | Legacy P2PKH Digi-ID address | Derived, not stored in history. |
| ConfirmedSatoshis | Sum of UTXOs with at least 1 confirmation | Only this amount is sweepable. |
| PendingSatoshis | Sum of UTXOs with 0 confirmations | Shown, never swept. |
| Outcome | `Funds`, `Empty`, `CouldNotCheck` | `CouldNotCheck` only when the lookup threw. Never shown as `Empty`. |
| Error | Short user-facing reason | Set only for `CouldNotCheck`. |
| InFlight | A sweep record for this address is `Sending` or `Pending` | The address is not offered for sweeping. |
| OtherOutputsCount | Outputs whose script is present and differs from the address's own standard script | Left untouched and excluded from every amount (FR-016). Shown as "other outputs left untouched". Zero when the lookup returns no script. |

A check result is a list of findings plus a total of `ConfirmedSatoshis` and `PendingSatoshis` over `Funds` findings.

## DigiIdSweepPlan (what the user reviews)

| Field | Meaning | Rule |
|---|---|---|
| Items | One per swept address | At most 19 (`ReceiveGapLimit` minus 1), and fewer if unused addresses already handed out leave less room. |
| Remaining | Count of eligible addresses left for a later round | Shown to the user. |
| TotalAmount / TotalFee / TotalNet | Sums over Items | |
| FeeRateSatPerVByte | Rate used | Not below the wallet floor (100). |

### DigiIdSweepPlanItem

| Field | Meaning | Rule |
|---|---|---|
| Domain, SourceAddress | Where the funds are | |
| AmountSatoshis | Confirmed funds on the address | |
| EstimatedFeeSatoshis | `EstimateFee(inputs, 1, rate, Legacy)` | Estimate only. |
| NetSatoshis | Amount minus fee | Must be at least 10,000 sat (0.0001 DGB), else the item is excluded as "too small". |
| DestinationAddress | Fresh BIP84 receive address | Unique within the plan and unused. Never reused across items (FR-008a). |
| DestinationIndex | Receive index of that address | The highest index plus one is saved as `NextReceivingIndex` before the first broadcast, so the Receive page shows a fresh unused address and never a destination. |

A plan holds no keys. `ExecuteDigiIdSweepAsync` re-derives the key for each item at execution time.

## DigiIdSweepRecord (persisted, one per swept address)

Stored per wallet under `digiid_sweeps_{walletId}` in encrypted storage.

| Field | Meaning |
|---|---|
| Id | Unique id for the record |
| Domain, SourceAddress, DestinationAddress | As in the plan item |
| AmountSatoshis, FeeSatoshis | Final amounts from the signed transaction (fee is the real one) |
| TxId | Null until broadcast succeeds |
| Status | `Sending`, `Pending`, `Confirmed`, `Failed` |
| FailureReason | Short text, set when `Failed` |
| CreatedUtc, UpdatedUtc | Timestamps |

### State transitions

```text
(plan confirmed) → Sending ──broadcast ok──→ Pending ──≥1 confirmation──→ Confirmed
                       │                         │
                       └─broadcast rejected──→ Failed   ←──tx dropped / replaced──┘
Sending (no TxId after reload) ──re-verified UTXOs still present──→ Failed (address offered again)
```

- `Confirmed` and `Failed` records stop being refreshed. `Confirmed` records stay visible in the section for the current session and then drop out of the list. Their transactions remain in normal history (FR-013).
- Rule for `InFlight`: a source address is not sweepable while it has a `Sending` or `Pending` record.
- The record contains no key, raw transaction or signature.

## Existing entities touched (not changed)

- `DigiIdLoginRecord` (Domain, Address, Timestamp, and more): read-only source of domains.
- `TransactionRecord`: receives a normal "sent" entry for each broadcast sweep through `TransactionTracker.RecordSendAsync`.
- `WalletInfo`: `NextReceivingIndex` is advanced through `PersistAddressIndexesAsync` before broadcast.
