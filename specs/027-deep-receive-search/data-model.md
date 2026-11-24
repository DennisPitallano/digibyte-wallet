# Data Model: Find Payments Past the Normal Search

Nothing here is secret. Everything is stored on the device only; nothing new is sent anywhere.

## 1. Saved with the wallet (`WalletInfo`)

`WalletInfo` is the wallet's JSON record. Four fields are added. All are absent on existing wallets and default to "none", so an existing record reads as a wallet that has never used the wider search.

| Field | Type | Default | Meaning |
|---|---|---|---|
| `ReceiveBaseFloor` | int | 0 | A lower bound on the base run that only ever rises. Set when the address on show first leaves the base run, so the addresses the wallet handed out before that stay checked (FR-015c). |
| `WatchedReceiveIndexes` | list of `{ Index: int, Paid: bool }`, ascending by `Index`, no duplicate `Index` | empty | Every receive index beyond the base run that the wallet checks individually: found paid by a search or the automatic search (`Paid = true`), or handed out by this wallet (`Paid = false` until it is found paid). Checked on every refresh. |
| `ReceiveSearchedTo` | int | 0 | The largest count, from the first address, that any **finished** wider search has covered (FR-016). Only ever rises. Not changed by a cancelled or failed search. |
| `ReceiveSearchNoticeShown` | bool | false | The "this key seems to be used elsewhere" notice has been shown for this wallet (FR-015a). |

Existing fields this feature reads and writes: `NextReceivingIndex` (the address on show), `UsedReceivingCount` (one past the highest used index in the base run).

**Rules**

- An entry is added to `WatchedReceiveIndexes` in exactly two ways: a search or the automatic search finds an index beyond the base run paid (`Paid = true`); or `GenerateNewReceivingAddressAsync` hands out an index that is not contiguous with the base run (`Paid = false`).
- `Paid` only ever changes from false to true. There is no action, automatic or manual, that unmarks or removes a paid entry (clarified 2026-11-24): a search cannot be undone short of removing and restoring the wallet.
- An entry is removed in exactly one way: the base run grows to include its index. There is no other removal, and nothing "moves" between lists.
- `ReceiveBaseFloor` never decreases.
- Found funds are never celebrated as a first payment (FR-013a). The wallet's first-receive state (spec 025) and its record are two separate stored values, so they cannot be written in one operation; the order is fixed instead. Before an operation changes the wallet in memory to include a found address (a paid watched entry, or a raised used-count in today's search), it sets the first-receive state to `History` if it is `Empty` or `Undecided`. Only then does it change the wallet and save. A history load that runs in between therefore sees `History`, and never sees found payments on a wallet still recorded as empty.
- `PersistAddressIndexesAsync` must copy all four new fields, as it does the existing three, and normalizes the watched entries against the current `BaseCount` as it does so. Every save therefore drops entries the base run has grown over, whichever operation grew it.
- Results from lookups are applied to the wallet's watched list as it stands when they are applied, never to a copy taken before the lookups began, so an entry added meanwhile (an address handed out during the automatic search) is not lost.
- For the same reason `PersistAddressIndexesAsync` reads the list, normalizes it and assigns it to the stored record with no pause in between, after it has loaded the stored record.
- An operation whose save fails undoes exactly what it changed (removes the entries it added, puts back the floor and the address on show). It does not restore a copy of the list taken earlier, which would erase results applied meanwhile.
- Deleting a wallet deletes the record, so these go with it.

## 2. Derived, never saved: `ReceiveScanSet`

Computed from `NextReceivingIndex`, `UsedReceivingCount`, `ReceiveBaseFloor` and `WatchedReceiveIndexes`. Pure; the same inputs always give the same set.

The model has three parts that do not depend on each other:

1. **A base run** from the first address, whose length only ever grows.
2. **Watched indexes** beyond it, checked one by one, never dropped while they are beyond the base run.
3. **A tail** of 20 after the highest paid watched index, which follows the outside issuer and may move.

