# Research: Sign an Unsigned Transaction (PSBT)

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-11-05

Behavior marked *tested* was observed in two throwaway programs run on 2026-11-05 against NBitcoin 9.0.5 and the project's DigiByte network definitions. Those programs are not part of the repository. The tests in this feature must establish the same behavior.

## R1. How to sign without touching other inputs

**Decision**: Sign one input at a time with `PSBTInput.Sign(Key)`, only for inputs the wallet has classified as its own. Do not call `PSBT.SignWithKeys`.

**Rationale**: *Tested*: `SignWithKeys` added a signature to a 2-of-2 P2WSH input containing the wallet's key, which FR-006 forbids. `PSBTInput.Sign` signed only the chosen input, and signing an already signed input again left one identical signature.

**Alternatives considered**: `SignWithKeys` with a pre-filter would still sign any matching input in the PSBT, so it is unsafe. `MultisigService.SignPSBT` wraps `SignWithKeys`, so it is not reused here.

## R2. How to refuse non-standard signature types

**Decision**: Read `SighashType` on every input after parsing and refuse the PSBT if any value is set to anything other than `SigHash.All`. A missing value is acceptable.

**Rationale**: *Tested*: the requested value survives a round trip and can be read, but `SignWithKeys` ignored it and signed with `ALL`. The library will not refuse on its own.

## R3. How to validate a completed transaction

**Decision**: Use `TryFinalize` on a clone of the signed PSBT, then `ExtractTransaction`. Do not use `TransactionBuilder.Verify` for this decision.

**Rationale**: *Tested*: `TryFinalize` rejected a tampered partial signature with a script error (`NullFail`). `TransactionBuilder.Verify` reported failure for a validly signed transaction because its default policy treats a 0.1 DGB fee as too high, so its result mixes wallet policy with validity. The existing `DigiByteTransactionBuilder.Verify` has this limitation.

**Export form**: The exported PSBT keeps partial signatures and is not finalized, so the receiving tool can finalize or combine it. Only the clone is finalized.

## R4. How to check input facts against the chain

**Decision**: Use two existing `IBlockchainService` calls:

| Need | Call | Result used |
|---|---|---|
| Prove a wallet input that has PSBT data (FR-007, FR-008) | `GetVerifiedUtxosAsync(address of the stated script)` | Entry matching txid and output index, comparing amount. Finding it at that address also confirms the script |
| Amount and script of an input with no PSBT data (FR-023), or of another participant's input (FR-032) | `GetTransactionAsync(prevTxId)` | `Outputs` entry whose `Index` matches. Script from `ScriptHex`, or derived from `Address` when empty |
| Prove a wallet input that had no PSBT data is unspent | `GetVerifiedUtxosAsync(address)` | As in the first row, after the transaction lookup identifies the script |

**Rationale**: `TxOutput` carries `Index`, `AmountSatoshis`, and `ScriptHex`. `BlockchainApiService`, `BlockbookApiService`, and `NodeApiBlockchainService` were each read and all populate them. `NodeApiBlockchainService` and the node API client depend on the node's own view of transactions, so a lookup there can fail for an old transaction; that is handled as "not found" below.

No new chain API is needed, so no explorer or node change is required.

**Failure handling**:

| Situation | Wallet input | Another participant's input |
|---|---|---|
| Unspent list call throws (all sources failed) | Chain unavailable: signing blocked (FR-013) | Not called |
| Transaction lookup throws | Only reached when the input has no PSBT data. It can't be identified yet: treated as unknown, signing blocked, retry offered | Fee unverified |
| Transaction not found | Only reached when the input has no PSBT data. It can't be identified: treated as unknown, signing blocked, retry offered | Fee unverified |
| Stated amount or script differs | Refused, naming the input | Refused, naming the input (FR-032) |
| Not in the unspent list | Refused as spent or unknown | Not checked: other participants' coins are not queried for spent status |

The "Fee unverified" results apply when the PSBT states the input's amount and script. An input with neither, whose lookup fails or finds nothing, can't be identified as anyone's, so signing is blocked with a retry (FR-035), and the fee is therefore always known once signing is possible.

**First release**: lookups of other participants' inputs with PSBT data arrive with Story 4. Before then nothing would compare their amounts, so a lookup would only tell the chain data source about those coins for no benefit. Their fee is labeled unverified instead. Inputs with no PSBT data are still looked up, because that's the only way to identify them.

`GetUtxosAsync` can't be used for this. Esplora and the node API in its lenient mode catch errors and return an empty list, so an outage would read as a spent coin. `GetVerifiedUtxosAsync` throws when a source can't answer. It has the same shape as `HasAddressHistoryAsync`: a default interface method, overridden by Esplora and the node API, with `FallbackBlockchainService` trying explorers and then the node where that's allowed, without setting a cooldown or the outage state. Its result isn't cached.

**Wrong network**: *Tested*: the same key gives an identical script on mainnet and testnet, so ownership matching cannot detect the network. The coin not being found on the active network's chain does, so no separate network check is added.

**Alternatives considered**: Adding a raw-transaction call to `IBlockchainService` would let the wallet fill legacy inputs, but the spec leaves this out and it would touch all six implementations.

## R5. How ownership is established

**Decision**: Compare each input's script, from the chain lookup where possible and otherwise from the PSBT, with a script-to-key map built from the wallet's own derivation: the addresses it currently scans (native SegWit across every scanned index, legacy across receiving 0-19 and change 0-9), or both address forms of the imported key. PSBT-supplied key paths are not read. The owned scripts are read after waiting a limited time for address discovery, so a PSBT loaded straight after unlock still recognizes coins on newly found addresses.

**Rationale**: This is the range `WalletService.GetAddressKeyMap` and the WIF branch of `SendAsync` already use. The map stays inside `WalletService`, and callers only learn which scripts are owned (see R6).

