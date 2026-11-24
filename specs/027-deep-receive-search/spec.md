# Feature Specification: Find Payments Past the Normal Search

**Feature Branch**: `feature/deep-receive-search` (spec directory `027-deep-receive-search`)

**Created**: 2026-11-24

**Status**: Draft

**Input**: User description: "Find receive funds beyond the standard gap limit. Problem: when the same seed/account key is also used by an outside issuer (for example a payment/deposit page that hands out one fresh receive address per order, paid or not), paid addresses can sit far past the wallet's automatic scan, which stops 20 unused receive addresses after the last used one. Real case: an account whose addresses 0-21 were never paid and whose addresses 22 and 23 hold 755.8 DGB shows a zero balance after restore. The wallet already has a manual deeper search on the Recovery page (looks 500 addresses past the last used one, bounded at 2000), but nothing tells the user it exists and it cannot cover a full issuer keyset (up to 5,000 addresses). Two changes: (1) Prompt: after a restore or import from seed whose scan finds no funds or history (and wherever else an unexpectedly empty wallet is shown right after restore), offer a clear \"Expected funds here? Search further\" prompt that leads to / starts the deeper search, dismissible, not shown to newly created wallets. (2) Reach: let the user of the deeper search choose how far to look (\"search up to address number N\"), so a whole issuer keyset range (up to 5,000 addresses) can be covered, with progress, the ability to cancel, a failed/incomplete search never presented as an empty result, and found addresses joining the normal balance and scan so funds are spendable with nothing to sweep. Out of scope: changing the automatic gap limit of 20, changing how the wallet itself issues addresses (it already refuses to hand out more than 20 unused in a row), and any change to the outside issuer."

## Context

A wallet finds its money by walking along its receive addresses in order and stopping once 20 in a row have never been paid. That is the convention almost every wallet follows, and this wallet keeps to it when it hands out addresses: it will not give out a new one while 20 unpaid ones are outstanding.

**What the wallet does today.** It always checks its first 20 receive addresses. Each time it is unlocked it also searches onward, to at least 20 addresses past that range and then until it has seen 20 unpaid addresses in a row after the last paid one. A freshly restored wallet therefore reaches address 39 by itself, and further whenever it keeps finding paid addresses.

The convention breaks when something other than the wallet hands out addresses from the same key. A shop or deposit page that gives every order its own address, whether or not the order is paid, can leave long runs of unpaid addresses. This feature began with an account whose addresses 0 to 21 were unpaid and whose addresses 22 and 23 held 755.8 DGB. That account turned out to be within the automatic search's reach: the wallet found the funds by itself, and the zero it briefly showed was a display fault, fixed separately (pull request 109). The same key had left 22 of its first 24 addresses unpaid, though, so a run of 20 or more unpaid addresses in a row is a realistic next step for it. When that happens, every payment after the run is invisible to the wallet: on a fresh restore if the first paid address is number 40 or later, and on any wallet once a paid address sits more than 20 unpaid addresses past the last one found.

The wallet already has a wider search on its Recovery page for this. Two things stop it from helping. Nothing on an empty wallet tells the person the search exists, and it reaches a fixed distance (500 addresses past the last paid one), which is shorter than the 5,000 addresses one such issuer may use.

This feature points people to the wider search when a restored wallet looks unexpectedly empty, and lets them say how far it should look.

## Clarifications

### Session 2026-11-24