| Part | Definition |
|---|---|
| `ShownIsWatched` | `NextReceivingIndex` is the `Index` of an entry in the saved `WatchedReceiveIndexes`, tested against the list as saved, before the `>= BaseCount` filter below (so the definition is not circular) |
| `BaseCount` | `max(20, UsedReceivingCount, ReceiveBaseFloor, ShownIsWatched ? 0 : NextReceivingIndex + 1)` |
| Base run | indexes `0 .. BaseCount - 1` |
| Watched | the entries of `WatchedReceiveIndexes` whose `Index >= BaseCount`. Entries below `BaseCount` are ignored everywhere below. |
| `MaxPaid` | the highest `Index` among watched entries with `Paid = true`, or none |
| Tail | none when `MaxPaid` is none; otherwise `MaxPaid + 1 .. MaxPaid + 20` |
| `Indexes` | base run, watched and tail together, ascending, no duplicates |
| `Anchor` | `max(UsedReceivingCount, MaxPaid + 1)`; when `MaxPaid` is none, `UsedReceivingCount` |
| `NextNewIndex` | `max(NextReceivingIndex + 1, Anchor)` |
| `CanHandOutNew` | `NextNewIndex < Anchor + 20` |
| `HandOutIsWatched` | `NextNewIndex > BaseCount`: the new index is not contiguous with the base run |
| `ListedIndexes` | the base-run and watched indexes at or below `NextReceivingIndex`, ascending; no tail-only index. This is what "Your addresses" on Receive lists (FR-015b). |
| `Fingerprint` | a value that changes whenever `Indexes` changes, for cache keys |

There is one threshold, `BaseCount`, and it is computed first. `MaxPaid`, the tail and `Anchor` are computed from the watched entries at or above it.

**Moving the address on show.** `NextReceivingIndex` has exactly two writers today, and both MUST go through this one rule: `GenerateNewReceivingAddressAsync` (hands out `NextNewIndex`) and the Digi-ID sweep, `ExecuteDigiIdSweepAsync` (uses `NextNewIndex` and the indexes after it as destinations, then puts the address on show one past the highest destination). Given the first index being handed out, `first = NextNewIndex`, and the new address on show, `shown` (`first` for a new address; highest destination + 1 for a sweep), in one save:

In the sweep the two steps are in two methods: `PlanDigiIdSweepAsync` takes `first` from `NextNewIndex` and computes the room; `ExecuteDigiIdSweepAsync` sees only the planned destinations, so there `first` is the lowest destination index, `BaseCount` is taken at execute time, and the existing guard (do nothing unless the new address on show is beyond the current one) is kept.

- If `first > BaseCount` (`HandOutIsWatched`): set `ReceiveBaseFloor = BaseCount` (its value before this change), add every index from `first` to `shown` as `{ Paid: false }` watched entries, and set `NextReceivingIndex = shown`.
- Otherwise: set `NextReceivingIndex = shown` only, as today. The base run grows by the `NextReceivingIndex + 1` term, as today.
- If the save fails, the operation undoes its own three changes (see the rules in section 1); it does not restore an earlier copy of the list.
- The room for a sweep is `Anchor + 20 - 1 - first`, in place of today's `UsedReceivingCount + 20 - 1 - first`.

No other code may assign `NextReceivingIndex`. A writer that moved it past the base run without recording the entries would turn the base run into every address up to it and leave the hand-out limit permanently closed (review finding).

**Why the base run cannot shrink or explode**

- It cannot shrink: `UsedReceivingCount` and `ReceiveBaseFloor` only rise, and the `NextReceivingIndex + 1` term is replaced by the floor at the moment the address on show leaves the base run (analysis finding G1).
- It cannot jump: while the address on show is a watched entry, `NextReceivingIndex` does not feed `BaseCount`, and being found paid only flips `Paid` (analysis finding G2). The entry can stop being one only by being dropped, which happens only when the base run has grown over it, and the base run grows over a watched entry only through `UsedReceivingCount`. At that moment `UsedReceivingCount > NextReceivingIndex`, so the `NextReceivingIndex + 1` term adds at most what `UsedReceivingCount` already gave (worked example D).

**With no watched entries and a floor of 0** this is exactly today's behaviour: `Indexes` is `0 .. max(20, NextReceivingIndex + 1, UsedReceivingCount) - 1`; `NextNewIndex` is `max(NextReceivingIndex + 1, UsedReceivingCount)`, which is never above `BaseCount`, so `HandOutIsWatched` is always false; and the hand-out limit is `UsedReceivingCount + 20`.

**What looks up which addresses**

