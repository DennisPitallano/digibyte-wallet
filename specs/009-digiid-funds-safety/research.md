# Research: Digi-ID Address Funds Safety

Findings from reading the existing code. Each item records the decision, why, and what was rejected.

## R-1. Do not reuse the existing recovery sweeps

**Finding**: `WalletService` already has `ScanExternalWifAsync`, `ScanCurrentWalletOldPathsAsync` and `SweepExternalFundsAsync` (about lines 1706 to 1865). They differ from this spec in three ways:
- The scans wrap each lookup in `catch { }`, so a failed lookup looks like "no funds". That breaks FR-007.
- `SweepExternalFundsAsync` merges UTXOs from every source into one transaction. That breaks FR-008a.
- It pays a fixed `DeriveReceivingAddress(0)`. That breaks the one-destination-per-address rule.

**Decision**: Write new methods. Reuse only `DigiByteTransactionBuilder.BuildSendAllTransaction` (no change output, so the net amount is exact), `HdKeyDerivation.DeriveDigiIdKey`, `TransactionTracker.RecordSendAsync`, and the fee floor logic.

**Rejected**: Adding flags to the existing sweep. It would change behaviour of recovery flows that already work.

## R-2. Lookups must be able to fail loudly, one address at a time

**Finding**: `IBlockchainService.GetVerifiedUtxosAsync(address)` throws when the source cannot answer and "never returns an empty list for a failure". `GetUtxosAsync` does not promise that. `FallbackBlockchainService` already implements the verified call across explorers and the node API.

**Decision**: Use `GetVerifiedUtxosAsync` for both the scan and the pre-sweep re-check. Call it once per address, never `GetUtxosAsync(IEnumerable<string>)`, so the explorer cannot link the sites (FR-004b). Run at most 4 at a time. 50 lookups at roughly 1 s each finish in about 13 s, inside SC-002.

**Known limit**: Separate requests from one browser still share an IP and a time window. This feature stops the *request content* from linking the sites, not network-level correlation. The Help text should not promise more.

**Rejected**: Batched lookup (links sites). A random delay between lookups (slows SC-002 for little gain).

## R-3. Domain handling must match what login signs

**Finding**: The address depends on `DeriveSiteIndex(domain)`, which lowercases the domain and hashes it. At login, the domain is `httpUri.Host` from `ParseUri`, which drops the port, path and scheme. Hand-typed domains must produce the same string or they derive a different address and silently find nothing.

**Decision**: Add `DigiIdDomain.Normalize(string input)` in `DigiByte.Crypto` next to `DigiIdService`. It trims, accepts a bare host or a URL (`digiid://`, `https://`, with port, path or query), takes the host the same way `ParseUri` does, lowercases, and rejects empty or invalid input. Its tests compare against `ParseUri(...).Domain` for the same host.

**Also found**: The login history is global, not per wallet (`digiid_login_history`, written by `Identity.razor`), and each entry stores the address that was used. With several wallets, a stored address can belong to another wallet.

**Decision**: Derive the address from the domain with the active wallet and ignore `Entry.Address`. This fits the spec's "active wallet only" assumption.

## R-4. Destination addresses and the 20-address gap limit

**Finding**: `GenerateNewReceivingAddressAsync` persists the new index before returning it and throws once `ReceiveGapLimit` (20) unused addresses are out, because a restore only looks 20 past the last used one. A sweep of N addresses needs N fresh destinations.

**Decision**:
- Peek the next N unused indexes when building the plan, without saving anything. Cancelling then burns no addresses.
- On confirmation, save **the highest chosen index plus one** as `NextReceivingIndex` with the existing `PersistAddressIndexesAsync` before broadcasting. The Receive page shows the address at exactly `NextReceivingIndex` (`GetReceivingAddressAsync`), so saving the highest destination itself would make it show an address that already holds swept funds, which invites address reuse. If saving fails, abort before any broadcast, as the receive flow does.
- Cap a single confirmation at `ReceiveGapLimit - 1` = 19 swept addresses, so that index plus one stays inside the 20-address window a restore scans past the last used address. If unused addresses already handed out leave less room, the plan is capped lower. The address the Receive page already shows is itself one of the unused addresses, so a fresh wallet fits 18 destinations; 19 is reached only when the wallet's used count is ahead of its shown address (found while testing: the room is `UsedReceivingCount + ReceiveGapLimit - 1 - firstDestinationIndex`). The plan shows "N more can be swept after these confirm". Once the first round confirms, those destinations count as used and the next round can proceed.

**Spec impact**: Scenario 5 says one confirmation covers all addresses. Add an assumption that a confirmation covers at most 19.

**Rejected**: Reusing one destination (links sites, already rejected in clarification). Ignoring the gap limit (funds could disappear after a restore).

## R-5. Fees, dust and unconfirmed funds

**Finding**: The wallet enforces a floor of 100 sat/vB (`Math.Max(rate, 100)`). A Legacy P2PKH input is about 148 vB, so one input plus one output is about 192 vB, around 19,200 sat. `DigiByteTransactionBuilder.EstimateFee` already supports `ScriptPubKeyType.Legacy`. Batch send already enforces a 0.0001 DGB minimum amount.