- Q: When a restored or watch-only wallet finishes its first load and finds nothing, should the wallet only offer the wider search, or run it by itself? → A: Offer only. Home shows the message; the person follows it and presses start. The wallet never starts a wider search on its own.
- Q: When a wider search finds payments at addresses the wallet never handed out itself, should the wallet warn the person that this key seems to be giving out addresses somewhere else? → A: Warn once. The search result adds a short notice that addresses from this key appear to be handed out elsewhere, and that new addresses made in this wallet may be reused by that other place. It is shown the first time a search adds paid addresses to a wallet, blocks nothing, and does not appear on the Receive page.
- Q: If a wider search is cancelled or fails partway, should the paid addresses it had already found be kept in the wallet? → A: Keep them. Addresses confirmed as paid before the stop join the wallet, and the result says the search is incomplete and how far it got. This applies to a cancel and to a failure alike.
- Q: Should wallets that were restored or added before this update, and are still empty, also show the offer the first time they load after the update, or only wallets restored from now on? → A: Existing wallets too. Any restored or watch-only wallet that is empty and has not dismissed the offer shows it after the update. (What happens when the device cannot tell whether a wallet was restored is settled by a later answer in this list, the one about seed wallets already on a device.)
- Q: Should a restored wallet that shows some funds, but could be missing others further along, get any pointer to the wider search? → A: No. The offer is only for wallets that found nothing. A wallet with funds relies on the existing Recovery page.
- Q: Should seed wallets that are already on a device and empty, where the wallet cannot tell whether they were restored or created, show the offer once after the update? → A: Yes, show once. The wallet keeps no record of how an existing seed wallet came to be on the device, so every existing empty seed wallet shows the offer once after the update, worded so it also makes sense to someone who simply has not received anything yet. Wallets created after the update never show it.
- Q: After a wider search finds a payment far along, should the wallet go on checking every address before it on each refresh, or only the addresses that were actually paid? → A: Paid only. The wallet keeps its normal range plus the individual paid addresses the search found. Unpaid addresses in between are not checked again until the person runs another search, and the result tells them that.
- Q: How should the wider search decide that the explorer's answers can't be trusted? → A: With a control address. Before searching, the wallet asks the data source about an address that cannot have been paid. If the data source says it was, the search does not run and says the answers can't be trusted. A run of addresses that are all reported paid is otherwise accepted, however long.
- Q: When a search finds a payment close to the end of the range the person chose, should it stop at that number or keep going past it? → A: Stop and suggest. The search stops at the chosen number. If it found a payment within the last 20 addresses of the range, the result says more may lie beyond and offers to search further. It never extends itself.
- Q: Should the search card tell the person, before they start, that the wallet's addresses will be checked with the public block explorer? → A: Yes, with a one-line note next to the start button saying the search checks up to N of this wallet's addresses with the public block explorer. No confirmation step.
- Q: Should the wallet give the person a way to remove addresses that a search added by mistake? → A: No. An address a search or the automatic search has marked as paid stays in the wallet's checks for good. The spec says so, and says the remedy for a bad search is to remove the wallet from the device and restore it. A reset action is left for later if it is ever needed.
- Q: Should the offer on Home stay when the search the person started did not finish? → A: Yes. The offer is removed when a search completes, with or without finds. A search that failed, was cancelled or was refused leaves the offer in place. The person can still dismiss it.
- Q: Should the search field ask how many addresses to search, counting from the first, or ask for the index of the last address to check? → A: How many. The field reads "Search the first N addresses", accepts 1 to 5,000, and checks the wallet's first N receive addresses. Results say "searched the first N addresses". The wallet shows no address numbers of its own, so nothing else in it needs to agree with a numbering.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A restored wallet that looks empty offers to search further (Priority: P1)

A person restores a wallet from its recovery phrase, or adds it as watch-only, expecting to see money. The wallet finishes loading and shows nothing. On Home, next to the empty state, a short message asks whether they expected funds here and offers to search further. They follow it, start the search, and the payments that sat past the normal search appear in their balance.

**Why this priority**: Without this, a person looking at a zero balance has no reason to believe the money exists or to visit the Recovery page. It works with the search exactly as it is today.

**Independent Test**: Restore a wallet whose only paid addresses are numbers 60 and 61, beyond what the automatic search reaches. Confirm that Home shows the offer once loading has finished, that following it leads to the wider search, and that after running the search the balance shows the funds.

**Acceptance Scenarios**:

