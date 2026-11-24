# Research: Find Payments Past the Normal Search

Findings from reading the code the feature touches and from running the wallet. Each entry is a decision the plan relies on. Line numbers are as of `main` at `e76c33a` and are approximate: several have drifted, so find each member by name.

## R1. What the automatic search does today

- **Finding**: `WalletService` always scans receive indexes `0..19` (`MinScannedReceive`). Once per unlock, `FindUsedReceiveAsync` walks on from `UsedReceivingCount` until 20 addresses in a row are unused (`ReceiveGapLimit`) and it has reached at least `ScannedReceiveCount + 20`. It gives up, changing nothing, after 500 lookups (`MaxAddressSearch`). A fresh restore therefore checks indexes 0 to 39, and further while it keeps finding used ones.
- **Checked in the running wallet**: a watch-only import of an account key whose first paid addresses are 22 and 23 showed its full balance with no manual step.
- **Consequence**: the feature is for a paid address more than 20 unused addresses past the last one found (index 40 or later on a fresh restore). The spec's Context and examples were corrected to say so.
- **The existing wider search**: `ScanForMissingReceiveFundsAsync` (`WalletService.cs:1129`) is the same walk with a gap of 500 and a cap of 2,000 lookups. It raises `UsedReceivingCount` to one past the highest used index. It has no cancel, reports progress as a percentage only, and throws on any failure, keeping nothing.

## R2. The checked addresses are one count today; they must become a set

- **Finding**: every reader derives the receive addresses from a single number, `ScannedReceiveCount = max(20, NextReceivingIndex + 1, UsedReceivingCount)`. The two that matter are `GetAllAddresses` (`:956`, feeds balance, history, sends, gateway scope) and `GetAddressKeyMap` (`:1034`, feeds signing). Both loop `0..ScannedReceiveCount-1` and are cached on `(walletId, ScannedReceiveCount, ScannedChangeCount)`.
- **Problem**: a payment found at index 3,000 would make every refresh read 3,001 addresses. The code already avoids this elsewhere ("a value this high would be scanned on every balance refresh from now on", `:1229`).
- **Decision**: a pure class, `ReceiveScanSet`, in `DigiByte.Wallet/Services/`, computes the ordered list of receive indexes to check from the wallet's saved address fields (see [data-model.md](data-model.md)): a base run that only grows, individually watched indexes beyond it (found paid, or handed out by the wallet), and a 20-address tail after the highest paid one. `GetAllAddresses` and `GetAddressKeyMap` iterate that list. Their cache key gains the set's fingerprint.
- **Rationale**: it is wallet logic with awkward edges (the base run growing over a watched entry, the address on show being a watched entry), and a pure class is what lets Constitution III's tests be written first. For a wallet with no watched entries it returns exactly `0..ScannedReceiveCount-1`, so nothing changes for anyone who never uses the wider search (FR-025, SC-007).
- **Alternatives considered**: raising `UsedReceivingCount` as today (refresh cost grows with distance; rejected by clarification). A second wallet record for far-off addresses (two places for balance, history and signing to read; easy to miss one).

## R3. Other readers of the count

Each of these reads the count or `UsedReceivingCount` directly and needs a decision. All are in `WalletService.cs` unless noted.

