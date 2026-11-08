# Contract: WalletService additions

Public surface the UI calls. Signatures describe behaviour, not final code. All methods live on `WalletService` (research R-7) and none returns or accepts a private key.

## Common preconditions

Every method first checks the wallet:

| Condition | Behaviour |
|---|---|
| Locked | Throws `InvalidOperationException("Wallet is locked. Unlock it first.")`. No lookup is made. |
| Not HD, or watch-only (including WIF) | Throws `InvalidOperationException("Digi-ID requires an HD wallet with private keys.")`. No lookup is made. |

The UI checks `IsUnlocked` and wallet type first and shows the matching state instead of calling.

## `ScanDigiIdFundsAsync(IEnumerable<string> domains, IProgress<int>? progress, CancellationToken ct)`

Returns `IReadOnlyList<DigiIdFundFinding>`, one per distinct normalised domain.

- Each domain is normalised with `DigiIdDomain.Normalize`. A domain that fails normalisation is reported as a `CouldNotCheck` finding with the reason "not a valid site name", not thrown.
- The address is derived per domain. One `GetVerifiedUtxosAsync(address)` call per address, never batched (FR-004b). At most 4 run at once.
- A lookup that throws gives that finding `Outcome = CouldNotCheck`. The other findings are unaffected (spec edge case: partial failure).
- Only `Confirmations >= 1` counts as confirmed. Others count as pending.
- An output whose script is present and differs from the address's own standard script is excluded from the amounts and counted in `OtherOutputsCount` (FR-016). If no script is returned, the output counts as plain.
- `InFlight` is set from the sweep store.
- Reports progress as a percentage. Cancellation stops further lookups and returns what finished.
- Never throws for a network failure. It does throw for the common preconditions.

## `PlanDigiIdSweepAsync(IEnumerable<DigiIdFundFinding> selected, int feeRateSatPerVByte = 100)`

Returns `DigiIdSweepPlan`.

- Considers only findings with `Outcome = Funds`, `ConfirmedSatoshis > 0` and not `InFlight`.
- Re-verifies each selected address (one `GetVerifiedUtxosAsync` per address) so the plan uses current UTXOs. A failed re-verify excludes that item with a reason. Other outputs are filtered out exactly as in the scan, and only plain outputs are ever given to the transaction builder.
- Computes the fee per item, excludes items whose net is below 10,000 sat with reason "too small to sweep" (FR-010), and takes at most 19 items (`ReceiveGapLimit - 1`, fewer if unused addresses already handed out leave less room). The rest are reported in `Remaining`.
- Peeks that many fresh, unused, distinct receive addresses. Saves nothing (cancelling burns nothing).
- Sends nothing.

## `ExecuteDigiIdSweepAsync(DigiIdSweepPlan plan, IProgress<DigiIdSweepRecord>? progress, CancellationToken ct)`

Returns `IReadOnlyList<DigiIdSweepRecord>`, one per plan item, in plan order.

1. Rechecks the common preconditions. If the wallet is locked here, before any item, it throws, sends nothing and records nothing, and the UI asks the user to unlock and review again.
2. Saves the highest destination index plus one as `NextReceivingIndex` (`PersistAddressIndexesAsync`), so the Receive page afterwards shows a fresh unused address and not a sweep destination. If this fails, throws before any broadcast and sends nothing.
3. For each item, one at a time:
   - Skips the item if a `Sending` or `Pending` record already exists for its source address (FR-012). The existing record is returned.
   - Takes the per-address in-memory lock. A concurrent second call gets the existing record.
   - Writes a `Sending` record, re-derives the key, builds a send-all transaction (one input set, one output, no change, fee at or above the floor), signs and broadcasts it.
   - On success: sets `TxId` and the real fee, status `Pending`, and records the send in `TransactionTracker` (memo "Digi-ID recovery sweep").
   - On any failure: status `Failed` with a short reason. The funds stay on the address. The loop continues with the next item (spec scenario 5).
   - Before each item, and before writing its `Sending` record, checks the wallet is still unlocked. If it is locked mid-run, every item not yet sent is marked `Failed` with the reason "wallet locked" and the method returns. Items already broadcast are unaffected.
4. Does not throw for a single item failure. It throws only for step 1 or step 2.
5. Logs no key, raw transaction or signature.

## `GetDigiIdSweepsAsync()`

Returns `IReadOnlyList<DigiIdSweepRecord>` for the active wallet, newest first, **read from storage only**: no network lookup, so it is instant. The UI shows these at once when the section opens and then calls `RefreshDigiIdSweepsAsync` in the background, because a refresh waits on network lookups and can take many seconds (found in browser verification of T029: without this the saved list appeared only after about 20 seconds). Throws when the wallet is locked.

## `RefreshDigiIdSweepsAsync(CancellationToken ct)`

Returns `IReadOnlyList<DigiIdSweepRecord>` for the active wallet, newest first.

- For every `Pending` record, makes one `GetTransactionAsync(txId)` call (separate requests). At least 1 confirmation gives `Confirmed`. A transaction the network no longer knows about after a grace period gives `Failed`.
- A lookup failure leaves the record `Pending` and does not throw.
- For a `Sending` record without a `TxId` (interrupted run), re-verifies the source address: UTXOs still present gives `Failed`, so the address is offered again. UTXOs gone leaves it for the user to check in history.

## Pure helpers

- `DigiIdDomain.Normalize(string input) → string?` in `DigiByte.Crypto`. Returns null for empty or invalid input. Output equals `DigiIdService.ParseUri(...).Domain` for the same host.
- `DigiIdSweepPlanner` (no key, no I/O): fee, net, eligibility, the per-round cap and destination assignment from a list of candidate addresses.
- `DigiIdSweepStore`: load, upsert and list records with the state rules from the data model. Rejects an illegal transition.

## Error text

Messages are user-facing and contain no key material. Network failures say what failed ("could not reach the network, try again"), not raw exception text.