1. **Given** a wallet restored from a recovery phrase whose first complete load finds no payments, **When** the person arrives on Home, **Then** a message asks whether they expected funds and offers to search further.
2. **Given** a wallet added as watch-only whose first complete load finds no payments, **When** the person arrives on Home, **Then** the same offer is shown.
3. **Given** the offer is showing, **When** the person follows it, **Then** they arrive at the wider search, ready to start, without having to find the Recovery page themselves.
4. **Given** the offer is showing, **When** the person dismisses it, **Then** it disappears and does not return for that wallet, and the wider search remains available on the Recovery page.
5. **Given** a wallet created on this device after this feature exists, **When** the person arrives on Home with no payments, **Then** the offer is not shown, however long the wallet stays empty.
6. **Given** a restored wallet whose first load found at least one payment, **When** the person arrives on Home, **Then** the offer is not shown.
7. **Given** a restored wallet whose first load could not finish (no connection, data source unavailable), **When** the person arrives on Home, **Then** the offer is not shown until a load has completed and found nothing.
8. **Given** the offer was shown and the wider search then found payments, **When** the person returns to Home, **Then** the offer is gone and the balance includes those payments.
9. **Given** a wallet that was restored or added as watch-only before this feature existed and is still empty, **When** it completes a load after the update, **Then** the offer is shown, exactly as for a wallet restored today.
10. **Given** an empty seed wallet from before this feature, for which the device has no record of whether it was restored or created here, **When** it completes a load after the update, **Then** the offer is shown, and dismissing it removes it for good like any other.

---

### User Story 2 - Choose how far the search looks (Priority: P2)

A person knows their addresses were handed out by a shop or deposit page that may have used several thousand of them. On the wider search they enter how many of the wallet's addresses to search, counting from the first, start it, and watch its progress. When it finishes, every paid address among those is part of their wallet.

**Why this priority**: The fixed reach of today's search covers a run of up to 500 unpaid addresses, but not an issuer that left more than that in a row. Letting the person set the distance removes that limit. It depends on nothing in Story 1.

**Independent Test**: With a wallet whose only paid address is number 3,000, run the search with its default reach and confirm nothing is found; run it again up to address number 5,000 and confirm the payment appears in the balance and can be spent.

**Acceptance Scenarios**:

1. **Given** the wider search, **When** the person opens it, **Then** they can enter how many addresses to search, in a field that reads "Search the first N addresses", with a default already filled in that matches the distance the search starts with today.
1a. **Given** the wider search before it is started, **When** the person looks at it, **Then** a short note says the search checks up to the chosen number of this wallet's addresses with the public block explorer, and the number in the note follows the number they enter.
2. **Given** the person enters a number above 5,000, zero, a negative number or something that is not a whole number, **When** they try to start, **Then** the search does not start and a message says that 1 to 5,000 is accepted.
3. **Given** a paid address at number 3,000 and nothing before it, **When** the person searches up to 5,000, **Then** the search finds it, and the result states how many addresses were added and how much DGB they hold.
4. **Given** a search has found paid addresses, **When** it finishes, **Then** those funds are in the wallet's balance and history, and can be sent like any other funds, with no separate step to move them.
5. **Given** a search has found paid addresses, **When** the wallet is closed and opened again, **Then** those addresses are still checked and the funds still show.
6. **Given** a search has finished, **When** the person looks at the wider search again later, **Then** it shows the furthest any finished search has covered, and a later, shorter search does not lower it.
7. **Given** a search up to a chosen number finds nothing, **When** it finishes, **Then** the result says no paid addresses were found up to that number, naming the number.
7a. **Given** a search up to 500 that finds a payment at address 495, **When** it finishes, **Then** it has checked nothing past 500, and the result says more payments may lie beyond 500 and offers to search further with a larger number already filled in.
7b. **Given** a search up to 500 whose last payment found is at address 300, **When** it finishes, **Then** the result makes no such suggestion.
8. **Given** a wallet whose wider searches have never added an address, **When** a search adds at least one paid address, **Then** the result also shows a short notice that addresses from this key appear to be handed out somewhere else and that new addresses made in this wallet may be reused there.
9. **Given** that notice has been shown once for a wallet, **When** a later search adds more paid addresses, **Then** the notice is not shown again, and generating a new address is never blocked or interrupted by it.

