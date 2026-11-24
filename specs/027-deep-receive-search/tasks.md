---

description: "Task list for Find Payments Past the Normal Search"
---

# Tasks: Find Payments Past the Normal Search

**Input**: Design documents from `/specs/027-deep-receive-search/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/receive-search.md](contracts/receive-search.md), [quickstart.md](quickstart.md)

**Tests**: Required. Constitution Principle III makes tests mandatory for every change to `DigiByte.Wallet/`: written first, failing before the code change, and covering negative paths. There is no web test project, so web-only changes are checked in the browser preview with quickstart.md.

**Organization**: Tasks are grouped by user story (US1 to US3 from spec.md) and listed in the order they are done. The file order is the execution order: no task needs anything a later task creates. This file was rewritten in one pass on 2026-11-24 after four analysis rounds, then corrected and renumbered after an independent walk of it against the code; task numbers do not match earlier drafts.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel with the tasks next to it (a different file, no dependency on an unfinished task)
- **[Story]**: The user story the task belongs to (US1 to US3)

## Path Conventions

- Wallet logic: `src/DigiByte.Wallet/Services/`, models in `src/DigiByte.Wallet/Models/`
- Web app: `src/DigiByte.Web/` (Pages, Components, Services, wwwroot, Program.cs)
- Tests: `tests/DigiByte.Wallet.Tests/` (harness: `ChangeAddressTests.Env` and `FakeChain`; `HistorySyncEnv` for tests that need the history sync)
- Run tests without the live benchmarks: `dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName!~LiveExplorerBenchmarks"`

## Rules that apply to every task

- **A test task is written before the task that makes it pass**, and must fail before that task. Where the member under test does not exist yet, the test project does not compile until the implementing task is done; that is expected, and is the only way the build may be red.
- **The build and the suite are green after every implementation task**, that is, after each pair of test task and the task that makes it pass. A task that removes something also moves or removes everything that uses it, in the same change.
- **Tests that need the new wallet fields seed the stored wallet record**, with the harness helper from T004, and then reload. Setting a field on the unlocked wallet in memory is not enough: a reload reads storage.
- **Tests that assert anything about the wallet's history turn on `FakeChain.DetailedHistory`** (T004). Without it this harness returns no usable transactions, and such an assertion proves nothing.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: The branch, a known-good baseline and the wording every later phase uses

- [X] T001 Commit the existing `specs/027-deep-receive-search/` directory to the branch `feature/deep-receive-search` (already created from `main`). Do not commit `.claude/settings.local.json`, `digipay-verify.db*` or other unrelated working-tree files. Done: commits 5ad435e and ef74e35.
- [X] T002 Run `dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName!~LiveExplorerBenchmarks"` and record the pass count as the baseline. It must be all-pass before any change.
- [X] T003 [P] Add the translation keys listed in contracts/receive-search.md ("Translation keys") to src/DigiByte.Web/wwwroot/locales/en.json and src/DigiByte.Web/wwwroot/locales/es.json: the four `home.search_offer.*` keys and the `recovery.receive_search.*` keys (`label`, `explorer_note`, `range_error`, `last_searched`, `start`, `cancel`, `progress`, `finished_found`, `finished_none`, `nothing_to_check`, `cancelled`, `failed`, `could_not_start`, `untrusted`, `amount_unavailable`, `leaves_unwatched`, `search_further_hint`, `search_further_action`, `foreign_issuer_notice`). Same keys in both files; Spanish translated, not copied. Wording rules: extent is always a count ("the first {0} addresses"), never an index (FR-009); `last_searched` says "furthest" ("Furthest search so far: the first {0} addresses", FR-016); `home.search_offer.body` must not state or imply that funds are missing (FR-008); `explorer_note` names the public block explorer and the count (FR-027a).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: What both US1 and US2 need: the test-harness additions their tests use, and the helper that records a wallet as having history when a search finds old payments, so they are never celebrated as a first payment (FR-013a).

- [X] T004 [P] Extend the test harness, changing no existing behaviour (every new hook is off or unused by default, and the whole suite must still pass). In tests/DigiByte.Wallet.Tests/ChangeAddressTests.cs, on `FakeChain`: make the history-lookup limit a settable `MaxHistoryLookups` (default 5,000, as now); add `HistoryAddresses`, the addresses passed to `HasAddressHistoryAsync` in order; add `HoldAfterLookups` (int?) with a `Release()` method, so `HasAddressHistoryAsync` calls after the first N wait until released (the existing `HistoryGate` holds all of them). N counts `HasAddressHistoryAsync` calls only, not transaction reads, and `HistoryDown` is checked after the hold, so a test can hold a search, switch the explorer off and release, and the held lookups then fail; add an opt-in `DetailedHistory` flag which, when true, makes `GetAddressTransactionsAsync` return one transaction per credit with a unique txid and one output carrying the credited address **and the credited amount** (the wallet counts a transaction as a receipt only when its amount is above zero). Today that method returns a single transaction with no inputs or outputs, which `LoadTransactionHistoryAsync` discards, so under this harness the history is always empty and any assertion about history or about `FirstReceiveRule.Evaluate` on it passes or fails vacuously; add an opt-in `BalanceDown` flag that makes the batch `GetBalanceAsync(IEnumerable<string>)` throw, since `HistoryDown` does not affect balance reads. On `InMemoryStorage`: add `HoldWalletInfoWrites` with a `ReleaseWalletInfoWrites()` method, so a wallet-record write can be held and then made to fail or succeed: `FailWalletInfoWrites` is read when the held write is released, not when it was started (the existing flag fails at once, leaving no moment in which a test can change something else). Add, in tests/DigiByte.Wallet.Tests/ChangeAddressTests.cs beside `InMemoryStorage`, a static helper `SeedWalletInfoAsync(storage, walletId, Action<WalletInfo> change)` usable from `Env`, `HistorySyncEnv` and `DigiIdEnv` (all three use `InMemoryStorage`), which reads the stored wallet record through `WalletKeyStore.GetWalletInfoAsync` (key `wallet_info_{id}`, plain JSON), applies the change and writes it back with `SaveWalletInfoAsync`. Move the throwing storage that tests/DigiByte.Wallet.Tests/FirstReceiveStoreTests.cs keeps private into a shared internal class, for tests of unreadable stored values.
- [X] T005 [P] Create tests/DigiByte.Wallet.Tests/FoundFundsHistoryTests.cs for a new `WalletService.MarkHasHistoryAsync(string walletId)`, using `FirstReceiveStore` over the test's storage: a wallet in state `Empty` becomes `History`; a wallet in state `Undecided` becomes `History` (so an empty load that lands afterwards cannot record it as empty; research R14); `History` and `Celebrated` are left as they are; a storage failure does not throw; and, to show the helper is what prevents the celebration, `FirstReceiveRule.Evaluate(FirstReceiveState.Empty, receipts, HistorySource.CompleteLoad)` returns a payment for a list holding a DGB receipt while `Evaluate(FirstReceiveState.History, receipts, HistorySource.CompleteLoad)` returns none. (Asserting only that `Evaluate` returns nothing for the stored state would prove nothing: it returns nothing for `History` whatever the list holds.)
- [X] T006 In src/DigiByte.Wallet/Services/WalletService.cs add `MarkHasHistoryAsync(string walletId)`: through `FirstReceiveStore`, set `History` when the state is `Empty` or `Undecided`; never throw (a failed write is logged and ignored). Makes T005 pass.