| Reader | Today | With watched entries |
|---|---|---|
| `NextNewReceivingIndex` (`:1121`) | `max(Next+1, Used)` | also at least one past the highest paid watched entry (FR-015) |
| Hand-out limit (`:1092`) | refuses at `Used + 20` | refuses at `anchor + 20`, where `anchor` is `Used` or one past the highest paid watched entry, whichever is larger (FR-026 unchanged in effect) |
| `FindUsedReceiveAsync` | one walk from `Used` | the same walk, then the watched pass (R4) |
| `BuildHotHints` (`:827`) | address on show and the 3 before it | unchanged; it takes indexes, not the count |
| Digi-ID sweep room, in `PlanDigiIdSweepAsync` (`:2777`) | `Used + 20 - 1 - firstIndex` | uses the same `anchor` as the hand-out limit |
| Digi-ID sweep, address on show, in `ExecuteDigiIdSweepAsync` (`:2867`) | sets `NextReceivingIndex` to highest destination + 1 directly | must go through the hand-out rule: this is the only other writer of `NextReceivingIndex`, and left as it is it would turn the base run into every address up to the destinations (second review finding) |
| `Receive.razor` address list (`:441`) | lists `0..NextReceivingIndex` | must list only the checked indexes once the address on show is in the tail; otherwise it would page through thousands of never-issued addresses |
| `GetScopeAsync` (gateway, `:665`) | `GetAllAddresses()` | picks up watched entries with no change |

The Receive list is the one touchpoint outside the wallet library. It is in scope because FR-015 makes the next new address jump past the highest paid watched entry.

## R4. Keeping up after a far-off find (FR-014b)

- **Decision**: after its existing base walk, the automatic search makes a watched pass, only for a wallet that has watched entries: it looks up every watched entry not yet marked paid, then walks on from one past the highest paid entry **as it stood when the pass began** until 20 in a row are unused, capped at 500 lookups like the base walk. All results are applied once at the end: an index found used is marked paid (added if it was a tail index). If there is no paid entry there is no walk. If the walk hits the cap, the whole pass is discarded, including results for the unpaid watched entries: the cap is the sign of a source that cannot be trusted, and marking an entry paid cannot be undone and moves the hand-out limit (third review finding). The pass does not run when the base walk gave up at its own cap. Results are applied to the wallet's watched list as it stands at that moment, so an address handed out during the pass is kept.
- **Why "as it stood when the pass began"**: if the walk started from a highest paid entry that the same pass had just raised, it would skip the old tail. A payment sitting in the old tail would then leave the checked set when the tail moved (second review finding).
- **Rationale**: the outside issuer keeps handing out addresses just past the last one, and addresses this wallet handed out get paid. Without the pass, each new paid order would need a manual search, and the 20-unpaid hand-out limit would never move on.
- **Base walk reach**: unchanged, and now written as `BaseCount + 20`. It must not be read as "highest checked index + 20", which after a far-off find would walk thousands of addresses on every unlock (analysis finding G7).
- **Cost**: for a wallet with watched entries, the number of unpaid watched entries plus at least 20 lookups per unlock; none for a wallet without (SC-007).

## R5. When the base run grows over a watched entry

- **Decision**: a watched entry whose index falls inside the base run is dropped; the base run now covers it. Nothing else is ever removed, and nothing is "absorbed": the base run grows only through `UsedReceivingCount` (the base walk) and `ReceiveBaseFloor`, never because a watched entry happens to sit next to it.
- **Rationale**: one threshold (`BaseCount`), one removal rule. An earlier draft absorbed adjacent entries and compared against two different counts, which two implementers would have computed differently (analysis finding G6).

## R6. The search itself

