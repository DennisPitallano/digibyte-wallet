# Quickstart: Find Payments Past the Normal Search

How to check the feature works. Details are in [contracts/receive-search.md](contracts/receive-search.md) and [data-model.md](data-model.md). Task numbers are those of [tasks.md](tasks.md).

## 1. Automated tests

```powershell
dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName~ReceiveScanSet|FullyQualifiedName~ReceiveSearch|FullyQualifiedName~SearchOffer|FullyQualifiedName~ReceiveAddress|FullyQualifiedName~FoundFundsHistory|FullyQualifiedName~DigiIdFundsRecovery"
```

Counts are counts of addresses from the first one: "the first 500" is indexes 0 to 499.

### Found funds are not a first payment (T005, T007, T032, T034)

| Scenario | Expected |
|----------|----------|
| Wallet recorded as empty, marked as having history | state `History`; a later load with receipts returns no first payment |
| Wallet not yet decided, marked as having history | state `History` |
| Wallet already `History` or `Celebrated` | unchanged |
| Today's search adds addresses to a wallet recorded as empty | state `History` |
| New search, first batch finds a payment, later lookups held | state already `History` while the search runs |
| Automatic search's watched pass finds the first paid address | state `History` |
| A search that finds nothing | an empty wallet stays `Empty` |

### The offer (T009 to T011)

| Scenario | Expected |
|----------|----------|
| Offer rule | every row of the table in data-model.md, section 5 |
| Offer store: unreadable answer | reads as dismissed |
| Offer store: unreadable origin | reads as unknown |
| Offer store: set origin twice | first value kept |
| Offer answer `Searched` | never goes back to unanswered |
| Deleting a wallet | its `walletOrigin:` and `searchOffer:` keys are gone; another wallet's stay |

### The checked-address set (T021, T024 to T026)

| Scenario | Expected |
|----------|----------|
| No watched entries | indexes `0 .. max(20, next + 1, used) - 1`; next new index and limit as today |
| Every row of worked examples A to E in data-model.md | base count, tail and number checked as tabled |
| Address on show (3,003, watched) is found paid | base count still 20, not 3,004 |
| Wallet with address on show at 34, find at 3,000, new address handed out | floor becomes 35; 20 to 34 still checked |
| Base walk grows over the address on show | base count follows the used-count; no jump |
| Base run grows over a watched entry | that entry is dropped from the stored record; nothing else changes |
| One paid watched address at 3,000 | 41 receive indexes checked (20 base, the find, 20 tail); the payment is in the balance and the history, can be sent, and is still counted after a reload with the explorer down |
| Unpaid watched address | checked; a payment to it is in the balance |
| Rename or colour change, then reload | the four new fields are kept |
| Hand out a new address after finds at 3,000 and 3,001 | index 3,002, recorded as watched; refused once 20 unpaid are out past the anchor |
| Hand-out save fails | address on show and floor as before; 3,002 not watched; an entry added meanwhile is kept |
| Digi-ID sweep of 3 after a find at 3,000 | destinations 3,001 to 3,003 and the new address on show 3,004 are unpaid watched entries; base count still 20; a new address can still be handed out |
| Unlock with a find at 3,000 | no index from base count + 20 up to 2,999 is looked up |

### The search (T029 to T031)

| Scenario | Expected |
|----------|----------|
| Payment at index 3,000, first 5,000 searched | `Finished`; balance includes it; the lookups are one control address, then the search targets |
| Same payment, default count | `Finished`, nothing added |
| Found funds | spendable; still there after a reload |
| Nothing to find | `Finished`, nothing added, furthest search saved |
| Count inside the base run | `NothingToCheck`, no address lookups, furthest search unchanged |
| Count of 0, -1, 5,001 | refused before any lookup |
| Repeat search after a late payment to a previously unpaid address | found |
| Payment at 3,010 in the tail, then a search finds 3,500 | 3,010 is a paid watched entry; balance unchanged by the tail moving |
| First 5,000 then first 600, both finished | furthest search stays 5,000 |
| Default count with a paid address at 3,000 and no finished search | 3,521 |
| Default count after a finished search of the first 2,000, nothing watched | 2,500 |
| First 500, payment at index 495 | suggests searching further; no index at or above 500 looked up |
| First 500, last payment at index 300 | no suggestion |
| First 5,000, payment at index 4,995 | no suggestion (already at the maximum) |
| First search that marks an address paid | notice flag set once; a second such search does not set it again |
| Unpaid addresses left unwatched below the count | the result says so |
| History sync in use | no address-summary requests; the sync's snapshot gains nothing |
| Source reports every address used | `Untrusted`; one lookup (the control); nothing saved |
| Control lookup fails | `Failed`, covered 0, nothing saved |
| Balance read for the found addresses fails | still `Finished`; addresses added; amount not available |
| Single-key wallet | `NothingToCheck`, no lookups |
| Watch-only wallet | searches like any other |
| First batch finds a payment, later lookups held | that address is already saved in the stored record |
| Indexes 100 to 104 all paid, first 105 searched | all five added |
| Control address | different each search; never one of the wallet's addresses |

### The automatic search's watched pass (T034)