**Checkpoint**: Phases 1 and 2 done. US1 can start. US2 can start too, but needs five US1 tasks first (see "If US2 is built before US1" under Dependencies).

---

## Phase 3: User Story 1 - A restored wallet that looks empty offers to search further (Priority: P1) 🎯 MVP

**Goal**: Home's empty state shows a dismissible offer for restored and watch-only wallets whose completed load found nothing; it opens the wider search on the Recovery page.

**Independent Test**: Import as watch-only a key whose only paid addresses are the 61st and 62nd. Home shows the offer once loading has finished; following it opens Recovery at the Missing Payments card; running the search there (as it exists today) puts the funds in the balance, the offer is gone and no first-payment celebration appears. A newly created wallet never shows the offer.

### Today's search must not celebrate found funds

- [X] T007 [P] [US1] Add to tests/DigiByte.Wallet.Tests/ReceiveAddressTests.cs, for today's wider search (FR-013a), with `FakeChain.DetailedHistory` on so the wallet's history really contains the found payment (T004): a wallet whose first-receive state is `Empty`, with a payment beyond the automatic search's reach, has state `History` after `ScanForMissingReceiveFundsAsync` adds addresses; the transactions `LoadTransactionHistoryAsync` returns include the found payment as a DGB receipt, and `FirstReceiveRule.Evaluate(FirstReceiveState.Empty, thoseTransactions, HistorySource.CompleteLoad)` returns a payment, which is what the wallet would have celebrated had the state still been `Empty`; the same for a wallet that starts `Undecided`; a scan that adds nothing leaves `Empty` as `Empty`. This fails today: it is an existing fault that the offer would lead people into.
- [X] T008 [US1] In src/DigiByte.Wallet/Services/WalletService.cs `ScanForMissingReceiveFundsAsync`, when the scan found a higher used-count, call `MarkHasHistoryAsync` (T006) **before** assigning `info.UsedReceivingCount`, so no history load can see the found payments while the wallet is still recorded as empty (research R14). Makes T007 pass.

### The offer's rule and store (tests first; they do not compile until T012 and T013 exist)

- [X] T009 [P] [US1] Create tests/DigiByte.Wallet.Tests/SearchOfferRuleTests.cs covering every row of the table in data-model.md section 5 for `SearchOfferRule.ShouldShow(walletType, origin, state, answer)`: shown for (`Restored`, `hd`, `Empty`, `None`), (`WatchOnly`, `xpub`, `Empty`, `None`), (`Unknown`, `hd`, `Empty`, `None`), (`Unknown`, `xpub`, `Empty`, `None`); not shown for origin `Created`; for state `Undecided`, `History` or `Celebrated`; for answer `Dismissed` or `Searched`; for a wallet type other than `hd` / `xpub`; for a null wallet type.
- [X] T010 [P] [US1] Create tests/DigiByte.Wallet.Tests/SearchOfferStoreTests.cs using `ChangeAddressTests.InMemoryStorage`, and the shared throwing storage from T004 for the "unreadable" cases: origin round-trips for `Created`, `Restored`, `WatchOnly`; no value reads as `Unknown`; an unreadable or unknown origin value reads as `Unknown`; setting origin twice keeps the first value; `Unknown` is never written; answer round-trips for `Dismissed` and `Searched`; no value reads as `None`; an unreadable or unknown answer reads as `Dismissed`; an answer never goes back to `None`; `OriginKey` is `walletOrigin:{walletId}` and `AnswerKey` is `searchOffer:{walletId}`.
- [X] T011 [US1] Add a test to tests/DigiByte.Wallet.Tests/SearchOfferStoreTests.cs that `WalletService.DeleteWalletAsync(id)` removes both `walletOrigin:{id}` and `searchOffer:{id}` and leaves another wallet's keys in place.
- [X] T012 [P] [US1] Create src/DigiByte.Wallet/Services/SearchOfferRule.cs: `enum WalletOrigin { Unknown, Created, Restored, WatchOnly }`, `enum SearchOfferAnswer { None, Dismissed, Searched }`, and pure `static bool ShouldShow(string? walletType, WalletOrigin origin, FirstReceiveState state, SearchOfferAnswer answer)` per data-model.md section 5 (FR-001 to FR-003, FR-005, FR-006). Makes T009 pass.
- [X] T013 [US1] Create src/DigiByte.Wallet/Services/SearchOfferStore.cs over `ISecureStorage`, per contracts/receive-search.md: `GetOriginAsync`, `SetOriginAsync` (does not overwrite, does not write `Unknown`), `GetAnswerAsync`, `SetAnswerAsync` (never back to `None`), static `OriginKey` / `AnswerKey`. Reads never throw (FR-007). Makes T010 pass.
- [X] T014 [US1] In src/DigiByte.Wallet/Services/WalletService.cs `DeleteWalletAsync(string walletId)`, remove `SearchOfferStore.OriginKey(walletId)` and `SearchOfferStore.AnswerKey(walletId)` next to the existing removal of `FirstReceiveStore.StateKey(walletId)`. Makes T011 pass.

### The offer in the app