---

### User Story 3 - Watch, stop and trust a long search (Priority: P3)

A search across thousands of addresses takes minutes. The person sees how far it has got, can stop it, and is never told "nothing found" when the search did not actually finish.

**Why this priority**: A long search that cannot be stopped, or that reports an empty result after failing halfway, would mislead someone about where their money is. It refines Story 2 and has no value without it.

**Independent Test**: Start a search of the first 5,000 addresses, cancel it partway, and confirm the result says how many addresses were covered and that the search is not complete. Repeat with the connection cut partway and confirm the same kind of message, never "nothing found".

**Acceptance Scenarios**:

1. **Given** a search is running, **When** the person watches it, **Then** they see progress that advances as addresses are checked, showing how many have been covered out of the count asked for.
2. **Given** a search is running, **When** the person cancels it, **Then** it stops promptly and the result says it was stopped, how many addresses it had covered, and that the rest were not checked.
3. **Given** a search fails partway because the data source cannot be reached, **When** it ends, **Then** the result says the search could not finish and how far it got, and does not say that nothing was found.
4. **Given** a search was cancelled or failed after it had already found paid addresses, **When** it ends, **Then** those addresses are kept in the wallet and the result lists them as found, alongside the statement that the search is incomplete.
5. **Given** a data source that reports an address that cannot have been paid as paid, **When** the person starts a search, **Then** the search does not run, says the answers cannot be trusted, and adds nothing to the wallet.
5a. **Given** a trustworthy data source and addresses 100 to 104 all genuinely paid, with nothing paid before them, **When** the person searches up to 105, **Then** all five are added, even though every address checked was paid.
6. **Given** a search is running, **When** the person leaves the page or locks the wallet, **Then** the search ends as if cancelled and nothing is left half-saved.

---

### Edge Cases

- **Some funds found, others further along.** A restored wallet finds payments at its first few addresses while more sit past the normal search. It shows a balance, so it gets no offer: the wallet cannot tell a short total from a complete one. The person can still run the wider search from the Recovery page.
- **Payment arrives later.** A restored wallet is empty, the offer is dismissed, and a payment then arrives at one of the first 20 addresses. The normal search finds it; the offer stays dismissed.
- **Offer followed, nothing found.** The person runs the search and it finishes having found nothing. The offer does not return on Home; the Recovery page still lets them search again with a larger number.
- **Offer followed, search did not finish.** The person starts the search and the connection drops, or they cancel it, or the source is refused as untrustworthy. Back on Home the offer is still there, so they can try again. Dismissing it still removes it for good.
- **Number inside the normal range.** The person enters a count that lies inside the wallet's normal range. The search has nothing new to do and says so, stating how far the wallet already looks.
- **A payment just past the last one found, then a search.** The wallet watches the 20 addresses after its highest paid one, and a payment has arrived at one of them. A search then finds another payment much further along. The first payment is still in the balance afterwards: the search checked those 20 addresses itself before moving on.
- **Searching the same range again.** The person searches up to a number they have searched before. The search checks again every address in that range that is not in the normal range and not already known to be paid (FR-012), so late payments to previously unpaid addresses are found.
- **Wallet's own next address.** After the search finds a payment at address 61, the next new receive address the wallet hands out comes after 61, never one the outside issuer has already been seen to use. The issuer may still hand out that same next address later, which the wallet cannot prevent; the one-time notice in the search result tells the person so.
- **Unpaid addresses in between.** A search finds payments at addresses 3,000 and 3,001 and nothing between the wallet's normal range and 2,999. From then on the wallet checks its normal range, those two addresses, and the 20 after the highest one. The addresses in between are not checked on refresh. A late payment to one of them stays unseen until the person runs the search again, and the search result says so.
- **Found funds are not a "first payment".** A restored wallet loads empty, and a search then finds payments made months ago. Home shows them in the balance and the list with no first-payment celebration.
- **An address the wallet handed out, then overtaken.** After a find at 3,001 the person hands out 3,002. Later the automatic search finds a payment at 3,005. Address 3,002 is still checked, and a payment to it shows up. A refresh still reads a few dozen addresses, not three thousand.
- **The address list after a far-off find.** A search finds payments at addresses 3,000 and 3,001, and the person then makes a new address, which is number 3,002. "Your addresses" on Receive shows the wallet's normal range, 3,000, 3,001 and 3,002. It does not show the 2,980 addresses in between, which the wallet is not checking.
- **Unpaid addresses inside the normal range.** The normal range is the wallet's first 20 addresses plus every address up to the last paid one its automatic search has found. All of it stays checked on every refresh, paid or not, as today.
- **Several wallets on one device.** The offer and its dismissal belong to one wallet. Dismissing it in one wallet does not hide it in another.
- **Wallet types without a run of addresses.** A wallet built from a single private key or a single address has no further addresses to search. It gets no offer, and the wider search is not shown for it, as today.
- **Multi-signature and DigiDollar.** Neither is searched by this feature and neither shows the offer because of it.
- **Data source turns faulty partway.** The control address passes, and the data source starts giving wrong answers later in the search. This is not detected; the control address is checked once, before the search. Addresses wrongly added hold no funds, which shows in the result's total, but they are permanent: the wallet checks them on every refresh from then on and its next new address comes after the highest of them. There is no action to remove them. The remedy is to remove the wallet from the device and restore it, which starts its checks afresh. A source that is merely struggling returns errors, which end the search as incomplete; it does not invent payments.
- **Slow or limited data source.** The data source slows down or refuses requests partway through a long search. The search ends as an incomplete search, keeping what it found.
- **Search already running.** The person tries to start a second search while one is running. Only one runs at a time.

