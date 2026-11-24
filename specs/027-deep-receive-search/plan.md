# Implementation Plan: Find Payments Past the Normal Search

**Branch**: `feature/deep-receive-search` (spec directory `027-deep-receive-search`) | **Date**: 2026-11-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/027-deep-receive-search/spec.md`

## Summary

Let a person find and spend payments that sit beyond the wallet's automatic address search, and tell them the option exists when a restored wallet looks empty.

1. **Checked addresses become a set.** Today the wallet checks receive addresses `0 .. N-1` for a single saved `N`. A new pure class, `ReceiveScanSet`, turns that into three independent parts: a base run that only grows, individually watched addresses beyond it (found paid, or handed out by the wallet), and a 20-address tail after the highest paid one. Balance, history, signing, the gateway scope and new-address hand-out all read it. A wallet that never uses the wider search gets exactly today's addresses.
2. **A search with a chosen count.** `WalletService.SearchReceiveAddressesAsync` checks the wallet's first N addresses, where the person picks N (default: 500 past the furthest address already checked or searched; maximum 5,000), in batches of 20. It first asks about a throwaway control address to make sure the source can be trusted, saves paid addresses as it goes, stops at N and never extends itself, can be cancelled, and returns an outcome instead of throwing. It replaces the existing fixed-reach scan. Found addresses are permanent: there is no undo.
3. **The automatic search keeps up.** After a far-off find, each unlock also makes a watched pass: it checks the watched addresses not yet paid and the 20 addresses past the highest paid one, so the next few payments appear without another manual search.
4. **The offer.** Home's empty state shows a dismissible "search further" message for restored and watch-only wallets whose completed load found nothing. It opens the Recovery page at the search, and it stays until a search has actually finished or the person dismisses it. Pages that bring a wallet onto the device start recording how it got there.
5. **Found funds are not a first payment.** Before any search changes a wallet to include a found address, it records that the wallet has history, so old payments never trigger the first-payment celebration. This also fixes that fault in today's search.

No server, signing-path, secret-storage or network-destination change. The only added traffic is the address lookups of a search the person starts, plus, for a wallet that has used it, a lookup per unpaid watched address and at least 20 more on each unlock.

## Technical Context

**Language/Version**: C# / .NET 10, Blazor WebAssembly (PWA)

**Primary Dependencies**: Existing only (NBitcoin, already used for key and address derivation); no new packages

**Storage**: Four new fields on the wallet's existing `WalletInfo` JSON record; two new non-secret `ISecureStorage` keys per wallet (`walletOrigin:{id}`, `searchOffer:{id}`). Search progress and results are in memory only. See [data-model.md](data-model.md).

**Testing**: xUnit in `tests/DigiByte.Wallet.Tests`, written first, using the existing `Env` / `FakeChain` harness. There is no web test project, so pages and components are checked in the browser preview with [quickstart.md](quickstart.md).

**Target Platform**: Installed PWA and mobile/desktop browsers

**Project Type**: Web app (Blazor WASM) with a separate wallet-logic library

**Performance Goals**: SC-003 a 5,000-address search within 10 minutes (measured outside the wallet: about 5.6 minutes; not yet timed inside it); SC-004 cancel within 5 seconds (one batch of 20 lookups: about 1.3 seconds outside the wallet, about 2.6 at the slowest rate seen inside it); SC-007 no extra request for wallets that never use the search; SC-007a a refresh after a far-off find adds at most the found addresses plus 20.

**Constraints**: A search that does not finish never reads as "nothing found"; paid addresses are saved batch by batch; nothing is checked if the control address fails; only one search per wallet; the search must not poison the history sync's snapshot with thousands of unwatched addresses; no new kind of request and no new destination; English and Spanish.

**Scale/Scope**: 2 new pure classes, 1 store and 4 small records in the wallet library; 3 new methods and 8 changed members on `WalletService`; 1 method and 1 record removed; 1 new web service; 1 new component; changes to 6 pages; 22 translation keys in 2 languages; 50 tasks, 16 of them test tasks, plus one test-harness task.

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1. Still passes.*

| Principle | Assessment |
|-----------|------------|
| I. Self-custody | No seed, private key or credential is read, stored or sent. The control address comes from a key generated for one lookup and discarded; it is not the user's and protects nothing. Pass. |
| II. In-browser cryptography | Signing is unchanged; `GetAddressKeyMap` derives keys for more indexes by the same path. The control key is generated in the browser. No server takes part. Pass. |
| III. Test-first (wallet logic) | `ReceiveScanSet`, the search, the watched pass, hand-out, the Digi-ID sweep rule, `MarkHasHistoryAsync`, `SearchOfferRule` and `SearchOfferStore` are in `DigiByte.Wallet`. In tasks.md every test task precedes the task that makes it pass, and the two "types only" tasks (the wallet fields, the result records) add no behaviour. Tests include negative paths: reach out of range, untrusted source, control lookup failing, lookup failing mid-search, cancel, lock during a search, unreadable stored values, a second concurrent search. Signing for a found address has its own test. Pass. |
| IV. Boundary discipline | Decisions and storage rules in `DigiByte.Wallet`; `DigiByte.Web` owns the pages, navigation and wording. `DigiByte.Web` → `DigiByte.Wallet` only, as today. `DigiByte.Crypto` is not touched. Pass. |
| V. Resilience | A failed or cancelled search leaves the wallet working and keeps what it found. Address lookups set no cooldown and no outage state, so a rate-limited search cannot take the explorer away from balance and send. The offer needs no network and never blocks the empty state. Core flows do not depend on any new piece. Pass. |
| Security & privacy | No third-party script, no CSP change, no new origin. A search sends more of the wallet's own addresses to the explorer the wallet already uses, only when the person starts it, and the card says so beforehand (FR-027a). The account public key is never sent. Pass. |

No violations, so Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/027-deep-receive-search/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── receive-search.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/DigiByte.Wallet/
├── Models/
│   ├── WalletInfo.cs                  # + ReceiveBaseFloor, WatchedReceiveIndexes, ReceiveSearchedTo, ReceiveSearchNoticeShown
│   ├── ReceiveSearchResult.cs         # NEW: result, outcome, progress records
│   └── ReceiveRecoveryResult.cs       # REMOVED
└── Services/
    ├── ReceiveScanSet.cs              # NEW: pure; which receive indexes are checked (FR-013 to FR-015, FR-025, FR-026)
    ├── SearchOfferRule.cs             # NEW: pure; show the offer? (FR-001 to FR-007)
    ├── SearchOfferStore.cs            # NEW: walletOrigin:{id}, searchOffer:{id}
    ├── ReceiveAddressList.cs          # WindowStart / NextWindow page through the checked indexes (FR-015b)
    └── WalletService.cs               # SearchReceiveAddressesAsync (replaces ScanForMissingReceiveFundsAsync);
                                       # MarkHasHistoryAsync; GetListedReceiveIndexes; GetAllAddresses, GetAddressKeyMap,
                                       # GenerateNewReceivingAddressAsync, FindUsedReceiveAsync,
                                       # PlanDigiIdSweepAsync, ExecuteDigiIdSweepAsync,
                                       # PersistAddressIndexesAsync, DeleteWalletAsync

src/DigiByte.Web/
├── Program.cs                         # register SearchOfferStore and SearchOfferCoordinator
├── Services/SearchOfferCoordinator.cs # NEW
├── Components/Wallet/SearchFurtherOffer.razor   # NEW
├── Pages/
│   ├── Home.razor                     # render the offer in the plain empty state
│   ├── Recovery.razor                 # count field, progress, cancel, result; ?search=receive
│   ├── Receive.razor                  # address list follows the checked set
│   ├── CreateWallet.razor             # record origin: created
│   ├── RecoverWallet.razor            # record origin: restored
│   └── ImportXpub.razor               # record origin: watch-only
├── Pages/Help.razor                   # describe the search and the offer
└── wwwroot/locales/{en,es}.json       # new keys

docs/PROCESS_FLOWS.md                  # the flow, with the public-explorer note
CHANGELOG.md                           # entry under the next unreleased version

tests/DigiByte.Wallet.Tests/
├── FoundFundsHistoryTests.cs          # NEW: MarkHasHistoryAsync
├── ChangeAddressTests.cs              # harness additions to FakeChain and Env (no behaviour change)
├── ReceiveAddressListTests.cs         # paging over a list of indexes that is not an unbroken run
├── ReceiveScanSetTests.cs             # NEW
├── ReceiveSearchTests.cs              # NEW: search, control address, cancel, failure, watched pass, hand-out, signing
├── SearchOfferRuleTests.cs            # NEW
├── SearchOfferStoreTests.cs           # NEW
└── ReceiveAddressTests.cs             # today's search and found-funds history; DeepScan_* tests moved to the new method
```