- **Decision**: a new method on `WalletService`, `SearchReceiveAddressesAsync(upTo, progress, cancellationToken)`, replaces `ScanForMissingReceiveFundsAsync`. It checks every index below `upTo` that is not in the base run and not already a paid watched entry, in batches of 20 with 10 lookups in flight. Tail indexes and unpaid watched entries are looked up too: a payment sitting in the tail must become a watched entry before a higher find moves the tail away from it (analysis finding G3). After each batch it marks the used indexes paid and saves if anything changed. It returns a result record with an outcome (see [contracts/receive-search.md](contracts/receive-search.md)); it does not throw for a cancel, a failed lookup or an untrusted source.
- **Cancel**: checked between batches; a batch in flight is left to finish. The batch is 20, not the automatic walk's 50, for SC-004: outside the wallet 20 lookups take about 1.3 seconds, and at the slowest rate seen inside it (about 100 lookups in 13 seconds) about 2.6, both inside the 5-second limit. A batch of 50 could take over 6 (analysis finding F6). Progress is reported per batch, which is also FR-018's "at least once for every 20 addresses".
- **Failure**: `HasAddressHistoryAsync` throws on any failed lookup and never reports "unused" in its place (`BlockchainApiService.cs:324`, `FallbackBlockchainService.cs:719`). A batch with a failed lookup ends the search as `Failed`, with the reach set to the end of the last complete batch. Used indexes from earlier batches are already saved (FR-022).
- **No walk-until-gap**: the search covers the whole chosen range (FR-012) and stops there (FR-012a, clarified 2026-11-24). Today's scan extends itself 500 past each find; this one reports when a find sits in the last 20 addresses of the range and lets the person decide. The watched pass (R4) picks up payments in the 20 addresses after the highest find at the next unlock either way. The 2,000-lookup "can't be trusted" cap goes away with the walk; R7 replaces it.
- **Lookups go straight to `HasAddressHistoryAsync`**, not through `IsAddressUsedAsync`. The latter also seeds the history sync's in-memory snapshot with each address's summary (`WalletHistorySync.Offer`), which is right for the 40-address automatic walk and wrong for 5,000 addresses the wallet will not watch.
- **Existing tests**: `DeepScan_*` in `ReceiveAddressTests.cs` (3 tests) move to the new method. `DeepScan_WhenEveryAddressShowsHistory_FailsInsteadOfTrustingIt` becomes the control-address test.

## R7. Control address (FR-023)

- **Decision**: before checking any wallet address, the search asks the data source about the SegWit address of a private key generated for that purpose and discarded. If it reports history, the outcome is `Untrusted` and nothing is checked or saved. If the lookup fails, the outcome is `Failed` with reach 0.
- **Rationale**: an address made from fresh random bytes cannot have been paid, and it shares nothing with the wallet, so asking about it reveals nothing. It catches the failure that matters in practice: the development mock source reports every address as used (seen in the dev build on testnet: "no unused addresses in 500 lookups").
- **Not secret material**: the key is not the user's, protects nothing and is never stored, so Constitution I and II are not engaged. It is still generated in the browser.
- **Alternatives considered**: a wallet address beyond the range (reveals another wallet address, and could one day be paid). A fixed well-known unused address (cannot be certain it stays unused).

## R8. Which data source answers

- **Finding**: `FallbackBlockchainService.HasAddressHistoryAsync` asks explorers only ("the node can't answer this"), sets no cooldown and no outage state on failure, and is not cached. When the light-client gateway is the first source, address summaries are refused and discovery falls back to this same call.
- **Consequence**: the search behaves the same in every source mode, uses a kind of request the wallet already makes to a destination it already uses (FR-027), and a rate-limited search cannot take the explorer away from balance and send.
- **Privacy**: because the explorer is asked whatever the wallet's usual source, a search sends up to 5,000 of the wallet's addresses to one explorer within minutes. The automatic walk already does this for about 40 addresses per unlock, so the kind of exposure is not new, but the scale is. The card says so before the search starts (FR-027a, clarified 2026-11-24). `SELF_CUSTODY.md` already states that the explorer sees the addresses the wallet asks about; no document needs correcting, but the user guide entry for the search should repeat the note.
- **Gateway mode**: found addresses reach the gateway through `GetScopeAsync`, the same way an address added by today's wider search does. Not exercised here; see Open points.

## R9. Speed (SC-003)

- **Measured 2026-11-24**: 300 lookups against `digiexplorer.info`, 10 in flight, took 20 seconds with no errors: 15 a second, so about 5.6 minutes for 5,000. One run, from a desktop, outside the wallet.
- **In the wallet**: a fresh import made about 100 lookups in 6 to 13 seconds while also loading balance and history, which is slower per lookup. The 10-minute target has margin but is not confirmed for a full 5,000-address search in the browser. Quickstart measures it.

## R10. Knowing a wallet looked empty, and how it came to be on the device