## Requirements *(mandatory)*

### Functional Requirements

**Offer on an empty restored wallet**

- **FR-001**: When a wallet restored from a recovery phrase or added as watch-only completes its first load and no payment is found, the wallet MUST show on Home an offer that asks whether funds were expected and leads to the wider search.
- **FR-002**: The offer MUST NOT be shown for a wallet created on this device after this feature exists, for a wallet whose load has found any payment, or before a load has completed.
- **FR-002a**: From this feature on, the wallet MUST record for each new wallet whether it was created on this device, restored from a recovery phrase, or added as watch-only.
- **FR-002b**: Wallets already on a device before this feature have no such record. An existing watch-only wallet MUST be treated as added. An existing seed wallet MUST be treated as possibly restored: if it is still empty after its next completed load, it shows the offer, once, under the same dismissal rules as any other (FR-005, FR-006).
- **FR-003**: The offer MUST NOT be shown for wallet types that have no further receive addresses to search.
- **FR-004**: Following the offer MUST take the person directly to the wider search, with the search ready to start. The search MUST begin only when the person starts it; the wallet MUST NOT start a wider search on its own, on restore or at any other time.
- **FR-005**: The person MUST be able to dismiss the offer. A dismissed offer MUST stay dismissed for that wallet on that device, including after the wallet is closed and reopened.
- **FR-006**: The offer MUST disappear by itself once the wallet has any payment, and MUST NOT return once a wider search has finished for that wallet, started from the offer or directly on the Recovery page, whether or not it found anything. A search that did not finish (it failed, was cancelled, was refused because the source could not be trusted, or had nothing to check) MUST leave the offer as it was.
- **FR-007**: The offer and its dismissal MUST be kept separately for each wallet on the device.
- **FR-008**: The offer MUST explain in one or two plain sentences why funds might be missing (addresses handed out elsewhere and not all paid), without requiring the person to know what a gap limit is. Because it can also reach someone who created a wallet before this feature and has not been paid yet (FR-002b), the wording MUST read sensibly to that person too and MUST NOT state or imply that funds are missing.

**How far the search looks**

