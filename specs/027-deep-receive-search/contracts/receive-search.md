# Contracts: Find Payments Past the Normal Search

The wallet exposes no new network interface and makes no new kind of request. These are the internal contracts between the wallet library, the web services and the pages, plus the UI contract of the new and changed components. Names are final; signatures show intent and may gain parameters during implementation.

## Wallet library (`DigiByte.Wallet`), unit-tested

### `ReceiveScanSet` (pure)

```text
record WatchedReceiveIndex(int Index, bool Paid)

static ReceiveScanSet From(int nextReceivingIndex, int usedReceivingCount, int baseFloor,
                           IReadOnlyList<WatchedReceiveIndex> watched)

IReadOnlyList<int> Indexes        // ascending, no duplicates
int  BaseCount
int? MaxPaid
int  Anchor
int  NextNewIndex
bool CanHandOutNew
bool HandOutIsWatched             // NextNewIndex > BaseCount
bool Contains(int index)
IReadOnlyList<int> UnpaidWatched  // for the automatic search's watched pass
IReadOnlyList<int> ListedIndexes  // base-run and watched indexes at or below the address on show
IReadOnlyList<int> SearchTargets(int upTo)   // indexes below upTo, not in the base run, not paid watched
string Fingerprint
```

- Definitions are in [data-model.md](../data-model.md), section 2. `BaseCount` is computed first; everything else uses only watched entries at or above it.
- With no watched entries and a floor of 0, `Indexes` equals `0 .. max(20, nextReceivingIndex + 1, usedReceivingCount) - 1` and `HandOutIsWatched` is false.
- Never throws for valid input; negative inputs are treated as 0.
- `SearchTargets` includes tail indexes and unpaid watched entries.
- `ShownIsWatched` is tested against the watched list as passed in, before entries below `BaseCount` are ignored.
- `NextReceivingIndex` is assigned only by `GenerateNewReceivingAddressAsync` and `ExecuteDigiIdSweepAsync`, both through the rule in data-model.md ("Moving the address on show").

```text
static List<WatchedReceiveIndex> Normalize(int baseCount, IEnumerable<WatchedReceiveIndex> watched)
static List<WatchedReceiveIndex> MarkPaid(int baseCount, IEnumerable<WatchedReceiveIndex> watched, IEnumerable<int> paidIndexes)
```

- `Normalize` sorts by `Index`, keeps one entry per `Index` (`Paid` wins), and drops entries below `baseCount`. It does nothing else: no entry is absorbed and `UsedReceivingCount` is not changed by it.
- `MarkPaid` adds each index at or above `baseCount` as paid, or sets an existing entry to paid, then normalizes.

### `WalletService`

```text
Task<ReceiveSearchResult> SearchReceiveAddressesAsync(
    int upTo,
    IProgress<ReceiveSearchProgress>? progress = null,
    CancellationToken cancellationToken = default)

const int MaxReceiveSearch = 5000
int DefaultReceiveSearchUpTo      // min(5000, max(BaseCount, MaxPaid + 21, ReceiveSearchedTo) + 500)
int ReceiveSearchedTo             // furthest finished search for the active wallet; never decreases
IReadOnlyList<int> GetListedReceiveIndexes()   // ReceiveScanSet.ListedIndexes, for the Receive page
```

- Requires an unlocked wallet with further receive addresses; otherwise returns `NothingToCheck`.
- `upTo` is a count: the search covers indexes `0 .. upTo - 1`. `upTo` outside `1 .. 5000` throws `ArgumentOutOfRangeException` before any lookup. The page validates first; this is the backstop.
- When there is something to check, the control address comes before any of the wallet's addresses (research R7). A wallet with no further receive addresses, and a count with nothing left to check, return `NothingToCheck` before it, with no lookup at all. Reported used: `Untrusted`, nothing saved. Lookup failed: `Failed`, `ReachedUpTo = 0`.
- Then `ReceiveScanSet.SearchTargets(upTo)`, taken once at the start: every index below `upTo` that is not in the base run and not already a paid watched entry, ascending, in batches of 20 (`ReceiveSearchBatchSize`). Tail indexes and unpaid watched entries are looked up, not skipped. After each batch: `MarkPaid` the used indexes, save if anything changed, report progress.
- Cancel is honoured between batches; with 20 lookups a batch, that is within SC-004's 5 seconds at the slowest rate measured in the wallet. A lookup failure ends the run as `Failed` with `ReachedUpTo` at the end of the last complete batch.
- Does not throw for cancel, lookup failure or an untrusted source: the outcome carries it. (`OperationCanceledException` is not thrown to the caller.)
- Ends as `Cancelled` if the wallet it started on is no longer the unlocked session it started with.
- One at a time per wallet: a second call while one runs returns that run's task.
- Never checks an index at or above `upTo`, whatever it finds (FR-012a).
- On `Finished`: `ReceiveSearchedTo = max(ReceiveSearchedTo, upTo)`. `SuggestSearchingFurther` is set when a paid index at or above `upTo - 20` was found and `upTo < 5000`.
- The first run that adds any index sets `ShowForeignIssuerNotice` and saves `ReceiveSearchNoticeShown`.
- Replaces `ScanForMissingReceiveFundsAsync` and `ReceiveRecoveryResult`, which are removed in the same change that moves the Recovery page and their tests over (tasks.md T038), so the build is never broken.
- Until User Story 3 is built, a lookup failure after the control address propagates as an exception; the Recovery page shows the `Failed` message for it.