**Decision**:
- Fee per address = `EstimateFee(inputCount, 1, rate, Legacy)`.
- An address is "too small to sweep" when net (total minus fee) is below 0.0001 DGB.
- Sweep only confirmed UTXOs. Unconfirmed amounts are shown as pending and excluded. This also avoids the node's ancestor-chain limit noted in `SendAsync`.
- Show the fee as an estimate. The final fee comes from the signed transaction.

## R-6. Idempotency and the persisted record

**Finding**: Spending the same UTXOs twice is rejected by the network. The risk is a confusing second attempt, not lost funds.

**Decision**:
- Write a `Sending` record for the source address before broadcast and update it with the txid after.
- An in-memory per-address lock blocks double clicks.
- While a record for that address is `Sending` or `Pending`, the UI does not offer it again.
- After a reload with a `Sending` record that has no txid (a crash between sign and broadcast), the next scan re-verifies the address. If UTXOs are still there, the record is marked `Failed` and the address is offered again. If they are gone, the user sees the funds were moved and is pointed to transaction history.
- Status refresh uses `GetTransactionAsync(txId)` one record at a time.
- Completed sweeps also go into `TransactionTracker` (memo "Digi-ID recovery sweep"), so they appear in normal history (FR-013).

**Rejected**: Keeping status only in memory (violates the clarified FR-011a).

## R-7. Where the code lives

**Finding**: `WalletService` holds the unlocked HD session privately. `Identity.razor` injects `WalletService` directly.

**Decision**: Scan, plan and execute are `WalletService` methods so keys never leave it. Pure logic goes in `DigiIdSweepPlanner` and `DigiIdSweepStore`. None of the new public types carries a `Key`.

**Rejected**: A separate service that receives keys from `WalletService`. That would widen where a private key can leak (FR-015).

## R-8. Wallet types

**Finding**: `GetDigiIdAddress` and `SignDigiIdAsync` already refuse when `_hd == null || _hd.IsWatchOnly`. WIF wallets have `_hd == null`.

**Decision**: The recovery section shows a "needs a wallet with a seed phrase" message for watch-only and single-key (WIF) wallets, and for a locked wallet an "unlock first" state. No check or sweep is offered. WIF wallets have no `m/13'` addresses.

## R-9. Localization and Help

**Finding**: `LocalizationService` lists 10 locales, but only `en.json` and `es.json` exist. English is the fallback. The Identity page text is currently hard-coded English, and `Help.razor` is hard-coded English data.

**Decision**: New Identity strings use localization keys in `en.json` and `es.json`. Other locales fall back to English, which is the wallet's existing behaviour for missing strings. The Help entry follows the existing hard-coded pattern in `Help.razor`.

**Spec impact**: FR-003 says "every language the wallet supports". Add an assumption that this means every locale file the wallet ships (English and Spanish), with English as the fallback elsewhere.

## R-10. Warning placement

**Finding**: Digi-ID addresses are shown in four places: the pending-approval panel (`Identity.razor` around line 98), the success panel (around line 234), the login detail modal (`Field("Signing address", ...)`, line 40), and the Help entry that explains the derivation path.

**Decision**: One `DigiIdAddressWarning` component, rendered next to each. The copy button sits in the same row, so the warning is always visible when the address is copied (scenario 6) without extra logic.

## R-11. Tokens and other non-plain outputs (FR-016)

**Finding**: `BuildSendAllTransaction` spends every input it is given. A DigiDollar or other token output on a Digi-ID address would be spent as plain DGB. `UtxoInfo.ScriptPubKey` exists, but the code comment in `SendAsync` notes that Esplora does not always return it. I did not verify how DigiDollar outputs look to the lookup.

**Decision**: Compare each output's script with the address's own standard script (`address.ScriptPubKey`). If a script is present and differs, leave the output out of the sweep and every amount, and count it for a notice. If no script is returned, treat the output as plain, because the lookup was by that address. Add the count to the finding (see the data model), and add the guidance to Help.

**Limit**: A token output that pays the address's own standard script cannot be told apart this way. The spec and Help state this limit.

**Rejected**: Treating this as out of scope with no protection (risks spending tokens as plain DGB). Blocking the whole sweep when any odd output exists (blocks recovery of the plain funds).

## R-12. Keeping status current

**Decision**: A refresh button, plus a refresh about every 30 seconds while the section is open and at least one record is `Pending` (spec FR-011). The timer stops when the section closes or nothing is pending, so FR-014 (no background monitoring) holds. Each refresh is the per-record `GetTransactionAsync` calls from `RefreshDigiIdSweepsAsync`.

## R-13. Locking during a sweep

**Decision**: `ExecuteDigiIdSweepAsync` checks that the wallet is still unlocked before each item. If it locks mid-run, every item not yet sent becomes `Failed` with the reason "wallet locked" and is offered again after unlocking. Items already broadcast are unaffected. `RefreshDigiIdSweepsAsync` needs no keys, but still requires an active wallet.

## Open items

None. All plan-level questions are resolved.