**Structure Decision**: The existing layout. Wallet rules and storage go in `DigiByte.Wallet` so they can be tested; the web project holds only what needs a page.

## Order of work

The stories are built in the spec's priority order, which is the order of [tasks.md](tasks.md). Each step leaves the build and the suite green and is independently testable.

1. **Foundation**: `MarkHasHistoryAsync`, the helper that keeps found funds from being celebrated as a first payment. Both stories call it.
2. **The offer (Story 1)**: store, rule, coordinator, component, Home, origin recording on the three pages, `?search=receive` on the Recovery page, and the helper call in today's search. It uses the wider search as it exists today, so it can ship alone.
3. **The checked-address set (start of Story 2)**: test-harness additions, `ReceiveScanSet`, the wallet fields, then the wiring into `GetAllAddresses`, `GetAddressKeyMap`, the shared save, hand-out and the Digi-ID sweep. No behaviour change for any existing wallet; the existing suite is the regression test.
4. **The search (rest of Story 2)**: the result types; `SearchReceiveAddressesAsync` with the control address in front and the helper call per batch; the automatic search's watched pass; the Receive list; and last, in one change, the Recovery card moved to the new search with a message for every outcome it can return, the old search's tests moved, and the old search removed.
5. **Progress, cancel and honest outcomes (Story 3)**: cancel, mid-search failure and lock handling, one search at a time, and the card's progress line, cancel button and remaining messages.

