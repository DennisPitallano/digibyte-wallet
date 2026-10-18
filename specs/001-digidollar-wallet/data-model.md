# Data Model: DigiDollar in the DGB Wallet

**Feature**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Research**: [research.md](research.md)

All amounts are integers. DD is in **USD cents** (`long`) and DGB is in **satoshis** (`long`).
Display conversion happens only in the UI layer.

## Client-side (browser, `DigiByte.Wallet`)

### DigiDollarAccount (persisted, IndexedDB, per wallet)

| Field | Type | Rules |
|---|---|---|
| WalletId | string | FK to the existing wallet record; one DD account per seed-based wallet |
| Network | enum {Mainnet, Testnet} | Regtest is not allowed (FR-029) |
| NextReceiveIndex | int | ≥ 0; incremented when the user asks for a new address (FR-005) |
| NextChangeIndex | int | ≥ 0; incremented when a send creates DD change |
| ScanComplete | bool | false until the first restore scan finishes (FR-007, research R12); a retry runs on next open |
| LastScanAt | DateTimeOffset? | |
| CreatedAt | DateTimeOffset | |

No keys are stored. Keys are re-derived from the decrypted seed in memory at unlock
(Constitution I/II).

**Storage protection (FR-008, research R11)**: `DigiDollarAccount`, `DigiDollarAddress`,
`DigiDollarCache`, and `PendingDdSend` are persisted together as one encrypted blob (key derived from the unlocked seed; the wallet does not retain the PIN after unlock).
The only plaintext item is the boolean `dd.used` (per wallet, no addresses or amounts).

### DigiDollarCache (persisted, encrypted)

| Field | Type | Rules |
|---|---|---|
| Balance | DigiDollarBalance snapshot | Shown only in `UnavailableCached`, labeled as cached (FR-003) |
| RecentHistory | DigiDollarTransaction[] | Last 50 |
| AsOf | DateTimeOffset | Displayed as "cached, as of …" |

### DigiDollarAddress (persisted, IndexedDB)

| Field | Type | Rules |
|---|---|---|
| Address | string | Valid per `DigiDollarAddressValidator` for `Network` (FR-006) |
| Chain | enum {Receive, Change} | Change addresses are hidden from the Receive list |
| Index | int | Derivation index on `m/86'/20'/0'/{chain}/{index}` |
| Label | string? | Optional, ≤ 64 chars |
| CreatedAt | DateTimeOffset | |

Uniqueness is on (WalletId, Chain, Index), and Address is unique per wallet.

### DigiDollarBalance (derived, in memory)

| Field | Type | Rules |
|---|---|---|
| ConfirmedCents | long | Sum of DD UTXOs with ≥ 1 confirmation across all the wallet's addresses |
| PendingCents | long | Sum of unconfirmed incoming DD − unconfirmed outgoing DD (may be negative during a send) |
| PerAddress | map<Address, long> | Used on the Receive page (FR-007) |
| AsOfHeight | int | Node tip height at fetch time |

### DigiDollarUtxo (derived, in memory, from NodeApi)

| Field | Type | Rules |
|---|---|---|
| Txid, Vout | string, int | Outpoint |
| Address | string | Owner DD address |
| AmountCents | long | > 0 |
| ScriptPubKey | hex | `5120` ‖ output key; required for the Taproot sighash (research R4) |
| ValueSats | long | 0 for DD outputs; required for the sighash |
| Confirmations | int | ≥ 0 |

### DigiDollarTransaction (derived + local pending overlay)

| Field | Type | Rules |
|---|---|---|
| Txid | string | |
| Direction | enum {Received, Sent, SelfTransfer} | SelfTransfer when every DD output goes to own addresses |
| CounterpartyAddress | string? | First non-own DD output for Sent, and the first sender DD input address for Received when it can be determined |
| AmountCents | long | Net DD to or from the user |
| FeeSats | long? | Only for Sent |
| Confirmations | int | ≥ 0 |
| Timestamp | DateTimeOffset | Block time, or local submit time while pending |
| Status | enum {Pending, Confirmed, Failed} | See state machine below |

### PendingDdSend (persisted, IndexedDB)

Covers FR-017, so a pending entry survives reloads.