- **FR-009**: The wider search MUST let the person choose how many of the wallet's receive addresses to search, counting from the first. The field MUST read as a count ("Search the first N addresses"), and every message that reports a search's extent MUST use the same count ("searched the first N addresses", "stopped after the first K addresses"). Throughout this document, "search up to address number N" and "reach N" mean the wallet's first N receive addresses.
- **FR-010**: The default MUST be 500 addresses past the furthest address the wallet already checks or has already searched (whichever is further), up to the maximum. That is the distance the wider search starts with today, and it means the default covers ground not yet searched, except once the maximum has been searched, when the default stays at the maximum. Unlike today's search it does not extend itself when it finds something (FR-012a).
- **FR-011**: The count accepted MUST be a whole number from 1 to 5,000. Anything else MUST be refused before the search starts, with a message stating that range.
- **FR-012**: A search of the first N addresses MUST check every one of them that is not in the wallet's normal range and is not already known to be paid. That includes addresses an earlier search found unpaid, addresses the wallet handed out that are not yet paid, and the 20 addresses the wallet watches after its highest paid one. It MUST NOT stop early because a run of unpaid addresses was met, and a payment the wallet could see before a search MUST still be seen after it.
- **FR-012a**: A search MUST stop at the chosen address number and MUST NOT continue past it for any reason. When a finished search found a paid address within the last 20 addresses of its range, and the range ended below the maximum, the result MUST say that more payments may lie beyond and MUST offer to search further, with a larger number filled in. The person starts that search; it does not start by itself.
- **FR-013**: Every paid address found MUST join the wallet's normal balance, history and ongoing checks, and its funds MUST be spendable with no separate step.
- **FR-013a**: Funds found by a wider search or by the automatic search that follows it (FR-014b) are old payments, not a first payment. Adding a found address to a wallet MUST NOT trigger the wallet's first-payment celebration, at once, while the search is still running, or on any later load. This applies to the wider search as it exists today as well as to the new one.
- **FR-014**: Addresses added by a search MUST still be checked after the wallet is closed and reopened. An address marked as paid is never unmarked or removed by the person or by the wallet, other than by the normal range growing to include it; this feature provides no way to undo a search.
- **FR-014a**: A search MUST add to the wallet's ongoing checks only the addresses it found paid. Unpaid addresses between the wallet's normal range and a paid address found further along MUST NOT be added, so the work done on each refresh grows with the number of addresses found paid or handed out by the wallet, never with how far along they are. This MUST hold whatever address the wallet has on show.
- **FR-014b**: After a search has found paid addresses, the wallet's automatic search MUST also cover the 20 addresses after the highest paid address found, and the addresses the wallet itself handed out beyond its normal range that are not yet known to be paid, so that payments arriving just past the last find, or at an address the person gave out, appear without another search and the hand-out limit moves on as they are paid.
- **FR-014c**: Whenever a search leaves unpaid addresses unchecked (FR-014a), its result MUST tell the person that a later payment to one of those addresses will not appear until they search again.
- **FR-015**: After a search finds a paid address, the wallet MUST NOT hand out as new any receive address at or before the highest paid address found.
- **FR-015c**: Every receive address the wallet itself hands out or sends its own funds to (as when it moves funds off a Digi-ID address) MUST stay in the checked set from then on, whatever is found later and wherever later searches move the wallet's attention. This is the wallet's existing rule (an address it has shown is always checked) carried over to addresses handed out after a far-off find.
- **FR-015b**: The "Your addresses" list on the Receive page MUST list only addresses the wallet checks: from the first address up to the one on show, leaving out any address in between that is not in the checked set. It MUST NOT list an address the wallet is not checking, because the list states whether each address has been used and the wallet does not know that for an address it does not check.
- **FR-015a**: The first time a wider search adds at least one paid address to a wallet, the result MUST include a short notice that addresses from this key appear to be handed out somewhere else and that new addresses made in this wallet may be reused there. The notice MUST be shown once per wallet, MUST NOT block or delay any action, and MUST NOT appear on the Receive page.
- **FR-016**: The wider search MUST show the largest number of addresses any finished search has covered for the current wallet. A later, shorter search MUST NOT lower it.
- **FR-017**: The search MUST cover the same kind of receive address the wider search covers today. Change addresses and other address formats are not added by this feature.