Changed behaviour of existing members, all identical to today when the wallet has no watched entries:

| Member | Change |
|---|---|
| `GetAllAddresses()` | receive part iterates `ReceiveScanSet.Indexes` |
| `GetAddressKeyMap()` | same, so found funds can be signed for |
| `GenerateNewReceivingAddressAsync()` | next index and limit come from `NextNewIndex` / `CanHandOutNew`; when `HandOutIsWatched`, sets `ReceiveBaseFloor = BaseCount`, adds the index as an unpaid watched entry and sets `NextReceivingIndex`, all in one save |
| automatic address search, base walk | unchanged, with its minimum reach written as `BaseCount + ReceiveGapLimit` |
| automatic address search, watched pass | new; only when there are watched entries: looks up every unpaid watched index, then, if `MaxPaid` exists, walks on from the `MaxPaid + 1` held at the start of the pass until 20 in a row are unused; applies all results once at the end, to the current watched list, with `MarkPaid`; if the walk hits the 500-lookup cap the whole pass is discarded; does not run when the base walk gave up (research R4) |
| `PlanDigiIdSweepAsync` | `first` comes from `NextNewIndex`; room is `Anchor + 20 - 1 - first` |
| `ExecuteDigiIdSweepAsync` | moves the address on show through the same rule as a hand-out, with `first` the lowest planned destination and `BaseCount` taken at execute time: when `first > BaseCount`, sets `ReceiveBaseFloor` and adds every destination and the new address on show as unpaid watched entries, in the same save; the existing "only move forward" guard is kept |
| `PersistAddressIndexesAsync` | copies the four new fields and normalizes the watched entries against `BaseCount` |
| `MarkHasHistoryAsync(walletId)` | new; sets the wallet's first-receive state to `History` when it is `Empty` or `Undecided`; never throws |
| found addresses | `MarkHasHistoryAsync` is called before the wallet is changed in memory to include them, in the search (per batch), the watched pass and, until it is removed, today's `ScanForMissingReceiveFundsAsync` (FR-013a) |
| `DeleteWalletAsync(id)` | also removes `walletOrigin:{id}` and `searchOffer:{id}` |

### `SearchOfferStore`

```text
Task<WalletOrigin> GetOriginAsync(string walletId)        // Created, Restored, WatchOnly, Unknown
Task SetOriginAsync(string walletId, WalletOrigin origin) // Unknown is not written; an existing value is not overwritten
Task<SearchOfferAnswer> GetAnswerAsync(string walletId)   // None, Dismissed, Searched
Task SetAnswerAsync(string walletId, SearchOfferAnswer a) // None is not written; never goes back to None
static string OriginKey(string walletId)
static string AnswerKey(string walletId)
```

- Unreadable or unknown origin reads as `Unknown`; unreadable or unknown answer reads as `Dismissed`.

### `SearchOfferRule` (pure)

```text
static bool ShouldShow(string? walletType, WalletOrigin origin, FirstReceiveState state, SearchOfferAnswer answer)
```

- The table in [data-model.md](../data-model.md), section 5.

## Web services (`DigiByte.Web.Services`)

### `SearchOfferCoordinator` (scoped)

```text
Task<bool> ShouldShowAsync(string? walletId, string? walletType)
Task DismissAsync(string walletId)
Task MarkSearchedAsync(string walletId)
Task MarkOriginAsync(string walletId, WalletOrigin origin)   // never throws; a failed write only means "Unknown"
event Action? Changed
```

- Reads `FirstReceiveStore` and `SearchOfferStore`, applies `SearchOfferRule`.
- Never throws to a page: on any error `ShouldShowAsync` is false.

## Pages and components

### `SearchFurtherOffer.razor` (new)