| Field | Type | Rules |
|---|---|---|
| Txid | string | Returned by broadcast |
| RawTxHex | string | Kept for rebroadcast; contains no secrets |
| AmountCents, FeeSats, Recipient | | For display |
| SpentOutpoints | (Txid, Vout)[] | DD and DGB inputs reserved by this send; excluded from available balance until Failed or confirmed |
| SubmittedAt | DateTimeOffset | |
| FirstMissingAt | DateTimeOffset? | Set when the tx status first comes back `unknown`; cleared if seen again (research R13) |
| LastCheckAt | DateTimeOffset? | When the backend last answered a status check for this send; unchanged while the backend is unreachable |
| MissingObserved | TimeSpan | Observed absence: each successful `unknown` check adds the time since `LastCheckAt`, capped at 2 minutes; reaching 30 minutes marks the send Failed |
| Rebroadcasted | bool | One automatic rebroadcast on first `unknown` |

The record is removed after 6 confirmations or when marked Failed (after the user has been
notified).

**Send state machine**

```text
Draft ──validate──▶ Confirming ──user confirms──▶ Signing ──▶ Broadcasting
  ▲                     │                                        │
  └──── edit ───────────┘                          ┌─────────────┴─────────────┐
                                                   ▼                           ▼
                                               Pending ──seen in index──▶ Confirmed (≥6 conf)
                                                   │
                       rejected / observed absent from mempool and chain for 30 min (FR-017)
                                                   ▼
                                                Failed
```

**Validation rules at Draft → Confirming** (FR-011..FR-016):
1. The recipient passes `DigiDollarAddressValidator.IsValid(addr, currentNetwork)`.
2. 100 ≤ amountCents ≤ 10,000,000.
2b. The DD change (selected input total − amountCents) is exactly 0 or ≥ 100 cents (FR-012,
    research R15); otherwise block and offer "Send all". "Send all" uses the full spendable balance
    and a transaction with no DD change output.
3. amountCents ≤ ConfirmedCents − (the DD in outstanding, non-Failed PendingDdSend records).
   The backend must be reachable (`NetworkHealth.Available`); otherwise Send is disabled (FR-003).
4. Available DGB covers the automatic fee (`max(0.1 DGB, feeRate × vsize)`, never user-set, FR-014).
5. The rate limiter allows the send (≤ 5 in the rolling 60 s).

### NetworkHealth (derived, in memory)

| Field | Type | Rules |
|---|---|---|
| Available | bool | false on NodeApi 503, timeout, or `enabled:false` (FR-003); disables Send and switches the UI to cached data |
| OraclePriceMicroUsd | long? | USD per DGB × 10⁶ |
| OracleHeight | int? | Block height of the price |
| NodeHeight | int | |
| NetworkHeight | int? | Best-known height from peers |
| ErrActive, VolatilityFreeze | bool | |
| DeploymentActive | bool | From `/health.deploymentActive`; false → `NotActive` UI state, no receive/send (FR-003, R16) |
| OraclePriceAvailable | bool | false when `oracle` is null/zero; hides the DGB USD equivalent (FR-019, R16); also counts as degraded for the confirm dialog (FR-027) |
| IsOracleStale | bool | `NodeHeight − OracleHeight > 20` (R6) |
| IsNodeLagging | bool | `NetworkHeight − NodeHeight > 6` (R6) |
| IsDegraded | bool | `ErrActive ∨ VolatilityFreeze ∨ IsOracleStale ∨ ¬OraclePriceAvailable` (FR-027 confirm-dialog warning) |

## Server-side (`DigiByte.NodeApi`, DD output index — research R3)

Public chain data only, with no user identifiers.

### DdOutput (SQLite)

| Field | Type | Rules |
|---|---|---|
| Txid, Vout | PK | |
| OutputKey | blob(32) | Indexed; the key in the `OP_1 <key>` script, equal to the key inside the DD address (research R3) |
| AmountCents | long | |
| CreatingTxid | string | Transaction whose OP_RETURN carries the amount |
| ScriptPubKey | blob | `5120` ‖ key |
| ValueSats | long | 0 |
| BlockHeight | int? | null while in the mempool |
| SpentByTxid | string? | Set when spent |

### DdTxSummary (SQLite)

| Field | Type |
|---|---|
| Txid (PK), BlockHeight, BlockTime, TxType, FeeSats | |

### IndexerState

| Field | Type |
|---|---|
| LastIndexedHeight, LastIndexedHash | int, string (for reorg detection: on hash mismatch, roll back to the fork point) |
