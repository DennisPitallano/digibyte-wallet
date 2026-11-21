# Feature Specification: Faster Balance and History

**Feature Branch**: `feature/faster-balance-history`

**Created**: 2026-11-21

**Status**: Draft

**Input**: User description: "Faster balance and transaction history retrieval. Today an HD wallet scans ~80 addresses (receive SegWit + legacy, change) and every refresh makes one explorer request per address for balance and another per address for history, so loads are slow and hit rate limits. Improvements: (1) reuse one cached chain tip height per history load instead of fetching it once per address; (2) concurrent identical reads share one in-flight request, and history is cached per address rather than per page size; (3) delta sync: keep a saved per-address summary (transaction counts, funded, spent, pending), compute the balance from it, and only fetch history for addresses whose counts changed, merging into saved history, including history past the explorer's 25-newest page and the pending balance; (4) the SPV gateway answers wallet-wide history in one pass instead of re-reading every transaction per address; (5) show saved balance and history instantly on all wallet pages and refresh in the background. Constraints: never show a balance that is too low because some lookups failed; coins used for signing stay uncached; sending still refreshes the balance immediately. Out of scope: a new server-side batch endpoint or a push-based backend. Success measured with the existing explorer vs SPV gateway benchmark on /dev/node."

## Context

A seed wallet watches about 80 addresses: receive and change addresses in the current format, plus older-format addresses kept for compatibility. The public block explorer the wallet reads from answers one address at a time. Today every balance refresh asks about every address once, every history refresh asks about every address again, and each history answer also asks for the current block height. One Home refresh can send well over 200 requests. That makes loading slow on mobile networks and makes the explorer's rate limits more likely, and a rate limit makes the read fail, so the user sees "Unable to load".