- Parameters: `WalletId`, `WalletType`, and `LoadVersion`, an int the page raises each time it finishes a history load. The component asks again whenever any of them changes: the wallet being recorded as empty changes neither of the first two and raises no event, so without `LoadVersion` an offer first asked while the wallet was undecided would never appear.
- Rendered by Home only in its plain empty state: not while loading, not in the "still looking" state, not in the "unable to load" state.
- Shows a title, one or two sentences (FR-008), a **Search further** action and a dismiss control with an accessible name.
- **Search further** navigates to `/recovery?search=receive`. It does not record `searched`; the Recovery page does, when a search finishes (FR-006).
- Dismiss calls `DismissAsync` and hides the component at once.
- `data-testid="search-further-offer"`.

### `Home.razor`

- Renders `SearchFurtherOffer` above `ReceiveEmptyState` in the plain empty branch, passing a counter it raises after each completed history load, once the wallet's first-receive state has been stored. No other change.

### `Recovery.razor`, "Missing Payments" card

- `?search=receive` scrolls to the card and moves focus to the count field (to the start button until US2 adds the field).
- Reach field: numeric, prefilled with `DefaultReceiveSearchUpTo`, labelled "Search the first N addresses" (a count, FR-009). Anything but a whole number from 1 to 5,000 shows that range and disables start.
- A one-line note by the start button: the search checks up to N of this wallet's addresses with the public block explorer, with N following the field. Always shown; no confirmation step (FR-027a).
- Shows "Furthest search so far: the first N addresses" when `ReceiveSearchedTo > 0`.
- Every message reports extent as a count ("the first N addresses", "stopped after the first K"). No message shows an address index.
- Start calls `SearchReceiveAddressesAsync`. When the outcome is `Finished`, and only then, the page calls `MarkSearchedAsync` if the offer is unanswered (FR-006). With today's search, before User Story 2, "finished" means the search returned without throwing.
- While running: a progress line with how many addresses have been covered out of the count asked for, and how many found so far, and a **Cancel** button. Start is disabled.
- Result, by outcome:

| Outcome | Message says |
|---|---|
| `Finished`, added > 0 | "Searched the first N addresses. M added (X DGB)."; the brackets hold "amount not available yet" when the amount could not be read |
| `Finished`, added = 0 | no paid addresses found up to N |
| `NothingToCheck` | the wallet already checks its first K addresses (`AlreadyCheckedUpTo`), so there is nothing new within the first N |
| `Cancelled` | stopped after covering the first K addresses; the rest were not checked; plus, when anything was added, the same "Searched the first K addresses. M added (X DGB)." line |
| `Failed` | could not finish; covered the first K addresses; the rest were not checked; plus anything added; never "nothing found" |
| `Failed`, covered 0 | the search could not start; nothing was checked (FR-023) |
| `Untrusted` | the data source's answers can't be trusted; nothing was changed |

- When `LeavesUnwatched`: adds that a later payment to an address that was unpaid during this search will not appear until the search is run again (FR-014c).
- When `SuggestSearchingFurther`: adds that more payments may lie beyond N and a **Search further** action that fills the count field with `min(5000, N + 500)`. It does not start the search (FR-012a).
- When `ShowForeignIssuerNotice`: adds the one-time notice (FR-015a).
- Disposing the page cancels a running search.
- `data-testid`: `receive-search-upto`, `receive-search-explorer-note`, `receive-search-start`, `receive-search-cancel`, `receive-search-progress`, `receive-search-result`, `receive-search-further` (the action that fills in a larger count).

### `Receive.razor`, address list

- Lists `WalletService.GetListedReceiveIndexes()`, newest first: the base-run and watched indexes at or below the address on show (tail-only indexes are not listed), in place of every index from 0 to the address on show (FR-015b). With no watched entries the two are the same list, so nothing changes for a wallet that has not used the search.
- `ReceiveAddressList.WindowStart` and `NextWindow` take a position in that list, not a raw index; "Show more" still adds ten.

### `CreateWallet.razor`, `RecoverWallet.razor`, `ImportXpub.razor`

- After the wallet exists, call `MarkOriginAsync` with `Created`, `Restored` or `WatchOnly`. A failure is ignored.

## Translation keys (English and Spanish)

`home.search_offer.title`, `home.search_offer.body`, `home.search_offer.action`, `home.search_offer.dismiss`, and under `recovery.receive_search.`: `label`, `explorer_note`, `range_error`, `last_searched`, `start`, `cancel`, `progress`, `finished_found`, `finished_none`, `nothing_to_check`, `cancelled`, `failed`, `could_not_start`, `untrusted`, `amount_unavailable`, `leaves_unwatched`, `search_further_hint`, `search_further_action`, `foreign_issuer_notice`.