- [X] T015 [US1] Create src/DigiByte.Web/Services/SearchOfferCoordinator.cs (scoped) per contracts/receive-search.md: `ShouldShowAsync(walletId, walletType)` reads `FirstReceiveStore.GetAsync` and `SearchOfferStore`, applies `SearchOfferRule`, and returns false on any error; `DismissAsync`, `MarkSearchedAsync`, `MarkOriginAsync` (never throws); `event Action? Changed` raised after a dismiss or a mark. Register `SearchOfferStore` and `SearchOfferCoordinator` in src/DigiByte.Web/Program.cs beside the `FirstReceive*` registrations.
- [X] T016 [US1] Record origin after the wallet exists (FR-002a), ignoring failures, with `SearchOfferCoordinator.MarkOriginAsync`: `WalletOrigin.Created` in src/DigiByte.Web/Pages/CreateWallet.razor (beside the existing `FirstReceive.MarkCreatedEmptyAsync` call), `WalletOrigin.Restored` in src/DigiByte.Web/Pages/RecoverWallet.razor, `WalletOrigin.WatchOnly` in src/DigiByte.Web/Pages/ImportXpub.razor.
- [X] T017 [US1] Create src/DigiByte.Web/Components/Wallet/SearchFurtherOffer.razor per contracts/receive-search.md: parameters `WalletId`, `WalletType` and `LoadVersion` (an int the page raises each time it finishes a history load); renders nothing unless `ShouldShowAsync` is true; title, body, a **Search further** action that navigates to `/recovery?search=receive` (and records nothing), and a dismiss control with an accessible name that calls `DismissAsync` and hides at once; re-checks on `Changed` and whenever any parameter changes, `LoadVersion` included. Without that the offer can be asked once while the wallet is still undecided, hide, and never look again, because the wallet being recorded as empty changes none of its other parameters and raises no event; all text through `L.T` with the `home.search_offer.*` keys; `data-testid="search-further-offer"`.
- [X] T018 [US1] In src/DigiByte.Web/Pages/Home.razor, add a counter, `historyLoadVersion`, that is raised after `FirstReceive.ObserveAsync` returns in `LoadTransactionsAsync` (so the wallet's first-receive state is stored before the offer asks), and render `<SearchFurtherOffer WalletId="@ActiveWalletId" WalletType="@WalletService.ActiveWallet?.WalletType" LoadVersion="@historyLoadVersion" />` above `ReceiveEmptyState` in the plain empty branch only (the final `else` of the `transactions.Count == 0` block). Not in the `fetchFailed` branch and not in the `SearchingAddresses` branch, so it cannot appear before a load has completed (FR-002).
- [X] T019 [US1] In src/DigiByte.Web/Pages/Recovery.razor: read the `search` query parameter; when it is `receive` and the "Current Wallet — Missing Payments" card is shown, scroll to that card and move focus to its start button after first render (FR-004). When a search started there returns without throwing, call `SearchOfferCoordinator.MarkSearchedAsync(walletId)`; do not call it when the search fails (FR-006: the offer goes only when a search has finished). The search itself is unchanged in this story.
- [ ] T020 [US1] Check in the browser, quickstart.md "A. The offer" (A1 to A6): offer shown for a watch-only import with nothing in the automatic range, not for a created wallet, gone after dismiss and after lock/unlock, still there after visiting Recovery without starting and after a search that failed (start one offline), gone once a search has finished; after the search finds funds, no "Your first DGB" celebration appears on Home, Receive or Activity (FR-013a). Record what was and was not checked.

**Checkpoint**: US1 works on its own with today's search. A person with funds past the automatic range is led to them.

---

## Phase 4: User Story 2 - Choose how far the search looks (Priority: P2)

**Goal**: The person chooses how many addresses to search (1 to 5,000). Found funds join the balance and can be spent, without making every later refresh read every address in between. A source that cannot be trusted is refused.

**Independent Test**: With a wallet whose only paid address is the 3,001st, the default search finds nothing; searching the first 5,000 addresses finds it, the balance shows it, it can be sent, and a later refresh reads about 40 receive addresses, not 3,000. On a source that reports every address as used, the search refuses to run.

The control address is User Story 3's scenarios 5 and 5a in spec.md. It is built here because T038 removes today's only guard against such a source; the new search must never exist without it.

### The checked-address set

- [X] T021 [P] [US2] Create tests/DigiByte.Wallet.Tests/ReceiveScanSetTests.cs for `ReceiveScanSet.From(nextReceivingIndex, usedReceivingCount, baseFloor, watched)` per data-model.md section 2. With no watched entries and floor 0: `Indexes` is `0 .. max(20, next + 1, used) - 1`, `NextNewIndex` is `max(next + 1, used)`, `CanHandOutNew` is false exactly at `used + 20`, and `HandOutIsWatched` is false (three cases: all zero; `next = 25`; `used = 30`). Then **every row of worked examples A, B, C, D and E in data-model.md section 2**, asserting `BaseCount`, the tail and the number of indexes for each row. These are the sequences that broke earlier drafts or went untested, so none may be skipped: A row 5 (address on show 3,003 found paid: `BaseCount` stays 20); B row 3 (floor 35 keeps 20 to 34 after the address on show moves to 3,001); C (3,010 is a `SearchTargets` index while it is only in the tail); D rows 2 and 5 (base run grows over the address on show with no jump; a find at exactly `BaseCount`, then only an unpaid entry left, so no `MaxPaid` and no tail); E row 2 (sweep destinations and the new address on show as watched entries, `BaseCount` 20). Add a find at `BaseCount + 1` (example B with 36 paid). Also: `ShownIsWatched` is tested against the list as passed in, before entries below `BaseCount` are ignored; `BaseCount` never decreases across any A or B step; `HandOutIsWatched` is true exactly when `NextNewIndex > BaseCount`; `CanHandOutNew` is false once `NextNewIndex` reaches `Anchor + 20`; `SearchTargets(upTo)` excludes the base run and paid watched entries and includes tail indexes and unpaid watched entries; `UnpaidWatched` lists unpaid entries only; `ListedIndexes` is the base-run and watched indexes at or below the address on show, with no tail-only index; `Contains` agrees with `Indexes`; `Fingerprint` differs when `Indexes` differ and is equal when they are equal; negative inputs behave as 0; watched entries below `BaseCount` are ignored, including for `MaxPaid`; `Indexes` is ascending with no duplicates; nothing in `ReceiveScanSet` unmarks a paid entry (FR-014). For `Normalize(baseCount, watched)`: sorts, keeps one entry per index with `Paid` winning, drops entries below `baseCount`, and removes nothing else. For `MarkPaid`: adds a new paid entry, sets an existing unpaid entry to paid, ignores an index below `baseCount`.
- [X] T022 [US2] Create src/DigiByte.Wallet/Services/ReceiveScanSet.cs (with `record WatchedReceiveIndex(int Index, bool Paid)`) implementing T021's contract exactly as defined in data-model.md section 2 and contracts/receive-search.md. Compute `BaseCount` first and derive everything else from watched entries at or above it. Pure, no I/O. Makes T021 pass.
- [X] T023 [US2] Fields only, no behaviour: add to src/DigiByte.Wallet/Models/WalletInfo.cs `int ReceiveBaseFloor { get; set; }` (default 0; "only ever rises"), `List<WatchedReceiveIndex> WatchedReceiveIndexes { get; set; } = []` ("ascending by `Index`, no duplicate `Index`", default empty), `int ReceiveSearchedTo { get; set; }` (default 0; "only ever rises"), `bool ReceiveSearchNoticeShown { get; set; }` (default false). Do not touch `PersistAddressIndexesAsync` here: its tests come first (T024, T025) and it is changed in T027. The existing suite must still pass, which shows existing saved records load with these defaults.
- [X] T024 [US2] Create tests/DigiByte.Wallet.Tests/ReceiveSearchTests.cs with tests of the checked set through the service, seeding the stored record with `SeedWalletInfoAsync` (T004) and reloading, with `FakeChain.DetailedHistory` on for the history assertion: a payment credited to `env.ReceiveAddress(3000)` with watched `{3000 paid}` is in `GetBalanceAsync` and in the transactions returned by `LoadTransactionHistoryAsync` (FR-013); `SendAsync` can spend it (signing key found); after another reload with `HistoryDown = true` it is still counted; `GetAllAddresses()` holds 20 Legacy + 20 SegWit base entries, the watched entry and its 20-address tail in SegWit only, and nothing between index 20 and 3,000 (41 receive indexes, FR-014a, FR-017); an unpaid watched entry is in `GetAllAddresses()` and a payment to it is in the balance; with `ReceiveBaseFloor = 35` and the address on show a watched entry, indexes 20 to 34 are in `GetAllAddresses()`; a rename or colour change followed by a reload keeps (a guard: this may already pass once T023 adds the fields, because those saves round-trip the whole stored record; it is here so T027 cannot break it) `WatchedReceiveIndexes`, `ReceiveBaseFloor`, `ReceiveSearchedTo` and `ReceiveSearchNoticeShown` (they are saved by `PersistAddressIndexesAsync` and not dropped by other saves); with no watched entries and floor 0, `GetAllAddresses()` is identical to before.
- [X] T025 [US2] Add to tests/DigiByte.Wallet.Tests/ReceiveSearchTests.cs, seeding the stored record as in T024. **Hand-out** with watched `{3000 paid, 3001 paid}`: `GenerateNewReceivingAddressAsync` returns index 3,002, adds it as an unpaid watched entry, sets `ReceiveBaseFloor` to the base count it had, and refuses once 20 unpaid are out past the anchor (FR-015, FR-015c, FR-026); if that save fails, the address on show and the floor are what they were and 3,002 is not a watched entry; with the save held by `InMemoryStorage.HoldWalletInfoWrites` (T004), a watched entry the test adds directly to `ActiveWallet.WatchedReceiveIndexes` while the save is pending (nothing in production can add one yet) is still there after the save is made to fail (the rollback removes what the hand-out added; it does not restore a copy of the list); a wallet whose address on show was 34 with watched `{3000 paid}` still has a payment to index 30 in its balance after handing out 3,001. **Base walk reach** (a guard: the reach is already `BaseCount + 20` today; this keeps T027 and T035 from widening it) with watched `{3000 paid}`: during an unlock no receive index from `BaseCount + 20` up to 2,999 appears in `FakeChain.HistoryAddresses` (assert on which indexes were looked up, not on a total, so the watched pass added in T035 does not break this test). **Dropping covered entries**: with watched `{45 paid, 46}` and the address on show 46, a base walk that raises `UsedReceivingCount` to 47 leaves no watched entries in the stored record and `BaseCount` 47 (worked example D).
- [X] T026 [P] [US2] Add to tests/DigiByte.Wallet.Tests/DigiIdFundsRecoveryTests.cs, for the Digi-ID sweep after a far-off find (data-model.md worked example E). These go here, not in ReceiveSearchTests.cs, because a sweep can only be driven through `DigiIdEnv` and the funding helpers in this file; `Env` has no Digi-ID recovery configured. Seed the stored record with `SeedWalletInfoAsync` (T004). With watched `{3000 paid}`: a sweep of 3 items uses destinations 3,001 to 3,003 and leaves them and the new address on show 3,004 as unpaid watched entries with `ReceiveBaseFloor` 20; `GetAllAddresses()` does not grow by thousands; funds swept to 3,002 are in the balance; a new address can still be handed out afterwards; a failed save undoes the sweep's own changes. The room formula `Anchor + 20 - 1 - first`: with watched `{3000 paid}` and 15 unpaid addresses already handed out past it, the plan has room for 4 items, not `MaxItemsPerRound` (in example E itself the room equals `MaxItemsPerRound`, so it does not show the formula). With no watched entries the sweep behaves exactly as today.
- [X] T027 [US2] In src/DigiByte.Wallet/Services/WalletService.cs: make `GetAllAddresses()` and `GetAddressKeyMap()` iterate `ReceiveScanSet.Indexes` for the receive part (Legacy copies only for indexes below `MinScannedReceive`, as today) and add the set's `Fingerprint` to both cache keys. In `PersistAddressIndexesAsync`, copy the four new fields onto the stored record with the existing three, normalizing the watched list against the current `BaseCount` as it is assigned; read, normalize and assign after the `await GetWalletAsync` with no `await` in between, so a result applied meanwhile is not lost. In `GenerateNewReceivingAddressAsync`, replace `NextNewReceivingIndex` and the limit check with `NextNewIndex` / `CanHandOutNew`; when `HandOutIsWatched`, set `ReceiveBaseFloor = BaseCount`, add the index as an unpaid watched entry and set `NextReceivingIndex`, in one save; on a failed save undo exactly those changes (remove the entry it added, put back the floor and the address on show) and do not restore a saved copy of the list (FR-015c, research R13). Write the base walk's minimum reach in `FindUsedReceiveAsync` as `BaseCount + ReceiveGapLimit` (research R4). Digi-ID sweep, two methods: in `PlanDigiIdSweepAsync`, take `first` from `NextNewIndex` and compute room as `Anchor + 20 - 1 - first`; in `ExecuteDigiIdSweepAsync`, with `first = plan.Items.Min(i => i.DestinationIndex)` and `BaseCount` taken at execute time, keep the existing `wanted > previous` guard and replace the direct assignment of `NextReceivingIndex` with the shared rule: when `first > BaseCount`, set `ReceiveBaseFloor`, add every destination and the new address on show as unpaid watched entries and set `NextReceivingIndex`, in one save, undoing its own changes if the save fails (data-model.md "Moving the address on show"; research R3). After this task no other code assigns `NextReceivingIndex`. Re-read the two messages that say a restore "only looks 20 addresses past the last one used" (the hand-out refusal and the sweep refusal) and reword them if they are no longer accurate when the anchor is a far-off find. Makes T024, T025 and T026 pass; the whole existing suite must still pass unchanged (FR-025, FR-026, SC-007).

### The search

- [X] T028 [US2] Types only: create src/DigiByte.Wallet/Models/ReceiveSearchResult.cs per data-model.md section 3: `enum ReceiveSearchOutcome { Finished, Cancelled, Failed, Untrusted, NothingToCheck }`, `record ReceiveSearchResult(Outcome, RequestedUpTo, ReachedUpTo, AddedIndexes, AddedSatoshis (long?), LeavesUnwatched, SuggestSearchingFurther, ShowForeignIssuerNotice, AlreadyCheckedUpTo, Error)`, `record ReceiveSearchProgress(CheckedUpTo, RequestedUpTo, FoundSoFar)`. Leave `ReceiveRecoveryResult.cs` and `ScanForMissingReceiveFundsAsync` in place: they are still used until T038.
- [X] T029 [US2] Add to tests/DigiByte.Wallet.Tests/ReceiveSearchTests.cs, for `SearchReceiveAddressesAsync(upTo)` (raise `FakeChain.MaxHistoryLookups` in the tests that search the first 5,000): a payment at index 3,000 with `upTo = 5000` gives `Finished`, `AddedIndexes = [3000]`, the right `AddedSatoshis`, `ReachedUpTo = 5000`, `ReceiveSearchedTo = 5000`, and the lookups made during the search are exactly one control address followed by `SearchTargets(5000)` (read from `HistoryAddresses`); the same payment with the default `upTo` gives `Finished` with nothing added; the found funds can be sent and survive a reload; nothing to find gives `Finished`, empty, and saves `ReceiveSearchedTo`; `upTo` inside the base run gives `NothingToCheck` with no address lookups, `AlreadyCheckedUpTo` equal to `BaseCount`, and `ReceiveSearchedTo` unchanged; `upTo` of 0, -1 and 5,001 throws `ArgumentOutOfRangeException` before any lookup (FR-011: "a whole number from 1 to 5,000"); a second search to the same `upTo` after a late payment to a previously unpaid index finds it (FR-012); with watched `{3000 paid}` and a payment already credited to index 3,010 (in the tail), a search that also finds 3,500 leaves 3,010 as a paid watched entry and the balance still includes it (FR-012); an unpaid watched entry that has since been paid is marked paid by a search covering it; a refresh after the find reads the scan set's addresses and not every index below 3,000 (FR-014a, SC-007a); `LeavesUnwatched` is true when unpaid indexes below `ReachedUpTo` are outside the checked set and false otherwise (FR-014c). The default (FR-010): on a wallet with watched `{3000 paid}` seeded and no finished search, `DefaultReceiveSearchUpTo` is 3,521; on a separate wallet with no watched entries, after a finished search of the first 2,000 with nothing found it is 2,500; after a finished search of the first 5,000 it is 5,000. `ReceiveSearchedTo` is the furthest finished search: the first 5,000 followed by the first 600 leaves it at 5,000. FR-027 and research R6: using `HistorySyncEnv`, a search makes no address-summary requests (`SummaryExplorer.SummaryAddresses` stays empty) and adds nothing to the history sync's snapshot for the addresses it looked up. Guards and edges: a single-key wallet (no further receive addresses) gives `NothingToCheck` with no lookups; a watch-only wallet can run a search and gets the same results; when the balance read for the added addresses fails, the outcome is still `Finished`, the addresses are still added, and `AddedSatoshis` is none.
- [X] T030 [US2] Add to tests/DigiByte.Wallet.Tests/ReceiveSearchTests.cs: `upTo = 500` with a payment at 495 gives `SuggestSearchingFurther` true and no index at or above 500 in `HistoryAddresses`; last payment at 300 gives false; `upTo = 5000` with a payment at 4,995 gives false (FR-012a); the first search that marks an index paid sets `ShowForeignIssuerNotice` and saves `ReceiveSearchNoticeShown`; a second search that adds more does not set it again (FR-015a); after a search finds 3,001, `GenerateNewReceivingAddressAsync` returns 3,002.
- [X] T031 [US2] Add to tests/DigiByte.Wallet.Tests/ReceiveSearchTests.cs, for the control address (FR-023): with `FakeChain.EveryAddressHasHistory = true` the outcome is `Untrusted`, exactly one lookup was made during the search, `WatchedReceiveIndexes` and `ReceiveSearchedTo` are unchanged; with `HistoryDown = true` from the start the outcome is `Failed` with `ReachedUpTo = 0` and nothing saved; indexes 100 to 104 all paid with `upTo = 105` adds all five (no "everything is paid" rejection); the control address (the first entry of `HistoryAddresses` for each search) is different on each search and is never one of the wallet's receive or change addresses.
- [X] T032 [US2] Add to tests/DigiByte.Wallet.Tests/ReceiveSearchTests.cs, for FR-013a with the new search: a wallet whose first-receive state is `Empty` has state `History` after a search marks an index paid; with `FakeChain.DetailedHistory` on, the wallet's history includes the found payment as a DGB receipt and `FirstReceiveRule.Evaluate(FirstReceiveState.Empty, thoseTransactions, HistorySource.CompleteLoad)` returns a payment (what would have been celebrated); the same for `Undecided`; `History` and `Celebrated` are left as they are; a search that finds nothing leaves `Empty` as `Empty`; with `FakeChain.HoldAfterLookups` set so the search stops after its first batch and a payment found in that batch, the state is already `History` while the search is still running, and that payment's index is already a paid watched entry in the **stored** wallet record (found addresses are saved batch by batch, not when the search ends); handing out an unpaid watched index does not change the state.
- [X] T033 [US2] In src/DigiByte.Wallet/Services/WalletService.cs add `const int MaxReceiveSearch = 5000`, `const int ReceiveSearchBatchSize = 20`, `DefaultReceiveSearchUpTo` (`min(5000, max(BaseCount, MaxPaid + 21, ReceiveSearchedTo) + 500)`, FR-010), `ReceiveSearchedTo`, and `SearchReceiveAddressesAsync(int upTo, IProgress<ReceiveSearchProgress>? progress = null, CancellationToken cancellationToken = default)`. In order: for a wallet with no further receive addresses (no HD key) return `NothingToCheck` at once; validate `upTo`; take `ReceiveScanSet.SearchTargets(upTo)` once (not in the base run, not paid watched; tail and unpaid watched entries included) and return `NothingToCheck` with `AlreadyCheckedUpTo` if it is empty; **control address**: derive a SegWit address on the wallet's network from a freshly generated key, discard the key, look it up; used gives `Untrusted` with nothing checked or saved; a failed lookup gives `Failed` with `ReachedUpTo = 0` (research R7); then look the targets up ascending in batches of `ReceiveSearchBatchSize` with 10 in flight, calling `_blockchain.HasAddressHistoryAsync` directly and not `IsAddressUsedAsync` (research R6, SC-004); for each batch that found used indexes, call `MarkHasHistoryAsync` (T006) **before** changing the wallet's watched list in memory (research R14), then `MarkPaid` them on the wallet's current list and save; on finish set `ReceiveSearchedTo` to the larger of its value and `upTo`, read `AddedSatoshis` with `_blockchain.GetBalanceAsync` (none if that read fails), and fill `LeavesUnwatched`, `SuggestSearchingFurther` and `ShowForeignIssuerNotice`. A lookup failure after the control address is left to propagate as an exception in this story (US3 turns it into an outcome). Do not remove `ScanForMissingReceiveFundsAsync` here. Makes T029 to T032 pass.

### The automatic search keeps up

- [X] T034 [US2] Add to tests/DigiByte.Wallet.Tests/ReceiveSearchTests.cs, for the automatic search's watched pass (FR-014b, research R4), seeding the stored record: with watched `{3000 paid}`, a payment credited to index 3,005 is in the balance after a reload with no manual search and 3,005 is now a paid watched entry; the walk past `MaxPaid` stops after 20 unused in a row; an unpaid watched entry below the tail that has been paid is marked paid at the next unlock, and the hand-out limit moves with it; the address on show, a watched entry, is paid and found at unlock: `BaseCount` is unchanged and a refresh reads no more addresses than base + watched + tail (FR-014a); after handing out 3,002 and then a find at 3,010, a payment credited to 3,002 is still in the balance (FR-015c); with watched `{3000 paid, 3015 unpaid}`, an outside payment on 3,005 (not a watched entry, visible only through the tail) and 3,015 newly paid, one unlock leaves both 3,005 and 3,015 as paid watched entries (the walk starts from the `MaxPaid` held when the pass began; this setup fails if it starts from the raised one); a wallet whose watched entries are all unpaid makes no walk; when the pass's walk hits its 500-lookup cap, nothing from the pass is applied: no entry is added and none is set to paid, including unpaid watched entries that were reported paid; when the base walk gave up at its own cap, the watched pass does not run; an address handed out while the pass was held (`HoldAfterLookups`) is still a watched entry afterwards; a wallet in state `Empty` whose first paid entry comes from the watched pass has state `History` afterwards (FR-013a); a wallet with no watched entries makes exactly the same lookups on unlock as before this feature (SC-007). Harness note: `Env` sets `AddressDiscoveryWait` and `AddressDiscoverySendWait` to one minute, so the test that hands out an address while the pass is held must lower them (or drive the hand-out from a second task), or it will wait a minute for the held discovery.
- [X] T035 [US2] In src/DigiByte.Wallet/Services/WalletService.cs `FindUsedReceiveAsync`, after the existing base walk has finished and saved, add the watched pass for wallets that have watched entries: build the scan set from the wallet's state at that point; look up every index in `UnpaidWatched` (this needs a small new helper that returns which of a list of indexes are used: `FindUsedCountAsync` returns only a count; look them up through `IsAddressUsedAsync`, as the base walk does, since these addresses are ones the wallet watches); then, only if `MaxPaid` exists, walk on from the `MaxPaid + 1` held when the pass started (not one raised by this pass) until 20 in a row are unused, capped at `MaxAddressSearch` lookups. If the walk hits the cap, discard the whole pass and change nothing. Otherwise, if anything was found used: call `MarkHasHistoryAsync` first, then apply all results once with `MarkPaid` to the wallet's current watched list (not a copy taken before the lookups), and save. Do not run the pass when the base walk returned null (gave up at its cap) or when there are no watched entries. Makes T034 pass.

### The address list on Receive

- [X] T036 [US2] Add guard tests to the existing tests/DigiByte.Wallet.Tests/ReceiveAddressListTests.cs for paging by position over a list of indexes that is not an unbroken run (FR-015b): for a list of 23 entries (`0 .. 19, 3000, 3001, 3002`), `WindowStart(count - 1, shown)` and `NextWindow(shown, count)` page through it ten at a time. These pass today, because both methods already work on positions; they are there so T037 cannot break the paging it relies on. The failing test for this pair of tasks is the next one. Add to tests/DigiByte.Wallet.Tests/ReceiveSearchTests.cs, after the other tests there are written: `WalletService.GetListedReceiveIndexes()` returns `0 .. NextReceivingIndex` for a wallet with no watched entries, and for watched `{3000 paid, 3001 paid, 3002}` with the address on show at 3,002 returns the base run, 3,000, 3,001 and 3,002, with nothing in between and no tail-only index.
- [X] T037 [US2] In src/DigiByte.Wallet/Services/WalletService.cs add `IReadOnlyList<int> GetListedReceiveIndexes()` returning `ReceiveScanSet.ListedIndexes` for the active wallet. `ReceiveAddressList.WindowStart` / `NextWindow` are unchanged: they already work on positions. In src/DigiByte.Web/Pages/Receive.razor, build "Your addresses" from `GetListedReceiveIndexes()` in place of `0 .. NextReceivingIndex`: page by position in that list, and derive the address for each listed index individually. Today the page derives a contiguous range with `GetReceivingAddressesAsync(missing.Count, missing[0])`, which would show the wrong addresses for a list with gaps. Makes T036 pass; the existing `ReceiveAddressList` tests must still pass.

### Switching the Recovery page to the new search

- [X] T038 [US2] One change, so the build is never broken (analysis finding J3). (a) In src/DigiByte.Web/Pages/Recovery.razor, replace the card's call with `SearchReceiveAddressesAsync` and add: a numeric count field labelled with `recovery.receive_search.label` ("Search the first N addresses"), prefilled with `DefaultReceiveSearchUpTo`; start disabled and `range_error` shown unless the value is a whole number from 1 to 5,000 (FR-009, FR-011); the `explorer_note` line by the start button with the count following the field (FR-027a); `last_searched` when `ReceiveSearchedTo > 0` (FR-016); a result message for **every outcome this story can return**: `Finished` (found / none), `NothingToCheck` (with `AlreadyCheckedUpTo`), `Untrusted`, and `Failed`, which is also shown when the search throws. A `Failed` outcome that covered nothing (`ReachedUpTo` 0: the control lookup failed) uses `could_not_start` ("The search could not start. Nothing was checked."), not "covered the first 0 addresses" (FR-023); none of them says "nothing found" unless the search finished; plus `leaves_unwatched`, the `search_further_hint` with an action that fills the field with `min(5000, N + 500)` and does not start (FR-012a), and `foreign_issuer_notice`; `?search=receive` now focuses the count field; `MarkSearchedAsync` is called when the outcome is `Finished` and for no other outcome (FR-006); all text through `L.T`; `data-testid` values from contracts/receive-search.md; extent always shown as a count. (b) In tests/DigiByte.Wallet.Tests/ReceiveAddressTests.cs, move the three `DeepScan_*` tests that still apply to `SearchReceiveAddressesAsync` (finds funds beyond the automatic search and they can be spent after reload; reports nothing when there is nothing; an explorer that is down does not come back as `Finished`), delete `DeepScan_WhenEveryAddressShowsHistory_FailsInsteadOfTrustingIt` (T031 replaces it) and delete the T007 tests of `ScanForMissingReceiveFundsAsync` if present (T032 replaces them). (c) In src/DigiByte.Wallet: remove `ScanForMissingReceiveFundsAsync` and `MaxDeepScanSearch` from `WalletService.cs` and delete src/DigiByte.Wallet/Models/ReceiveRecoveryResult.cs. Keep `DeepScanGapLimit`: `RestoreViaGatewayAsync` still uses it; fix its doc comment, which refers to the removed method. Nothing may be left referring to the removed method or record.
- [ ] T039 [US2] Check in the browser, quickstart.md "B. The search" (B1 to B10) and "D. Receive" (D1): the note follows the count; a default search; a search of the first 5,000 that finds funds, with the amount, the one-time notice and the unwatched-addresses note; balance and history on Home with no first-payment celebration; a send of found funds from a seed wallet (a watch-only wallet cannot send; record which was used); lock/unlock speed unchanged; a second search shows no notice; invalid counts refused; on testnet the search refuses with the "can't be trusted" message and Home is unchanged; a new address is one past the highest found and the Receive list is short. Record what was and was not checked.

**Checkpoint**: US2 works on its own. The old fixed-reach scan is gone, and a source that cannot be trusted is refused. A long search cannot yet be cancelled.

---

## Phase 5: User Story 3 - Watch, stop and trust a long search (Priority: P3)

**Goal**: A long search shows how far it has got, can be stopped, keeps what it found, and never reports "nothing found" when it did not finish.

**Independent Test**: Start a search of the first 5,000 addresses and cancel partway: it stops within 5 seconds and says after how many addresses. Repeat with the connection cut: it says it could not finish and how many it covered. Leave the page mid-search: the search ends.

### Tests for User Story 3 (write first; they must fail)

- [X] T040 [US3] Add to tests/DigiByte.Wallet.Tests/ReceiveSearchTests.cs, using `FakeChain.HoldAfterLookups` and a `CancellationTokenSource`: cancel during the second batch with a payment in the first gives `Cancelled`, that index kept and in the balance, `ReachedUpTo` at the end of the last complete batch, and no further batch started (FR-019, FR-021, FR-022); a cancelled search does not change `ReceiveSearchedTo`; the method does not throw `OperationCanceledException`; progress is reported once per batch with a rising `CheckedUpTo` and the running `FoundSoFar` (FR-018).
- [X] T041 [US3] Add to tests/DigiByte.Wallet.Tests/ReceiveSearchTests.cs: lookups start failing in the third batch with a payment in the first gives `Failed`, that index kept, `ReachedUpTo` at the end of the second batch, `Error` set, `ReceiveSearchedTo` unchanged, and no exception to the caller (FR-021, FR-022, SC-005); locking the wallet during a held search gives `Cancelled` and leaves nothing half-saved; a second call while one is running returns the same run and makes no extra lookups (FR-024).

### Implementation for User Story 3

- [X] T042 [US3] In src/DigiByte.Wallet/Services/WalletService.cs `SearchReceiveAddressesAsync`: check the token between batches and return `Cancelled`; catch a lookup failure, return `Failed` with `ReachedUpTo` at the end of the last complete batch and `Error` set; before each batch, end as `Cancelled` if the session it started with is no longer the unlocked session; keep one running task per wallet and return it to a second caller; report `ReceiveSearchProgress` after each batch. Never let `OperationCanceledException` reach the caller. Makes T040 and T041 pass.
- [X] T043 [US3] In src/DigiByte.Web/Pages/Recovery.razor: show a progress line (`recovery.receive_search.progress`: addresses covered so far out of the count, and how many found) while a search runs; a **Cancel** button; start disabled while running; a result message for `Cancelled`, and extend the `Failed` message, each stating how many addresses were covered and that the rest were not checked, never "nothing found"; anything added is still listed; implement `IDisposable` / `IAsyncDisposable` to cancel a running search when the page is left (FR-024).
- [ ] T044 [US3] Check in the browser, quickstart.md "C. Stopping and failing" (C1 to C4): progress moves while a search runs; time a cancel with the pane visible and record the figure, which must be under 5 seconds (SC-004); it reports the count covered; going offline partway reports "could not finish" with the count; leaving the page ends the search. Record what was and was not checked.

**Checkpoint**: All three stories work.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T045 Run the whole wallet suite, `dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName!~LiveExplorerBenchmarks"`, and `dotnet build src/DigiByte.Web`. Everything from the T002 baseline must still pass.
- [X] T046 Time a search of the first 5,000 addresses on mainnet in the browser with the pane visible (quickstart.md E1). Target: under 10 minutes (SC-003). Record the figure; if it misses, record that and do not change the target silently.
- [X] T047 With the light-client gateway as the first source, do quickstart.md E2 and confirm found funds still show after the next gateway sync (research, open point 1). Record the result, including "not possible to test here" if no gateway is available.
- [ ] T048 [P] Check the offer and the search card in Spanish, in light and dark mode, and at phone width (quickstart.md E3 and E4). Have the Spanish strings reviewed by a Spanish speaker, or record that they were not.
- [X] T049 [P] Update docs per the constitution's documentation rule: describe the wider search and the offer in src/DigiByte.Web/Pages/Help.razor (the existing recovery/scan FAQ entries) and add the flow to docs/PROCESS_FLOWS.md, including the note that the search asks the public block explorer whatever the wallet's usual source, and that found addresses are permanent: a search cannot be undone except by removing the wallet from the device and restoring it (FR-014).
- [X] T050 [P] Add an entry to CHANGELOG.md under the next unreleased version: the offer, the chosen count, cancel and progress, the fix to the first-payment celebration after a search, and the change to what "Your addresses" lists.
- [X] T051 Reflect any scope change made during implementation back into spec.md, plan.md and this file (constitution, Spec-Kit workflow rule).

---

## Dependencies & Execution Order

The file order is the execution order. Within it:

- **Phase 1**, then **Phase 2** (T004, T005, T006). T003 can run beside T002.
- **US1 (Phase 3)** needs Phases 1 and 2.
- **US2 (Phase 4)** needs Phases 1 and 2, and from US1 the offer's rule, store and coordinator (T009, T010, T012, T013, T015): T038 records the offer as answered through `SearchOfferCoordinator`.
- **US3 (Phase 5)** needs US2.
- **Phase 6** after the stories being shipped.

### Where US1 and US2 meet

Three places, all handled by T038:

1. **The Missing Payments card** in src/DigiByte.Web/Pages/Recovery.razor: T019 (US1) adds the `?search=receive` landing and `MarkSearchedAsync`; T038 (US2) replaces the card's body, moves the focus to the count field and calls `MarkSearchedAsync` when the new search's outcome is `Finished`.
2. **`ScanForMissingReceiveFundsAsync`**: T008 (US1) adds the history call to it; T038 removes the method (T033 already does the same thing in the new search).
3. **tests/DigiByte.Wallet.Tests/ReceiveAddressTests.cs**: T007 (US1) adds tests of that method; T038 deletes them (T032 covers the new search).

### If US2 is built before US1

- Do T009, T010, T012, T013 and T015 first (the offer's rule, store and coordinator, with their tests). T038 calls `SearchOfferCoordinator.MarkSearchedAsync`, and the web project does not build without it.
- Skip T007 and T008: the method they concern no longer exists, and T032 and T033 cover the same requirement.
- T019 focuses the count field; T038 has already wired `MarkSearchedAsync` to the `Finished` outcome.
- T038 finds no T007 tests to delete.

### Parallel Opportunities

- T003 beside T002. T004 beside T005.
- US1: T009 and T010 together (two new files). T012 before T013, which uses its enums. T016 after T015, which it calls.
- US2: T021 beside the end of Phase 2; T026 (another test file) beside T025; T036's first part beside anything. The tasks that add to `ReceiveSearchTests.cs` (T024, T025, T029 to T032, T034, and T036's second part) share one file and are written one after another.
- US3: T040 and T041 share that file too.
- Polish: T048, T049 and T050 together.
- US1 and US2 can be built by two people at once, apart from the three places above.

## Parallel Example: User Story 1

```text
First:     T007 ReceiveAddressTests.cs    then T008 (makes it pass)
Together:  T009 SearchOfferRuleTests.cs   T010 SearchOfferStoreTests.cs
Then:      T012 SearchOfferRule.cs        then T013 SearchOfferStore.cs
Then:      T015 coordinator               then T016 origin on three pages, T017 SearchFurtherOffer.razor
```

## Implementation Strategy

### MVP first (US1 only)

1. Phase 1.
2. Phase 2.
3. Phase 3 (US1).
4. Stop and check: quickstart.md section A. Ship if wanted. People with funds past the automatic range are now led to the existing search, which reaches 500 unpaid addresses past the last paid one, and its results no longer trigger the first-payment celebration.

### Then

5. Phase 4 (US2): the checked-address set, the chosen count and the control address. This is the largest phase and the one that changes how balance, history and signing pick their addresses; T027's regression run is the gate.
6. Phase 5 (US3): progress, cancel and honest outcomes for a search that does not finish.
7. Phase 6.

### Note on shipping US2 without US3

After US2 alone, a search of several thousand addresses cannot be cancelled, and a lookup failure partway shows the "could not finish" message without saying how far it got. A source that reports every address as used is already refused (T031, T033). The count field invites longer searches than can be stopped, so prefer shipping US2 and US3 together.

## Requirement Coverage

| Requirements | Tasks |
|---|---|
| FR-001, FR-002, FR-002b, FR-003 | T009, T012, T015, T018 |
| FR-002a | T010, T013, T016 |
| FR-004 | T017, T019, T038 |
| FR-005, FR-006, FR-007 | T009, T010, T011, T012, T013, T014, T015, T017, T019, T038 |
| FR-008 | T003, T017 |
| FR-009, FR-011 | T003, T029, T033, T038 |
| FR-010 | T029, T033 |
| FR-012 | T021, T029, T033 |
| FR-012a | T030, T033, T038 |
| FR-013, FR-014 | T021, T022, T024, T027, T029, T033, T049 |
| FR-013a | T005, T006 (helper); T007, T008 (today's search); T032, T033 (new search); T034, T035 (automatic search) |
| FR-014a, FR-014c | T021, T024, T027, T029, T033, T038 |
| FR-014b | T034, T035 |
| FR-015, FR-026 | T021, T022, T025, T026, T027, T030 |
| FR-015a | T030, T033, T038 ("not on the Receive page" holds because no task adds it there; checked in T039) |
| FR-015b | T021, T036, T037 |
| FR-015c | T021, T024, T025, T026, T027, T034 |
| FR-016 | T003, T029, T033, T038 |
| FR-017 | T024, T027 |
| FR-018, FR-019 | T040, T042, T043 |
| FR-020 | T029, T033, T038 |
| FR-021, FR-022 | T040, T041, T042, T043 |
| FR-023 | T031, T033, T038 |
| FR-024 | T041, T042, T043 |
| FR-025 | T021, T024, T025, T027, T034 |
| FR-027 | T029, T033 (lookups asserted to be address lookups only; no task sends a key, and nothing tests that directly) |
| FR-027a | T003, T038; checked by hand in T039 (web only, no automated test) |
| FR-028 | T003, T048 |
| SC-001 | T020 (by hand: the offer leads to the funds from Home alone) |
| SC-002 | T029, T033 |
| SC-003 | T046 |
| SC-004 | T040, T044 |
| SC-005 | T031, T040, T041, T043 |
| SC-006 | T009, T010, T012, T013 |
| SC-007, SC-007a | T024, T025, T029, T034 |
| SC-008 | T024, T029 |

## Implementation record (2026-11-24)

What was done differently from the list above, and what the checks found.

- **Order of work**: T024 to T026 were written after T027, T029 to T032 after T033, and T040 and T041 after T042. The tests exist and pass; they were not written first.
- **T038, wording**: `recovery.receive_search.finished_found` reads "Searched the first {0} addresses. {1} added ({2}).", where {2} is the amount in DGB or the `amount_unavailable` text, so the sentence also works when the amount cannot be read.
- **T033**: `ReceiveSearchBatchSize` is public, so tests can refer to it.
- **Test harness**: `FakeChain.HasAddressHistoryAsync` takes a lock around its counters. Held lookups resume on several threads, and the log lost entries without it.
- **Test run**: the wallet suite takes about 60 to 75 seconds, up from about 41. On every full run two to four timing-based tests failed (`ParallelQueryTests`, `NodeSourceMonitorTests`, `NodeSourceMonitorLoopTests`, `AppStateTests`, `OracleDataServiceClientTests`), a different set each time; three of them failed on the run made before story 2. Each passes when its class is run alone.
- **T020, T039, T044 (browser), checked**: offer shown only after the load finishes; Recovery opens focused; offer stays without a search, goes after a finished search and after Dismiss, and stays gone after lock and unlock; count field prefilled, note follows the number, 0 and 5001 refused; default search runs with moving progress; Cancel stopped a search in 0.5 seconds (limit 5); leaving the page stopped the search.
- **T020, T039, T044 (browser), not checked**: a newly created wallet (A2); a search that fails offline (A5, C3); everything that needs a key with payments past the 40th address (A6, B4 to B9, D1); the testnet refusal (B10). The found-funds cases are covered by unit tests only.
- **T046**: a search of the first 5,000 addresses on mainnet took 4 minutes 12 seconds (target: under 10 minutes). The browser pane was hidden for part of the run, which slows a page, so a fully visible run should not be slower.
- **T047**: not possible to test here: no light-client gateway was available. Research open point 1 stays open.
- **T048**: in Spanish at 375 pixels wide the card's new text is translated and nothing overflows. Not checked: the Home offer in Spanish, light mode, and a review of the Spanish strings by a Spanish speaker. Two things seen: counts are written "5,000" in Spanish too (the page formats numbers in the app's fixed culture) while the Spanish range message says "5.000"; and the card's title and introduction are still English, as they were before this feature.
- **Development only**: after a rebuild, the lock screen on a local port used before reloads itself every few seconds. The browser's cached copy of the development service worker differs from the rebuilt one, the wallet applies the "update" while locked, and the development worker does not take over. Not changed here.
