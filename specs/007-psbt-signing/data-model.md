# Data Model: Sign an Unsigned Transaction (PSBT)

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

Everything here is in memory for one page visit. Nothing is persisted except the normal history record after a broadcast (FR-026, FR-028). Amounts are in satoshis.

## Entities

### PsbtDocument

The decoded, untrusted proposal.

| Field | Meaning |
|---|---|
| Psbt | The parsed PSBT |
| SourceFormat | Text or Binary |

**Validation**: At most 1 MB. Must decode as base64 (for pasted text) and as a valid PSBT. Must have at least one input and one output, no duplicate inputs, and pass the library's sanity check.

### ChainInputFact

What the chain says about one input. Supplied to the analyzer, so the analyzer stays free of I/O.

| Field | Meaning |
|---|---|
| Outpoint | Transaction ID and output index |
| Lookup | Found, NotFound, or Unavailable |
| Amount, Script | Values from the chain, when found |
| IsUnspent | True, false, or not checked. Checked only for this wallet's inputs |
| Confirmations | From the unspent list, when checked |

### InputAssessment

| Field | Meaning |
|---|---|
| Index | Position in the transaction |
| Owner | Wallet, Other, or Unidentified (no script in the PSBT and the lookup failed or found nothing) |
| Kind | NativeSegWit, Legacy, or Other |
| Amount | Value, when known |
| AmountSource | Chain, PsbtOnly, or Unknown |
| SignatureState | Unsigned, SignedByWallet, SignedByOthers, or Finalized |
| IsUnconfirmed | True when the chain reports zero confirmations |

**Rules**: `Owner` is `Wallet` only when the script matches the wallet's own script map (FR-004). PSBT-supplied key paths are never used (FR-005). Script-based inputs and wrapped SegWit inputs are always `Other` (FR-006).

### OutputAssessment

| Field | Meaning |
|---|---|
| Index | Position in the transaction |
| Amount | Value |
| Label | Address, Data (with readable text if any), or NonStandard |
| Address | Full address on the active network, when there is one |
| IsWallet | True only for a script in the wallet's own script map (FR-005) |

### ReviewSummary

| Field | Meaning |
|---|---|
| Inputs, Outputs | The assessments above |
| NetAmount | The spec's "total leaving this wallet": chain-verified wallet inputs less wallet outputs. Positive means leaving the wallet; negative is shown as received |
| Fee | Value, when every input amount is known |
| FeeStatus | Verified or Unverified (FR-016). Until Story 4, always Unverified when any input's owner is `Other` |
| InputsToSign | Wallet inputs not yet signed |
| OtherInputs | Number of inputs belonging to others |
| Warnings | LockTime, UnconfirmedInput, NonStandardOutput, HighFee, UnverifiedFee, OutOfRangeNote, SharedOutputsNotChecked |
| Refusal | A refusal reason, or none |
| ChainStatus | Verified or Unavailable. Based only on this wallet's inputs. A failed lookup of another participant's input with a stated amount leaves this as Verified and makes the fee unverified (FR-032) |
| NeedsAcknowledgment | True for HighFee or UnverifiedFee (FR-017) |
| CanSign | Derived; see below |
| IsComplete | True when every input is fully signed |
| CanBroadcastSigned | True when the PSBT arrived fully signed, there is no refusal, chain status is Verified, at least one input is the wallet's, and the wallet has spending keys (FR-034) |

**Derived rules**:

- `Fee` = sum of input amounts less sum of output amounts. A negative result is refused.
- `HighFee` applies when the fee is more than 10% of the amount sent to outputs that are not the wallet's. When every output is the wallet's, the comparison is with the total input amount.
- `OutOfRangeNote` applies whenever any input's owner is `Other`, because the wallet can't tell a coin beyond its search range from another participant's coin.
- `CanSign` is true only when there is no refusal, chain status is Verified, the wallet has spending keys, and `InputsToSign` is above zero.

### SigningResult

| Field | Meaning |
|---|---|
| SignedPsbtBase64 | The export form, with partial signatures and not finalized (research R3) |
| SignedCount | Inputs signed in this operation |
| RemainingCount | Inputs still needing signatures |
| IsComplete | True when a trial finalize succeeds |
| FinalTransaction | The extracted transaction, when complete. Used for broadcast |

### BroadcastResult

| Field | Meaning |
|---|---|
| TxId | Set on success |
| Error | Plain reason on failure. The signed PSBT remains available (FR-027) |

## Refusal reasons

The analyzer stops at the first that applies, in this order. Each carries a plain message and, where relevant, the input index.

| Order | Reason | Requirement |
|---|---|---|
| 1 | TooLarge | FR-002 |
| 2 | NotAPsbt | FR-002 |
| 3 | InvalidStructure (bad data, no inputs or outputs, duplicate input) | FR-002, Edge Cases |
| 4 | UnsupportedDigiDollar | FR-010 |
| 5 | UnsupportedSignatureType | FR-009 |
| 6 | InputNotIdentified, with a retry offered | FR-035 |
| 7 | UnsupportedTaproot | FR-010 |
| 8 | MissingPreviousTransaction | FR-011 |
| 9 | PreviousTransactionMismatch | FR-011 |
| 10 | AmountOrScriptMismatch (wallet or other input) | FR-007, FR-032 |
| 11 | InputSpentOrUnknown | FR-007 |
| 12 | OutputsExceedInputs | Edge Cases |
| 13 | NothingToSign, shown as a message rather than an error | FR-012 |

## State transitions

```text
Empty ──load──> Loading ──> Refused ──(load another)──> Empty
                   │
                   ├──> NothingToSign
                   │       └── all inputs signed, one is the wallet's ──> ReadyToBroadcast
                   │               └── acknowledge, limit, warnings, PIN ──> PrepareSigned ──> Signed (complete)
                   │
                   └──> Review ──cancel──> Empty
                          │
                          └── acknowledge, limit, warnings, PIN ──> Signing
                                                                      │
              re-check fails <── Refused ───────────────────────────┤
                                                                      ▼
                                               Signed (complete or incomplete)
                                                 │                │
                                               export      complete only: confirm
                                                                  ▼
                                                             Broadcasting
                                                              │         │
                                                         Broadcast   Failed
                                                          (done)    (export or retry)
```

Leaving the page or the wallet locking moves to `Empty` from any state, and the PSBT is discarded.