*Tested*: NBitcoin's `IsScriptType(ScriptType.Taproot)` recognizes Taproot scripts and rejects the other three types, so wrapped SegWit (a plain script hash) falls through to "someone else's" as the spec requires.

## R6. Where the code lives

**Decision**: Three layers, following the `IDdSessionProvider` precedent for exposing wallet capabilities without exposing keys.

| Layer | New code | Reason |
|---|---|---|
| `DigiByte.Crypto` | `Psbt/PsbtDocument`, `Psbt/PsbtAnalyzer`, `Psbt/PsbtSigner` | Pure and free of I/O. Decoding, classification, totals, signing |
| `DigiByte.Wallet` | `IPsbtKeyProvider`, `PsbtSigningService` | Chain lookups, signing flow, broadcast, history |
| `DigiByte.Web` | `Pages/PsbtSign.razor`, Settings entry, `Services/PsbtConfirmationFlow`, `Services/PinAttemptThrottle` | Input, review, check order, PIN, warnings, export |

`WalletService` implements `IPsbtKeyProvider`, so private keys never leave it. `PsbtSigningService` holds no PSBT state, which keeps FR-028 simple: the PSBT lives in the page component and is dropped with it.

**Alternatives considered**: Adding the flow to `MultisigWalletService` would risk the multisig flow (FR-029), and that service stores PSBTs, which FR-028 forbids. Exposing keys the way `GetPrivateKeyForMultisig` does would widen key access for no gain.

## R7. How to verify the PIN

**Decision**: Add `WalletService.VerifyPinAsync(walletId, pin)`, which decrypts the stored seed, clears the bytes, and returns whether it succeeded. Do not call `UnlockWalletAsync`.

**Rationale**: `SpendingLimits.razor` and `BackupSeed.razor` verify the PIN with `UnlockWalletAsync`, but that method rebuilds the session and changes the active wallet. Those are side effects a signing confirmation should not have.

**Throttling**: `Unlock.razor` keeps its lockout state in the component: 3 failures, then 15 seconds doubling, up to 5 doublings. This rule moves into a shared `PinAttemptThrottle` class in `DigiByte.Web`, used by both `Unlock.razor` and the signing page, so it exists in one place (Principle IV). Unlock behavior must not change. The state resets when the page is left, so it slows attempts rather than guaranteeing a limit.

**Biometrics**: `BiometricService.IsConfirmSendEnabledAsync` decides whether biometrics are enabled and `VerifyIdentityAsync` performs the check, matching `Send.razor` and `ConsolidateUtxos.razor`. If the biometric check fails or is cancelled, the user can use the PIN instead.

## R8. Reuse of send protections

**Decision**: The page applies the same checks as `Send.razor` before signing.

| Check | Existing code | Behavior when it can't run |
|---|---|---|
| Spending limits (FR-033) | `SpendingLimitService.CheckLimitAsync(walletId, amountDgb)` | Does not block, as on Send |
| Address and amount warnings (FR-031) | `SendRiskEvaluator.EvaluateBatch`, `KnownAddressProvider`, `BakuService.ConfirmSendRiskAsync` | Does not block, as on Send |

**Testable order**: `PsbtConfirmationFlow` in `DigiByte.Web` runs these checks and the PIN or biometric step in a fixed order, taking each as a delegate. Both the sign path and the already signed path use it, so the order that protects funds has automated tests rather than only manual checks.

**Finding that changed the spec**: spending limits were missing from the spec. Without FR-033, this feature would be a way around a limit the user set.

## R9. Recording history after broadcast

**Decision**: Call `TransactionTracker.RecordSendAsync` once per transaction: the largest outside output as the address, the amount leaving less the fee when all inputs are the wallet's, and the amount leaving with a zero fee on shared transactions. Skip recording when the wallet gains overall, and leave that case to normal chain sync. After broadcast, clear the same caches as `Send.razor`.

**Rationale**: `RecordSendAsync` ignores a second record with the same transaction ID, so one call is all that can take effect. On a shared transaction the wallet cannot say what share of the fee is its own.

## R10. Recognizing DigiDollar transactions

**Decision**: Refuse when `DigiDollarScripts.IsDigiDollarVersion` matches the transaction version, or when `TryParseOpReturn` or `TryParseMintOpReturn` matches any output.

**Rationale**: *Tested*: the version marker survives a PSBT round trip. Checking outputs as well catches a PSBT carrying the data output under a plain version.

## R11. Input format and file handling

**Decision**: Decode pasted text with `Convert.FromBase64String` and call `PSBT.Load`. Do not use `PSBT.Parse` directly. Read files with `InputFile` and a 1 MB limit, as `MultisigImport.razor` does. For download, add a small binary function beside `downloadTextFile` in the existing `js/download.js`, which only handles text.

**Rationale**: *Tested*: `PSBT.Parse` also accepts hex, which the spec does not offer. *Tested*: invalid data raises `FormatException`, and a duplicate input raises `InvalidOperationException`, so decoding must catch both and report invalid input. *Tested*: `GetFee` returned a negative value when outputs exceeded inputs, so the analyzer must check for this itself.

## R12. CSP

**Decision**: No CSP change. `download.js` already ships with the wallet and already downloads through a local blob URL, and this feature adds no new origin.

## Related findings outside this feature

- Batch send history and `WalletInfo.NextChangeIndex` handling were both confirmed and fixed separately, and are merged into `main` (`1b6eb0c`). The wallet's scanned address range now grows with use.
- `digiexplorer.info/testnet/api` returned an HTML page instead of API data on 2026-11-06. Testnet testing of this feature (SC-005) depends on a working testnet chain source, which may have to be a local DigiByte Core node.