| Scenario | Expected |
|----------|----------|
| Find at 3,000, new payment at 3,005, unlock | 3,005 becomes a paid watched entry; tail moves |
| Unpaid watched address paid, unlock | marked paid; hand-out limit moves with it |
| Watched `{3000 paid, 3015 unpaid}`, outside payment on 3,005, 3,015 newly paid, unlock | both 3,005 and 3,015 are paid watched entries |
| Handed out 3,002, then a find at 3,010; payment to 3,002 | in the balance |
| Only unpaid watched entries | no walk; the entries are looked up |
| The pass's walk hits its 500-lookup cap | the whole pass is discarded; nothing is marked paid |
| Base walk gave up at its cap | the watched pass does not run |
| Address handed out while the pass is held | still a watched entry afterwards |
| No watched entries, unlock | the same lookups as before this feature |

### The address list (T036)

| Scenario | Expected |
|----------|----------|
| No watched entries | every index from 0 to the address on show, as today |
| Watched 3,000 and 3,001 paid and 3,002; address on show 3,002 | base run, 3,000, 3,001, 3,002; nothing in between; no tail-only index |
| Paging | ten at a time by position in that list |

### Stopping and failing (T040, T041)

| Scenario | Expected |
|----------|----------|
| Cancel during the second batch, payment found in the first | `Cancelled`; that payment kept; no further batch starts; furthest search unchanged |
| Progress | reported once per batch, rising |
| Lookups fail in the third batch, payment found in the first | `Failed`; that payment kept; covered = end of the second batch |
| Wallet locked during a search | `Cancelled`; nothing half-saved |
| Second search started while one runs | same run returned; no extra lookups |

The whole wallet suite must still pass. The six `LiveExplorerBenchmarks` tests call an explorer that is offline and fail on `main`; leave them out:

```powershell
dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName!~LiveExplorerBenchmarks"
```

## 2. In the app

Start the wallet with the `digibyte-web` preview configuration (http://localhost:5251). The dev build starts on testnet, where the fallback source reports every address as used; switch to **Mainnet** in Settings for anything that needs real answers. Keep the browser pane visible: a hidden page pauses its background checks and runs slowly.

You need an account key with payments past the automatic search's reach. Any key whose first paid address is the 41st or later will do. A watch-only import is enough for everything except sending.

### A. The offer (Story 1; checked by T020)

- **A1.** Import that key as watch-only. Home shows the loading state, then the empty state with the **search further** offer above the address and QR code. It is not shown while Home says it is still looking.
- **A2.** Create a new wallet. Home shows the empty state with no offer.
- **A3.** Back in the watch-only wallet, dismiss the offer. It disappears. Lock, unlock: still gone.
- **A4.** Import the key again into a clean profile. Follow the offer: the Recovery page opens at the Missing Payments card with the start button focused. Go back to Home without starting: the offer is still there.
- **A5.** Go offline, follow the offer and start the search: it fails. Return to Home: the offer is still there.
- **A5a.** Go online, follow the offer again and let the search finish. Return to Home: the offer is gone, whether or not anything was found.
- **A6.** When that search has found funds: Home shows the balance and the transactions, and no "Your first DGB" celebration appears on Home, Receive or Activity.

### B. The search (Story 2; checked by T039)

- **B1.** Open the card (from the offer or from Recovery). The count field is focused and filled in. A note says the search checks up to that many of this wallet's addresses with the public block explorer. Change the number: the note follows it.
- **B2.** Enter 0, then 5001, then letters: start is disabled and the accepted range (1 to 5,000) is shown.
- **B3.** Start with the default count. The result says how many addresses were covered.
- **B4.** Search the first 5,000 addresses on a key with payments in that range. The result gives the number of addresses added and the amount, the one-time notice about the key being used elsewhere, and the note about unpaid addresses not being watched.
- **B5.** Home shows the balance and the transactions. No first-payment celebration appears.
- **B6.** Lock and unlock. The balance is still there, and it appears as fast as it did before the search.
- **B7.** Run the same search again. No notice this time. The card shows the furthest search so far.
- **B8.** In a wallet restored from a recovery phrase (not watch-only), send some of the found funds.
- **B9.** Search the first 500 on a key with a payment just inside that range's last 20 addresses: the result suggests searching further and its action fills in a larger count without starting.
- **B10.** Switch to testnet (the source that reports everything as used) and start a search: it refuses, says the answers can't be trusted, and Home is unchanged.

### C. Stopping and failing (Story 3; checked by T044)

- **C1.** Start a search of the first 5,000. A progress line shows how many addresses have been covered out of 5,000 and how many found, and it moves.
- **C2.** Cancel after a few seconds. Time it: it must stop within 5 seconds (SC-004). The result says how many addresses were covered and that the rest were not checked.
- **C3.** Start a search of the first 5,000 and go offline partway. The result says it could not finish and how many addresses it covered; it never says nothing was found.
- **C4.** Start a search and leave the page. Come back: no search is running.

### D. Receive (Story 2; checked by T039)

- **D1.** In a wallet where a search found addresses far along, open Receive and generate a new address. It is the one after the highest found. "Your addresses" shows the wallet's checked addresses up to it, not thousands of entries.

### E. Timing and other modes (Polish; T046 to T048)

- **E1.** Time a search of the first 5,000 on mainnet with the pane visible. Target: under 10 minutes (SC-003). Record the figure in the pull request.
- **E2.** With the light-client gateway as the first source, repeat B4 to B6 and confirm the found funds still show after the next gateway sync (research, open point 1).
- **E3.** Spanish: switch language and confirm the offer, the card and every result message are translated.
- **E4.** Dark and light mode, and a phone-width viewport, for the offer and the card.