| | Looks up |
|---|---|
| Every refresh (balance, history) | all of `Indexes` |
| Automatic search, base walk (each unlock, as today) | from `UsedReceivingCount` until 20 in a row are unused and index `BaseCount + 20` has been reached. The minimum reach is `BaseCount + 20`, not "highest checked index + 20". |
| Automatic search, watched pass (each unlock, only when there are watched entries) | First every watched entry with `Paid = false`. Then, if `MaxPaid` exists, a walk onward from `MaxPaid + 1` until 20 in a row are unused, where `MaxPaid` is the value **held when the pass started**, not one raised by this pass. If `MaxPaid` is none there is no walk (every watched entry is then within 20 of `UsedReceivingCount`, which the base walk covers). All results are applied once, at the end: each index found used is added or set to `Paid = true`. The walk stops after 500 lookups like the base walk. Hitting that cap means the source cannot be trusted, so the **whole pass** is discarded: nothing is added and nothing is set to paid, because `Paid` cannot be undone and it moves the hand-out limit. The pass does not run at all when the base walk gave up at its own cap. |
| A wider search of the first N addresses | every index below N that is not in the base run and is not a watched entry with `Paid = true`. Tail indexes and unpaid watched entries **are** looked up (analysis finding G3). The list is fixed when the search starts. |

**Address formats**: indexes below 20 are checked in SegWit and Legacy form, as today. Every other index is SegWit only (FR-017).

**Worked examples**

A. Restored wallet, far-off find, then ordinary use.

| Step | Next | Used | Floor | Watched (p = paid) | `BaseCount` | Tail | Checked |
|---|---|---|---|---|---|---|---|
| Restored, nothing found | 0 | 0 | 0 | none | 20 | none | 20 |
| Search finds 3,000 and 3,001 | 0 | 0 | 0 | 3000p 3001p | 20 | 3002 to 3021 | 42 |
| Hand out a new address: 3,002 | 3002 | 0 | 20 | 3000p 3001p 3002 | 20 | 3002 to 3021 | 42 |
| Hand out another: 3,003 | 3003 | 0 | 20 | 3000p 3001p 3002 3003 | 20 | 3002 to 3021 | 42 |
| 3,003, the address on show, is paid (G2) | 3003 | 0 | 20 | 3000p 3001p 3002 3003p | 20 | 3004 to 3023 | 44 |
| Outside issuer is paid at 3,010 | 3003 | 0 | 20 | 3000p 3001p 3002 3003p 3010p | 20 | 3011 to 3030 | 45 |
| 3,002 is paid late | 3003 | 0 | 20 | 3000p 3001p 3002p 3003p 3010p | 20 | 3011 to 3030 | 45 |

`BaseCount` is 20 throughout, and 3,002 is checked from the moment it is handed out, whatever the tail does.

B. A wallet that had already handed out addresses past 19 (G1).

| Step | Next | Used | Floor | Watched | `BaseCount` | Checked |
|---|---|---|---|---|---|---|
| Before the feature | 34 | 15 | 0 | none | 35 | 35 |
| Search finds 3,000 | 34 | 15 | 0 | 3000p | 35 | 56 |
| Hand out a new address: 3,001 | 3001 | 15 | 35 | 3000p 3001 | 35 | 56 |

The floor captures 35 as the address on show leaves the base run, so 20 to 34 stay checked.

D. The base run grows over the address on show, and a find next to the base run.

| Step | Next | Used | Floor | Watched | `BaseCount` | Tail | Checked |
|---|---|---|---|---|---|---|---|
| Search finds 45; a new address, 46, is handed out | 46 | 0 | 20 | 45p 46 | 20 | 46 to 65 | 41 |
| Base walk finds 25, 44, 45 and 46 used | 46 | 47 | 20 | none (both dropped) | 47 | none | 47 |
| Search finds 20, exactly at the base count | 0 | 0 | 0 | 20p | 20 | 21 to 40 | 41 |
| A new address, 21, is handed out | 21 | 0 | 20 | 20p 21 | 20 | 21 to 40 | 41 |
| Base walk finds 20 used | 21 | 21 | 20 | 21 | 21 | none | 22 |

In row 2 the address on show stops being a watched entry, and `BaseCount` becomes 47 because `UsedReceivingCount` is 47, not because of the address on show. In row 5 the only watched entry left is unpaid, so there is no `MaxPaid` and no tail; the base walk covers 21 to 40.

E. A Digi-ID sweep after a far-off find.

| Step | Next | Used | Floor | Watched | `BaseCount` | Checked |
|---|---|---|---|---|---|---|
| Search has found 3,000 | 0 | 0 | 0 | 3000p | 20 | 41 |
| Sweep of 3 items: destinations 3,001 to 3,003, address on show 3,004 | 3004 | 0 | 20 | 3000p 3001 3002 3003 3004 | 20 | 41 |

Without the rule above the sweep would set the address on show to 3,004 with no entries, and `BaseCount` would become 3,005.

C. A payment sitting in the tail when a search runs (G3).