Step 2 is independent of steps 3 to 5; tasks.md lists the three places they meet and what changes if Story 2 is built first. Steps 4 and 5 should ship together: a count field without cancel invites long searches that cannot be stopped.

## Risks

| Risk | Handling |
|---|---|
| A reader of the address count is missed and found funds cannot be seen or signed for | Research R3 lists every reader found; tests cover balance, history, send and reload for a far-off find |
| The checked-address model is wrong again (three drafts failed review) | It is now three independent parts with one invariant each (research R13); T021 tests every row of the five worked examples in data-model.md, which are the sequences that broke the earlier drafts or went untested |
| A save from another path drops the new fields | `PersistAddressIndexesAsync` copies them; a test saves through an unrelated path and reloads |
| The explorer limits a 5,000-lookup search | The search ends as `Failed`, keeps what it found, and can be run again; lookups set no cooldown |
| The gateway's ongoing sync does not watch an added address | Quickstart E2 (tasks.md T046); the mechanism is the one today's wider search uses |
| The task list is wrong in ways review has not caught (it was rewritten in one pass and that rewrite was not independently reviewed) | Every task leaves the build and the suite green, so a wrong order shows up at the task where it bites; T050 feeds corrections back into the documents |
| A search adds addresses wrongly and there is no undo | Accepted (research R15): the control address stops the realistic cause; the remedy is to remove and restore the wallet, and the spec says so |
| The offer annoys people who simply have an unfunded wallet from before the feature | Shown once, dismissible, worded not to imply missing funds (FR-008); never for wallets created afterwards |

## Complexity Tracking

No constitution violations.