**Progress, stopping and honest results**

- **FR-018**: While a search runs, the wallet MUST show how many of the addresses it has covered so far, out of the count asked for, and how many paid addresses it has found. The figures MUST be updated at least once for every 20 addresses checked.
- **FR-019**: The person MUST be able to cancel a running search, and it MUST stop promptly.
- **FR-020**: A finished search MUST state how many addresses it covered ("the first N addresses"), how many addresses were added and how much DGB they hold, or that none were found among them.
- **FR-021**: A search that was cancelled or could not finish MUST say so, state how far it got, and state that later addresses were not checked. It MUST NOT be presented as "nothing found".
- **FR-022**: Paid addresses confirmed before a search was cancelled or failed MUST join the wallet exactly as they would after a finished search (FR-013, FR-014, FR-015), and MUST be reported in the result. This holds whether the person stopped the search or it failed. A search stopped by FR-023 has checked none of the wallet's addresses, so it has nothing to keep.
- **FR-023**: Before a search checks any of the wallet's addresses, it MUST ask the data source about a control address that cannot have been paid. If the data source reports the control address as paid, the search MUST NOT run, MUST add nothing, and MUST tell the person the answers cannot be trusted. If the control address cannot be checked at all, the search MUST NOT run and MUST report that it could not start (FR-021). A search MUST NOT reject results merely because every address it checked was reported paid.
- **FR-024**: Only one search MUST run at a time for a wallet. Leaving the page or locking the wallet MUST end a running search as if cancelled.

**Boundaries**

- **FR-025**: The wallet's automatic search MUST keep stopping after 20 unpaid receive addresses in a row. For a wallet where no wider search has added an address, this feature MUST NOT change what happens on an ordinary load or unlock. Where one has, the only change is the one FR-014a and FR-014b describe.
- **FR-026**: The rule that the wallet hands out no more than 20 unpaid receive addresses in a row MUST stay as it is.
- **FR-027**: The search MUST use only the wallet's existing data sources and kinds of request. It MUST NOT send the recovery phrase, any private key or the account's public key anywhere.
- **FR-027a**: Before a search is started, the wider search MUST show a short note saying that it checks up to the chosen number of this wallet's addresses with the public block explorer. The note MUST be visible without any action, MUST name the number chosen, and MUST NOT add a confirmation step. It is shown whichever data source the wallet normally reads from, because the search always asks the explorer.
- **FR-028**: All new wording MUST be available in English and Spanish through the wallet's existing translation mechanism.

### Key Entities

- **Empty-restore offer**: The message on Home for one wallet. It knows the wallet's origin (created here, restored, added as watch-only, or unknown for seed wallets that predate this feature), whether a completed load found nothing, and whether the person has dismissed it or a wider search has finished for the wallet.
- **Search reach**: How many of the wallet's receive addresses a wider search covers, counting from the first. Chosen by the person, with a default and a maximum. The furthest count any finished search has covered is remembered per wallet.
- **Search result**: What one run reports: finished, cancelled, failed, refused because the source could not be trusted, or nothing to check; how many addresses it covered; the addresses added; the DGB they hold; whether a payment was found close enough to the end of the range to suggest searching further; and, the first time addresses are added for a wallet, the notice that the key appears to be used elsewhere.
- **Checked addresses**: The set of receive addresses a wallet looks at on every load: its normal range (the first 20 addresses and every address up to the last paid one the automatic search has found), each individual paid address a wider search found beyond that range, each address the wallet itself has handed out beyond that range, and the 20 addresses after the highest paid one found. It is not one unbroken run. A search can only add to it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A person who restores a wallet whose only payments sit at addresses 60 and 61 sees those funds in their balance within 2 minutes of the wallet finishing its first load, using only what Home shows them, without being told where the Recovery page is.
- **SC-002**: A payment at any receive address up to number 5,000 can be found and spent from the wallet, however many unpaid addresses come before it.
- **SC-003**: A search across the full 5,000 addresses finishes within 10 minutes on an ordinary home connection when the data source is responding normally.
- **SC-004**: A cancelled search stops within 5 seconds.
- **SC-005**: In every case where a search does not finish (cancelled, connection lost, data source refusing), the person is told it is incomplete and how far it got; in none of them is "nothing found" shown.
- **SC-006**: No wallet created after this feature ever shows the offer, and no wallet shows it again after it has been dismissed.
- **SC-007**: Loading and unlocking a wallet that has never used the wider search takes no longer and makes no more requests than before this feature.
- **SC-007a**: After a search finds two paid addresses near number 3,000, a balance refresh takes no more than 5 seconds longer than it did before the search.
- **SC-008**: Funds found by a search are still in the balance after the wallet is closed and reopened, in 100% of cases.