This feature cuts the number of requests a refresh needs, especially when nothing has changed. It also shows the last known figures straight away on every wallet page. Correctness rules from earlier features stay in force: a balance is never shown lower than it is because some lookups failed (fix from PR #93), and the coins chosen for a send are always read fresh.

## Clarifications

### Session 2026-11-21

- Q: When should the wallet do its full re-read of every address's history (the reorganisation guard)? → A: Once per unlock, in the background, after the change-only result is shown, at low priority so it never delays what the user is looking at.
- Q: How should the saved address summaries and history be protected on the device? → A: The same way as today's cached Home balance and history: on-device app storage, per wallet and network, deleted with the wallet; no extra encryption.
- Q: Should there be a switch to turn change-only sync off? → A: Yes, a developer setting on `/dev/node`, on by default; turning it off makes every refresh read every address's history, as today.
- Q: Should multisig wallets get the same speed-ups? → A: Only the shared read improvements (one block-height read per load, shared in-flight reads, complete history past one page). Change-only sync and instant saved display on multisig pages are out of scope.
- Q: Should the main balance figure on Home include pending amounts? → A: No. The main figure (and its fiat value) is the confirmed balance only; a separate line shows the net pending amount ("Pending +X DGB" or "Pending −X DGB") when it is not zero.
- Q: Should the `/dev/node` benchmark show request counts as well as timing? → A: Yes. For each refresh it shows the time and the number of address summary, history and block-height requests, with change-only sync on and off. Counts are kept in memory on that page only.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Refreshing a wallet with no new activity is fast (Priority: P1)

A user opens the wallet, or Home refreshes on its timer, and nothing has happened on any of their addresses since the last refresh. The balance and history confirm quickly, and the wallet asks the explorer only what it needs to learn that nothing changed.

**Why this priority**: Most refreshes find no new activity, so this is where most of the time and rate-limit pressure goes. Fixing it gives the largest improvement to the most users.

**Independent Test**: With a wallet whose saved summary is current, trigger a refresh against a counting test double of the explorer. Count the requests: one summary lookup per watched address, no history lookups, and at most one block-height lookup. The balance and history shown match the previous refresh.

**Acceptance Scenarios**:

1. **Given** a wallet whose saved address summaries match the chain, **When** the balance and history refresh together, **Then** each watched address is looked up once in total (not once for the balance and once for the history), and no address's transaction list is fetched.
2. **Given** a history load across all watched addresses, **When** confirmations are worked out, **Then** the current block height is read at most once for the whole load, not once per address.
3. **Given** two parts of the app ask for the same address data at the same moment (for example the balance card and a poller), **When** neither has an answer yet, **Then** only one request goes to the explorer and both get its answer.
4. **Given** pages that ask for different amounts of history (Home, Send, Receive, Transactions), **When** they load one after another within the cache lifetime, **Then** each address's history is fetched from the explorer at most once.

---

### User Story 2 - New activity is picked up without reloading everything (Priority: P1)

A payment arrives at one of the user's addresses, or a pending transaction confirms. On the next refresh the wallet notices which addresses changed, fetches the history for just those addresses, and shows the new balance and the new or updated transaction.

**Why this priority**: Skipping unchanged addresses (Story 1) is only safe if changes are always noticed. This story carries the correctness half of the speed-up.

**Independent Test**: Start from a saved, current summary. In the test double, add a transaction to one address. Refresh and check that exactly that address's transaction list is fetched, the new transaction appears, and the balance includes it.

**Acceptance Scenarios**:

1. **Given** a saved summary, **When** one address receives a new payment, **Then** the next refresh fetches the transaction list for that address only, and the payment appears in the history with the correct amount and direction.
2. **Given** an incoming payment that is still pending, **When** it confirms, **Then** the next refresh notices the change even though the address's total transaction count did not change, and the transaction's confirmation status updates.
3. **Given** a payment arrives at an address being shown in the wallet for the first time (a newly discovered or newly handed-out address), **When** the wallet refreshes, **Then** that address's history is fetched as part of the refresh.
4. **Given** the user has just sent a transaction, **When** the send completes, **Then** the balance and history refresh straight away and reflect the send, without waiting for any cache to expire.

---

### User Story 3 - Busy addresses show their complete history (Priority: P2)

A user whose address has many transactions (for example a reused donation or merchant address) opens their history and sees all of it, not only the newest page the explorer returns by default.

**Why this priority**: Today an address with more than 25 confirmed transactions silently shows only the newest 25. That is a correctness gap, but it affects fewer users than the speed problems.

**Independent Test**: In the test double, give one address 60 confirmed transactions. Load history and check that all 60 appear, then refresh and check that the older ones are not fetched again.

**Acceptance Scenarios**:

1. **Given** an address with more transactions than the explorer returns in one page, **When** its history is first loaded, **Then** the wallet keeps fetching older pages until it has every transaction for that address.
2. **Given** an address whose older history is already saved, **When** one new transaction arrives, **Then** only the new transaction is fetched, not the address's full history again.

---

### User Story 4 - Incoming payments show as pending straight away (Priority: P2)

Someone sends the user DGB. Before it confirms, the wallet shows it as a pending amount next to the balance, separate from the confirmed balance that can be spent.

**Why this priority**: The data is already in the address summaries the wallet reads, so this is nearly free once Story 1 is in place, and it answers a common "did my payment arrive?" question.

**Independent Test**: In the test double, add an unconfirmed incoming payment. Refresh and check that the pending amount shows, the spendable amount does not include it, and the pending amount clears once the payment confirms.

**Acceptance Scenarios**:

1. **Given** an unconfirmed incoming payment, **When** the balance refreshes, **Then** it is shown as pending and is not counted in the amount available to send.
2. **Given** an unconfirmed outgoing transaction the user made, **When** the balance refreshes, **Then** the pending figure reflects the outgoing amount, and the available amount does not offer coins that transaction already spends.
3. **Given** a pending payment confirms, **When** the balance next refreshes, **Then** it moves from pending into the confirmed balance.

---

### User Story 5 - Wallet pages show the last known figures immediately (Priority: P2)

A user opens Transactions, Receive or Send. Instead of a loading placeholder, the page shows the balance and history from the last successful refresh at once, then updates quietly when the fresh answer arrives.

**Why this priority**: Home already does this. Extending it to the other pages makes the whole app feel faster even when the network is slow, at little risk.

**Independent Test**: Load history once, then open each page with the explorer test double set to answer slowly. Check that saved figures show at once and are replaced when the slow answer arrives.

**Acceptance Scenarios**:

1. **Given** a previous successful refresh for this wallet, **When** the user opens Transactions, Receive or Send, **Then** the saved balance and history show at once while a refresh runs in the background.
2. **Given** the background refresh fails, **When** saved figures are on screen, **Then** they stay, with a clear note of when they were last updated, and no figure is replaced by zero or by a partial result.
3. **Given** the user switches wallets, **When** the new wallet's page loads, **Then** only that wallet's saved figures show, never another wallet's.
4. **Given** the Send page is open, **When** the user confirms a send, **Then** the coins chosen for it are read fresh at that moment, never taken from saved figures.

---

### User Story 6 - Gateway users get history without heavy processing (Priority: P3)

A user who has turned on the SPV gateway opens their history. The wallet builds the history from its own saved, checked record in one pass, so it appears quickly even for wallets with many addresses and transactions.

**Why this priority**: Only users who have the gateway turned on are affected, but for them history loading work grows with addresses × transactions.

**Independent Test**: With a gateway record of 80 addresses and 200 transactions, load the wallet's history and check that each transaction is read once for the whole load, and that the result matches the result built address by address.

**Acceptance Scenarios**:

1. **Given** the gateway is answering history, **When** the wallet loads history for all its addresses, **Then** each saved transaction is processed once for the whole load, not once per address.
2. **Given** the same wallet, **When** history comes from the one-pass method, **Then** it lists the same transactions, amounts, directions and verification labels as the current address-by-address method.

---

### Edge Cases

- **Some lookups fail during a refresh**: if any watched address cannot be read, the balance read fails as it does today and the screen keeps the last known figure with "last updated" information. A failed lookup is never treated as "unchanged" or "empty", and the saved summary for that address is left as it was.
- **Pending transaction confirms**: the total transaction count stays the same while the pending count goes down and the confirmed count goes up. The change check compares confirmed and pending counts separately, so this still counts as a change.
- **Pending transaction dropped or replaced**: the pending count changes, so that address is re-read, and a transaction that is no longer known is removed from pending or marked as not confirmed, never left looking pending forever.
- **Chain reorganisation**: a transaction can move to a different block, or leave the chain, without the address's counts changing. A full re-read of every address's history runs in the background once per unlock (FR-009), so a stale saved history cannot last longer than one session, and the first refresh after unlock is still change-only.
- **Explorer switch**: the wallet falls back to a different explorer or source part-way through a session. Summaries saved from one source are compared with answers from another only on the published figures (counts and amounts), which are the same whichever explorer answers.
- **Very large address**: an address with hundreds of transactions is fetched page by page on first load, without blocking the balance or the rest of the history from showing.
- **First run or cleared storage**: with nothing saved, the wallet behaves as it does today (full read), then saves the summary for next time.
- **Saved data unreadable or from an older version**: it is discarded and a full read is done; it never causes a wrong figure.
- **Several wallets on one device**: saved summaries and history are kept apart per wallet and per network.
- **Watch-only and single-key wallets**: the same rules apply to them; they have fewer addresses, so the gain is smaller.
- **Multisig wallets**: they get the shared read improvements (FR-001 to FR-003, FR-012) because they read through the same sources, but not change-only sync or instant saved display (see Assumptions).

## Requirements *(mandatory)*

### Functional Requirements

**Fewer requests per refresh**

- **FR-001**: A history load across all watched addresses MUST read the current block height at most once and use that value for every address's confirmation counts.
- **FR-002**: When several callers ask for the same data while a request for it is already in flight, they MUST all receive that request's answer instead of starting new requests.
- **FR-003**: Cached history for an address MUST be shared by every caller, whatever number of transactions each caller asks for. Paging and trimming happen after the cached data is read.
- **FR-004**: When the balance and the history refresh together, each watched address's summary MUST be read from the source at most once for both.

**Change-only history (delta sync)**

- **FR-005**: The wallet MUST keep, per wallet and per network, a saved summary for each watched address: confirmed transaction count, pending transaction count, confirmed amount received, confirmed amount spent, pending amount received and pending amount spent.
- **FR-006**: The wallet MUST compute the balance from current summaries. A summary counts as current only if it was read live from the source within the last 30 seconds in this session (including reads made by address discovery), never one loaded from storage; a lookup that failed MUST fail the balance read as it does today, never fall back to the saved figure for that address in a new total.
- **FR-007**: On a refresh the wallet MUST fetch an address's transaction list only when its confirmed or pending count differs from the saved summary, when the address has no saved summary, or when the saved history for it is missing.
- **FR-008**: Fetched transactions MUST be merged into the saved history for that wallet. A transaction already saved MUST be updated (confirmations, block, status), not duplicated.
- **FR-009**: The wallet MUST re-read the full history of every watched address once per unlock, so a change the summaries cannot show (such as a reorganisation) is corrected within one session. This re-read MUST run in the background after the change-only result is on screen, MUST NOT delay any balance or history the user is waiting for, and MUST yield to the user's own reads when both need the source at the same time. If it fails, it is retried on the first refresh at least 1 minute after the failure, in the same session; a failure MUST NOT change what is on screen.
- **FR-010**: The saved summary for an address MUST be updated only after its transaction list (when needed) has been fetched and merged successfully. A refresh that fails part-way MUST leave unchanged the summaries of addresses it did not finish.
- **FR-010a**: A developer setting on `/dev/node`, on by default, MUST turn change-only sync on or off. When off, every refresh MUST read every watched address's full history as today, while still reading the block height once per load and sharing in-flight requests (FR-001 to FR-004). The setting is kept per device and takes effect on the next refresh.
- **FR-010b**: The `/dev/node` benchmark MUST show, for each balance-and-history refresh it runs, the time taken and the number of requests by kind (address summary, transaction history pages, block height), and MUST be able to run the same refresh with change-only sync on and off. The counts are kept in memory on that page only and MUST NOT be stored, logged or sent anywhere.
- **FR-011**: Address discovery MUST store the summaries it already reads, so the first balance and history refresh after discovery does not read the same addresses again.

**Complete history**

- **FR-012**: When an address has more transactions than one page of the source's answer, the wallet MUST fetch older pages until it has the address's full history.
- **FR-013**: After an address's full history is saved, later refreshes MUST fetch only transactions newer than the saved ones, plus any pending ones.

**Pending balance**

- **FR-014**: The balance MUST report a pending amount, the net of pending incoming and pending outgoing amounts, separately from the confirmed balance.
- **FR-015**: The amount offered as available to send MUST be the confirmed balance plus any negative net pending amount (confirmed + min(0, net pending)). Pending incoming amounts MUST NOT be offered for spending, and coins already spent by a pending send MUST NOT be offered again. The coins actually chosen for a send are still read fresh (FR-022).
- **FR-016**: Home's main balance figure and its fiat value MUST be the confirmed balance only. When the net pending amount is not zero, Home MUST show it on a separate line labelled as pending, with its sign ("Pending +X DGB" or "Pending −X DGB"). A pending amount that is dropped MUST disappear from that line without the main figure changing.

**Instant display**

- **FR-017**: Transactions, Receive and Send MUST show the active wallet's last saved balance and history at once, when saved data exists, and refresh in the background.
- **FR-018**: When figures shown come from saved data and the latest refresh failed, the page MUST say when they were last updated.
- **FR-019**: Saved values shown under FR-017 MUST NOT be used for the coins chosen for a send, and after a send a value from before that send MUST NOT be shown as current.

**Gateway**

- **FR-020**: When the SPV gateway answers history, the wallet MUST be able to get the history for all its watched addresses in one pass over the saved record, processing each transaction once.
- **FR-021**: The one-pass gateway history MUST give the same transactions, amounts, directions and verification labels as the current address-by-address answer.

**Unchanged guarantees**

- **FR-022**: The coins used to build and sign a transaction MUST always be read fresh from the source at the time of the send, as today.
- **FR-023**: After a send, all cached and saved balance and history figures for that wallet MUST be treated as out of date, so the next read reflects the send.
- **FR-024**: If no source can answer, pages MUST keep working in the current degraded mode (saved figures, "Unable to load" with Retry where nothing is saved), and MUST NOT show a zero or partial balance as if it were current.
- **FR-025**: Saved summaries and history MUST be kept in the same on-device storage, with the same protection, as today's cached Home balance and history (no added encryption), separately for each wallet and network, and removed when the wallet is removed. They MUST NOT be sent to any server.

### Key Entities

- **Address summary**: what the wallet last learned about one watched address: confirmed and pending transaction counts, confirmed and pending amounts received and spent, when it was read, and which wallet and network it belongs to. It is used to compute the balance and to decide whether the address's history needs fetching.
- **Saved history**: the merged list of a wallet's transactions, each with its id, amounts, direction, confirmations or block, pending status, and the wallet addresses involved. For each address it also records how much of that address's history has been fetched (complete, or up to which transaction).
- **Wallet balance**: confirmed amount (the figure shown as the balance), net pending amount (shown separately, never added into the main figure), and when it was last confirmed from the chain.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a typical seed wallet (about 80 watched addresses) with no new activity, a combined balance and history refresh sends at most one request per watched address plus one block-height request, down from more than three requests per address today, as shown by the request counts on the `/dev/node` benchmark (FR-010b) and by the automated tests.
- **SC-002**: On the `/dev/node` benchmark, a repeat refresh of the same wallet (warm saved summary, no new activity) completes at least 50% faster with change-only sync on than with it off (FR-010a).
- **SC-003**: When one address receives a payment, the next refresh fetches transaction history for that address only, and the payment shows on Home within one refresh cycle.
- **SC-004**: An address with 60 confirmed transactions shows all 60 in the wallet's history (today: 25).
- **SC-005**: When saved data exists, Transactions, Receive and Send show a balance and history within 1 second of opening, even when the network is slow.
- **SC-006**: Across the test suite's failure cases (timeouts, rate limits, some addresses failing), no refresh ever shows a total lower than the true balance as current.
- **SC-007**: For a gateway wallet with 80 addresses and 200 transactions, loading history processes each transaction once instead of once per address.

## Assumptions

- The main explorer reports, for each address, separate confirmed and pending transaction counts and amounts, and lets older transactions be fetched page by page. The public DigiByte Esplora instance does this. A source that cannot report counts (such as the own node) keeps today's behaviour for that read.
- Address summaries and history are not secrets, so they can use the same on-device storage as today's cached Home balance and history. They are still private, so they are never sent off the device (FR-025).
- A background full re-read once per unlock is a sufficient guard against reorganisations, given DigiByte's confirmation depths. Users who want a fresh full read sooner can use the existing scan tools.
- The cache lifetimes used today (about 30 seconds for balance and history, 15 seconds for block height) stay as they are; this feature changes what is fetched, not how long answers are trusted.
- Pending amounts only cover transactions involving the wallet's own watched addresses, as reported by the source.
- Measuring success uses the existing explorer vs SPV gateway benchmark on `/dev/node`, extended to run a second, warm refresh and to show request counts (FR-010b).
- Out of scope: a new server-side batch endpoint, a push or subscription backend (Electrum-style), changes to the own-node API, changes to address discovery's gap limits, and change-only sync or instant saved display for multisig wallet pages (each multisig has one address, so the gain is small). These are possible follow-ups.