- **"A completed load found nothing"** already exists: `FirstReceiveStore` holds `Empty` for a wallet once a complete load, taken after the address search settled, returned no transactions (spec 025; tightened by pull request 109). It moves to `History` or `Celebrated` when a payment appears. The offer reads it; it does not add a second notion of "empty".
- **Origin is not recorded**: `WalletInfo.WalletType` is `hd`, `xpub` or a single-key type. `CreateWalletAsync` serves both Create and Recover, so the service cannot tell them apart (the same finding as spec 025, R2). A created wallet is also marked `Empty` at once, so `Empty` alone does not mean "restored".
- **Decision**: the three pages that bring a wallet onto the device record its origin through a small store: `CreateWallet.razor` writes `created`, `RecoverWallet.razor` writes `restored`, `ImportXpub.razor` writes `watch-only` (FR-002a). No value means the wallet predates the feature (FR-002b).
- **Rationale**: follows the precedent of `MarkCreatedEmptyAsync`, and leaves the creation path untouched.

## R11. Where the offer and the search live in the UI

- **Offer**: a component, `SearchFurtherOffer.razor`, in Home's empty-state branch, above `ReceiveEmptyState`. It is not shown in the "still looking" branch (pull request 109) or the "unable to load" branch, which gives FR-002's "not before a load has completed" for free. A scoped `SearchOfferCoordinator` in the web app answers "show it?" from a pure rule in the library.
- **When the offer counts as answered** (clarified 2026-11-24): when the person dismisses it, or when a wider search finishes. A search that fails, is cancelled, is refused or has nothing to check leaves it in place, because the person it was for still has an empty wallet and no result. The Recovery page records it, on the `Finished` outcome; with today's search, before Story 2, on the search returning without throwing.
- **Search**: the existing "Current Wallet — Missing Payments" card in `Recovery.razor` gains the count field, progress as a count of addresses covered, a cancel button and the richer result. The page takes `?search=receive` to open scrolled to that card (FR-004).
- **Leaving the page**: `Recovery.razor` cancels the search when disposed. Locking clears the wallet's session; the search compares the session it started with before each batch and ends as `Cancelled` (FR-024).
- **Wording**: new strings go through `LocalizationService` (`L.T`) with keys in `en.json` and `es.json` (FR-028). The card's existing text is hard-coded English; the strings this feature touches are moved to keys, the rest of the page is left.

## R13. Addresses the wallet hands out, and the shape of the model (FR-015c)

- **History**: three drafts of this model failed review. The first kept only paid indexes and a tail, and dropped an address the wallet had handed out when the tail moved. The second added a separate "issued" list but still derived the base run from the address on show, so the base run shrank when that address first left it (losing addresses handed out earlier) and jumped to thousands when that address was paid. Both failed for the same reason: "the address on show" stops meaning "the end of an unbroken run" once it is thousands of positions away.
- **Decision**: three independent parts (data-model.md section 2). A base run whose length only grows, protected by a saved floor that is set at the moment the address on show leaves it. One list of watched indexes, each marked paid or not, never removed while beyond the base run. A tail that follows the highest paid entry. The address on show feeds the base run only while it is inside it.
- **Rationale**: every invariant is now a property of one part. Nothing shrinks the base run. Nothing removes a watched entry. Only the tail moves, and the search and the watched pass both look the tail up before anything can move it.
- **Alternatives considered**: two lists, paid and issued (an entry had to move between them, and the base-run rule depended on which list held the address on show). Migrating existing wallets to set the floor at load (changes saved data for wallets that never use the feature; the `NextReceivingIndex + 1` term already covers them).

## R14. Found funds and the first-payment celebration (FR-013a)