| Step | Watched | Tail | Note |
|---|---|---|---|
| Start | 3000p | 3001 to 3020 | an outside payment has landed on 3,010; the balance shows it because the tail is checked |
| Search of the first 5,000 | 3000p 3010p 3500p | 3501 to 3520 | the search looked up 3,001 to 3,020 itself, so 3,010 became a watched entry before the tail moved |

## 3. One search run: `ReceiveSearchResult`

Returned by the search; held in memory by the Recovery page; not saved.

| Field | Type | Meaning |
|---|---|---|
| `Outcome` | `Finished`, `Cancelled`, `Failed`, `Untrusted`, `NothingToCheck` | How the run ended |
| `RequestedUpTo` | int | The count asked for: the search covers indexes `0 .. RequestedUpTo - 1` |
| `ReachedUpTo` | int | A count: every index below this was checked or was already checked. Equals `RequestedUpTo` only for `Finished` and `NothingToCheck`. 0 for `Untrusted` and for a control-address failure. |
| `AddedIndexes` | list of int | Indexes this run added as paid watched entries, or set to paid |
| `AddedSatoshis` | long, or none | What those addresses hold; none if that read failed (the addresses are still added) |
| `LeavesUnwatched` | bool | Some unpaid indexes below `ReachedUpTo` are not in `Indexes` (FR-014c) |
| `SuggestSearchingFurther` | bool | `Finished`, `RequestedUpTo < 5000`, and a paid address was found at or above `RequestedUpTo - 20` (FR-012a) |
| `ShowForeignIssuerNotice` | bool | First time this wallet has had addresses added (FR-015a); the flag in section 1 is set in the same save |
| `AlreadyCheckedUpTo` | int | For `NothingToCheck`: `BaseCount`, so the message can say how far the wallet already looks |
| `Error` | text, or none | For `Failed`: what the data source said, for display |

**Outcome rules**

| Outcome | When | Saved |
|---|---|---|
| `NothingToCheck` | every index below `RequestedUpTo` is in the base run or is a paid watched entry | nothing |
| `Untrusted` | the control address was reported as used | nothing |
| `Failed` | the control lookup or any address lookup failed | paid indexes from complete batches |
| `Cancelled` | cancel requested, page left, or wallet locked | paid indexes from complete batches |
| `Finished` | every unchecked address below `RequestedUpTo` was looked up | paid indexes; `ReceiveSearchedTo = max(ReceiveSearchedTo, RequestedUpTo)` |

Progress during a run: `ReceiveSearchProgress(CheckedUpTo, RequestedUpTo, FoundSoFar)`, reported after each batch.

**Validation of the request** (before anything is looked up): `1 <= upTo <= 5000` (FR-011). Outside that, the search does not start and the caller is told the accepted range.

## 4. Per wallet, in `ISecureStorage`: origin and offer

Two small non-secret keys per wallet, both removed when the wallet is deleted.

| Key | Values | Written by |
|---|---|---|
| `walletOrigin:{walletId}` | `created`, `restored`, `watch-only`; absent = from before this feature | the Create, Recover and Watch-only import pages, right after the wallet exists |
| `searchOffer:{walletId}` | `dismissed`, `searched`; absent = not answered | `dismissed` by the offer when the person dismisses it; `searched` by the Recovery page when a wider search **finishes** (not when it starts, and not when it fails, is cancelled, is refused or has nothing to check) |

An unreadable or unknown value is treated as absent for origin and as `dismissed` for the offer, so a storage problem can hide the offer but never make it reappear.

## 5. The offer rule

Pure. Inputs: wallet type, origin, the wallet's `FirstReceiveState` (existing, spec 025), offer value.

Show the offer when **all** hold:

1. the wallet type has further receive addresses (`hd` or `xpub`) (FR-003);
2. origin is not `created` (FR-002, FR-002b);
3. `FirstReceiveState` is `Empty`: a complete load, taken after the address search settled, found nothing (FR-001, FR-002);
4. the offer value is absent (FR-005, FR-006).

`FirstReceiveState` leaves `Empty` as soon as the wallet has a transaction, which is what makes the offer disappear by itself (FR-006).

| Origin | Type | State | Offer value | Shown |
|---|---|---|---|---|
| `restored` | hd | Empty | absent | yes |
| `watch-only` | xpub | Empty | absent | yes |
| absent | hd | Empty | absent | yes (FR-002b) |
| absent | xpub | Empty | absent | yes |
| `created` | hd | Empty | absent | no |
| any | any | Undecided | any | no (no completed load yet) |
| any | any | History / Celebrated | any | no |
| any | any | Empty | `dismissed` / `searched` | no |
| any | single key | any | any | no |