## Assumptions

- "Restored or added" means a wallet brought in from a recovery phrase or as watch-only from an account public key. Checked on 2026-11-24: the wallet records a wallet's type (seed or watch-only) but not whether a seed wallet was created here or restored. This feature starts recording that (FR-002a), and existing wallets follow FR-002b.
- The wallet does not show a number against its addresses anywhere, so the count in FR-009 is the only numbering a person meets. Someone comparing with an outside issuer that shows a "next index" can type that figure as the count: the first 24 addresses are the ones an issuer calls indices 0 to 23.
- "First complete load" is the first load whose history check finished successfully, the same notion the wallet already uses to decide a wallet is empty.
- The offer lives on Home's empty state. Activity is unchanged. Receive changes only in what its address list contains (FR-015b): today that list is every address from the first to the one on show, which after a far-off find would be thousands of addresses the wallet never handed out and is not checking.
- Following the offer opens the wider search on the Recovery page with the default reach filled in. The person presses start; the search does not begin on its own, because it sends many address lookups to the data source.
- 5,000 is the maximum because it is the largest address range the known outside issuer uses for one key. A larger range would be a later change.
- The search checks addresses the same way the existing wider search does: it asks the public block explorer about each address, including for wallets that normally read through their own node or the light-client gateway. It sends no key and nothing but addresses, but a long search sends many of the wallet's addresses to one explorer in a short time, which lets that explorer link them. The note in FR-027a says so before the search starts.
- The 10-minute target assumes the public explorer answers at its usual speed. Measured on 2026-11-24: 300 address lookups, 10 at a time, took 20 seconds with no errors, which projects to about 5.6 minutes for 5,000. That was one run from one desktop connection, not from inside the wallet. If it limits requests, the search ends as incomplete and can be run again.
- A search that follows an incomplete one may check again the addresses the incomplete one covered. The furthest search remembered (FR-016) counts finished searches only.
- The control address (FR-023) is one the wallet can be certain has no history. How it is chosen is a planning matter; it must not be an address of the wallet being searched, and checking it must not reveal anything about that wallet.
- Change addresses do not need a wider search: only the wallet itself hands those out.
- The rule that the wallet hands out at most 20 unpaid addresses in a row is unchanged (FR-026). What moves, after a far-off find, is where that run is counted from and where the next new address starts (FR-015).
- Found addresses are permanent (FR-014). Undoing a search that added addresses wrongly is done by removing the wallet from the device and restoring it, which needs the recovery phrase or, for a watch-only wallet, the account public key. No funds are lost by that: the wallet's checks are rebuilt from the chain. Payments beyond the automatic search's reach will again need a wider search to be seen, and things kept only on the device, such as address labels, go with the wallet.
- Out of scope: a way to remove or reset addresses found by a search; changing the automatic limit of 20; changing the 20-unpaid hand-out limit; anything in the outside issuer; multi-signature wallets; DigiDollar addresses; older address formats; searching change addresses; any server change.