- **Problem found in analysis**: `FirstReceiveRule` treats any receipt seen by a wallet in state `Empty` as its first payment, whatever its age (spec 025, R5). A restored wallet that loaded empty is `Empty`; the load after a search found old payments would celebrate them as "Your first DGB", with their total. Spec 025 says the celebration never fires for recovered wallets that have history.
- **Decision**: a helper, `MarkHasHistoryAsync`, sets a wallet's first-receive state to `History` through `FirstReceiveStore` when it is `Empty` or `Undecided`. Every operation that adds a found address calls it **before** it changes the wallet in memory, and only then changes the wallet and saves.
- **Why the order and not "the same save"**: the state and the wallet record are separate stored values, so they cannot be written together. The existing search assigns the raised used-count in memory before it saves, and a history load in that gap would see found payments on a wallet still recorded as empty and queue the celebration (`FirstReceiveCoordinator` reads the stored state on every load and does not cache it, so the stored order is all that matters). Writing `History` first closes the gap. Nor is "before the search returns" enough: a search saves batch by batch, and a person can leave the Recovery page mid-search and load Home.
- **Why `Undecided` too**: a wallet that has not yet completed a load is `Undecided`. If the helper left it alone, a stale empty load arriving just afterwards could record it as `Empty`, and the next load would celebrate. A wallet for which a search has found payments has history, whatever it was recorded as. The same is done in today's `ScanForMissingReceiveFundsAsync` as part of Story 1, because the offer leads people into that search before the new one exists, and it has this fault today (analysis finding G4). `Empty` to `History` is a move the store already allows, and `History` never celebrates.
- **Side effect, wanted**: `History` is also what makes the offer disappear (FR-006).
- **Alternatives considered**: teaching `FirstReceiveRule` about transaction age (changes spec 025's deliberate "whatever its age"). Suppressing in the page (three pages show the celebration).

## R12. Tests

- `tests/DigiByte.Wallet.Tests` has most of what is needed: `ChangeAddressTests.Env` and `FakeChain`, with `Credit`, `HistoryDown`, `EveryAddressHasHistory`, `HistoryGate` (holds every lookup) and `HistoryLookups` (counts them).
- **Four additions are needed** (tasks.md T020), none changing existing behaviour: a settable lookup limit (`FakeChain` stops a test after 5,000 lookups, and a search of the first 5,000 makes about that many); a record of which addresses were looked up (several tests assert what was and was not asked, not how many); a way to hold lookups after the first N (to stop a search between batches); and a helper to seed the stored wallet record. The last matters most: setting the new fields on the unlocked wallet in memory does not survive a reload, which reads storage, so a test that did only that would fail or pass without proving anything.
- There is no web test project. Pages and components are checked in the browser with [quickstart.md](quickstart.md), as for specs 025 and 026.
- `LiveExplorerBenchmarks` (6 tests) fail on `main` because they call an explorer that is offline. They are unrelated and are excluded from this feature's test runs.

## R15. No way to undo a search

- **Decision** (clarified 2026-11-24): an entry marked paid is permanent. No reset action and no automatic re-check that could drop it.
- **Rationale**: the control address stops the realistic failure, a source that is wrong from the start. A source that struggles partway returns errors, which end the search as incomplete; it does not report unused addresses as used. An automatic re-check that drops entries would turn one bad answer from the explorer into a lost address, and a manual reset is a way for a person to stop seeing real funds. The recovery path that already exists, removing and restoring the wallet, rebuilds the checks from the chain.
- **Cost accepted**: after a search that wrongly added many addresses, every refresh reads them and the next new address comes after the highest, until the wallet is restored.
- **Alternatives considered**: a "forget addresses found by search" action limited to entries holding no funds (must never touch addresses the wallet handed out; needs its own confirmation and tests). Dropping entries the explorer later reports as unused (see above).

## Open points

1. **Gateway mode end to end**: confirm that an address added by a search is watched by the light-client gateway's ongoing sync, not only by its restore record. The mechanism is the one today's wider search relies on, so this is a check, not a design question.
2. **`PersistAddressIndexesAsync`** copies three named fields onto the stored record (`:1308`). The new fields must be added there, or a save from another path would drop them.
3. **A 5,000-address search inside the browser** has not been timed (R9).
