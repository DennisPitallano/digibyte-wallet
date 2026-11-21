# Feature Specification: Fewer Requests per Refresh and per Send

**Feature Branch**: `feature/tiered-refresh`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "Fewer explorer requests per refresh and per send (follow-up to spec 023). After spec 023, a quiet refresh still reads one address summary for every watched address (about 60 for a seed wallet), and Home polls every 15 s (backing off to 60 s). Sending reads unspent outputs for every watched address (about 60 requests at confirm time). Improvements: (1) tiered refresh: each refresh checks only the addresses that can plausibly change; unused addresses are swept less often (every few minutes and once per unlock); (2) block-driven refresh: each poll reads the tip height first, and with no new block and nothing pending only the hot addresses are checked; (3) send reads unspent outputs only for addresses whose current summary shows a balance or pending activity, still read fresh at send time. Constraints: a balance is never shown too low because some lookups failed or because an address was skipped; coins for a send are always read fresh; a send still refreshes the balance at once; the developer switch can turn tiering off; the benchmark shows request counts with tiering on and off. Out of scope: a server-side batch endpoint, push notifications, changes to the light-client gateway."

## Context

Spec 023 (released in 0.7.0) made a quiet refresh read one address summary per watched address and nothing else. A seed wallet watches about 60 addresses, most of which have never been used: spare receive and change addresses kept ahead for the gap limit, and older-format copies kept for compatibility. Home refreshes every 15 seconds (backing off to 60 seconds when nothing changes), so a wallet left open still sends about 60 requests a minute or more, almost all of them about addresses that hold nothing and have never been used.

Sending has the same shape: when the user confirms a send, the wallet reads the unspent coins of every watched address (about 60 requests at the moment the user is waiting), although the summaries already say which few addresses hold money.

This feature makes a refresh ask only about the addresses that can plausibly change, checks the others less often, and makes a send read coins only where there is money.

The request also proposed a block-driven refresh (read the block height first and do a full pass only when a new block arrives). It is left out: DigiByte produces a block about every 15 seconds, the same as Home's fastest poll, so nearly every refresh would see a new block and do the full pass, cancelling the gain from tiering. Cold addresses are checked on a timer instead (FR-004). Correctness rules from earlier features stay in force: a lookup that fails is never counted as empty, coins for a send are always read fresh, and a send refreshes the balance at once.

## Clarifications

### Session 2026-11-22

- Q: How long may a payment to an old, unused address take to show up before a manual refresh? → A: 5 minutes (the cold-check interval); unused addresses are checked in groups spread over it.
- Q: Where should the wallet say when every address was last fully checked? → A: In the balance details (the info panel opened from the balance card) only, not on Home's main card.
- Q: Should tiering have its own developer switch or reuse the change-only sync switch? → A: Its own switch, "Tiered refresh", independent of "Change-only history sync".
- Q: With change-only sync off and tiered refresh on, does tiering still apply? → A: No. Tiering works only on top of change-only sync; with change-only off, every refresh and send reads every address as in 0.6.0.
- Q: Is the background refresh when the user opens Activity, Receive or Send tiered or a full check? → A: Tiered, like Home's timer. Only pull-to-refresh and the refresh button count as user-started full checks.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A wallet left open costs little (Priority: P1)

A user keeps the wallet open on Home. Nothing happens on their addresses. Each automatic refresh confirms the balance by asking only about the handful of addresses that could have changed, not every watched address.

**Why this priority**: This is the common case for every user with the app open, and it is where most of the remaining requests go. It also reduces the chance of hitting the explorer's rate limit and seeing "Unable to load".

**Independent Test**: With a seed wallet of about 60 watched addresses, 2 of them holding funds, run a quiet refresh against a counting test double and against the `/dev/node` benchmark. Count the requests: about the number of hot addresses plus at most one block-height read, not one per watched address. The balance equals the previous refresh.

**Acceptance Scenarios**:

1. **Given** a wallet whose addresses were all read in this session less than 5 minutes ago and nothing has changed, **When** an automatic refresh runs, **Then** only the hot addresses (see FR-002) are looked up, and the balance is unchanged.
2. **Given** some cold addresses were last looked up 5 or more minutes ago, **When** the next automatic refresh runs, **Then** it also looks up those addresses.
3. **Given** the developer switch for tiering is off, **When** a refresh runs, **Then** every watched address is looked up, as in 0.7.0.

---

### User Story 2 - Payments still show up (Priority: P1)

Someone pays the user. If the payment goes to an address the wallet treats as hot (the address on show, a recently handed-out address, one with funds), it appears on the next refresh, as today. If it goes to an old, unused address, it appears within a few minutes.

**Why this priority**: Skipping addresses is only acceptable if no payment can be missed for long. This story carries the safety half of the speed-up.

**Independent Test**: In the test double, pay the address on show and check that the next refresh shows it. Pay an unused address far down the list and check that it shows within the cold-check interval, and at once after a manual refresh.

**Acceptance Scenarios**:

1. **Given** the address on show on Receive (or the latest handed-out address), **When** it receives a payment, **Then** the next refresh shows it, pending or confirmed.
2. **Given** an unused address outside the hot set, **When** it receives a payment, **Then** the wallet shows it no later than the cold-check interval (FR-004) after it was made.
3. **Given** the user pulls to refresh or taps refresh, **When** the refresh runs, **Then** every watched address is looked up, so a manual refresh always shows everything.
4. **Given** the wallet is unlocked, **When** the first refresh of the unlock runs, **Then** every watched address is looked up.
5. **Given** a payment arrives to an address that was cold, **When** the wallet sees it, **Then** that address becomes hot from then on.

---

### User Story 3 - Sending reads only the coins it needs (Priority: P2)

A user confirms a send. The wallet reads the unspent coins of the addresses that hold money or have pending activity, instead of every watched address, so the send is quicker and less likely to be slowed by a rate limit.

**Why this priority**: It is a smaller number of events than refreshes, but it is the moment the user is waiting, and a rate limit there makes the send fail.

**Independent Test**: With a wallet of about 60 watched addresses, 2 holding funds, confirm a send against a counting test double. Count the coin lookups: 2, not 60. The transaction is the same as before.

**Acceptance Scenarios**:

1. **Given** current summaries show which addresses hold confirmed funds or pending activity, **When** a send is confirmed, **Then** coins are read only for those addresses, and read fresh.
2. **Given** the coins found that way are not enough for the amount and fee, **When** the send is built, **Then** the wallet reads the coins of every remaining watched address before reporting insufficient funds.
3. **Given** summaries are not available (regtest, the gateway first, either switch off, or no current summary), **When** a send is confirmed, **Then** coins are read for every watched address, as today.

---

### User Story 4 - Developers can see the difference (Priority: P3)

A developer opens `/dev/node`, runs the wallet refresh benchmark, and sees the request counts and times with tiering on and off, and how many coin lookups a send makes.

**Why this priority**: It is how the gain is proven and how a regression would be noticed, but it does not change anything a user sees.

**Independent Test**: Run the benchmark with an unlocked wallet; check that it shows a quiet refresh with tiering on and off, with the requests by kind.

**Acceptance Scenarios**:

1. **Given** an unlocked wallet, **When** the benchmark runs, **Then** it shows the time and the requests by kind for a quiet refresh with tiering on and with tiering off.
2. **Given** the tiering switch on `/dev/node`, **When** it is turned off, **Then** the next refresh and send behave as in 0.7.0.

---

### Edge Cases

- **A hot address lookup fails**: the refresh fails as today (the balance read fails, the screen keeps the last known figure with "last updated"). A failed lookup is never counted as empty or as unchanged.
- **A cold address was last read too long ago**: its summary is not reused; it is looked up in the refresh (FR-005).
- **A reorganisation**: summaries of skipped addresses are at most 5 minutes old, and the once-per-unlock full history re-read from spec 023 still corrects moved transactions.
- **A send spends from an address the summaries show as empty**: cannot happen with correct summaries; if summaries were stale, FR-010 (read the rest before "insufficient funds") covers it.
- **A pending outgoing send**: the addresses it spends from and its change address stay hot until it confirms.
- **The user changes wallet**: each wallet keeps its own hot set and check times; switching never reuses another wallet's.
- **Lock and unlock**: the first refresh after unlock is a full one (FR-006).
- **Watch-only, single-key and multisig wallets**: single-key wallets have 2 addresses and multisig 1, so tiering makes no difference; they are not tiered. Watch-only seed wallets are tiered like seed wallets.
- **Discovery finds a newly used address**: it joins the hot set.

## Requirements *(mandatory)*

### Functional Requirements

**Tiered refresh**

- **FR-001**: A refresh MUST look up only the hot addresses (FR-002) plus any addresses that are due (FR-004 to FR-006). Addresses not looked up keep the summary read for them earlier in this session.
- **FR-002**: The hot set MUST include: addresses with a confirmed balance or pending activity in their latest summary; the receive address on show and the 3 newest handed-out receive addresses not yet used; the next change address; the addresses spent from by a pending send of the wallet; and addresses with any activity in the last 7 days.
- **FR-003**: An address MUST join the hot set as soon as a lookup, a discovery read or a send shows activity on it.
- **FR-004**: Every watched address outside the hot set MUST be looked up at least once every 5 minutes (the cold-check interval). The cold addresses due in a refresh are looked up in that refresh.
- **FR-005**: A summary for an address not looked up in a refresh MAY be reused only if it was read live from the source in this session and no longer ago than the cold-check interval. Otherwise the address MUST be looked up in that refresh. A summary loaded from storage MUST NOT be reused.
- **FR-006**: The first refresh after unlock, a refresh the user starts (pull to refresh or the refresh button), and the first refresh after a send MUST look up every watched address. All other refreshes are automatic and tiered, including Home's timer and the background refresh when Activity, Receive or Send is opened.
- **FR-007**: A refresh MUST NOT show a balance that is lower than the sum of the latest summaries because some address was skipped. Any lookup that fails MUST fail the balance read, as today.
- **FR-008**: The wallet MUST be able to say when every watched address was last confirmed from the chain ("Fully checked N min ago"), shown only in the balance details (the info panel opened from Home's balance card), never on the main card. With tiering off it shows the time of the last refresh.

**Spreading the cold checks**

- **FR-009**: The cold addresses MUST be checked in small groups spread over the 5-minute interval rather than all in one refresh, so that no single automatic refresh looks up much more than the hot set plus about a fifth of the cold addresses.

**Sending**

- **FR-010**: When a send is built and current summaries exist, the wallet MUST read coins only for the addresses whose summary shows a confirmed balance or pending activity. The coins MUST be read fresh at that moment. If they are not enough for the amount and fee, the wallet MUST read the coins of every remaining watched address before reporting insufficient funds.
- **FR-011**: When current summaries are not available (regtest, the gateway first, either switch off), a send MUST read coins for every watched address, as today.
- **FR-012**: A send MUST still refresh the balance at once afterwards, with every watched address looked up (FR-006).

**Switch and measurement**

- **FR-013**: A separate "Tiered refresh" switch on `/dev/node`, independent of the "Change-only history sync" switch, MUST be able to turn tiering off. When off, every refresh looks up every watched address and every send reads every address's coins, as in 0.7.0. The switch is kept per device, on by default, and takes effect on the next refresh. Tiering applies only while change-only history sync is also on; with change-only off, every refresh and send reads every address, whatever this switch says.
- **FR-014**: The `/dev/node` wallet refresh benchmark MUST show a quiet refresh with tiering on and off, with time and requests by kind, and the number of coin lookups a send would make, all kept in memory on that page only.

**Unchanged guarantees**

- **FR-015**: Coins used to build and sign a transaction MUST always be read fresh from the source at the time of the send.
- **FR-016**: Wallet types with fewer than 10 watched addresses (single-key, multisig) MUST NOT be tiered.
- **FR-017**: Nothing new is sent to any server, and no new data is stored outside the wallet's existing on-device storage.

### Key Entities

- **Hot set**: per wallet and network, the watched addresses looked up on every refresh, with why each is in it (balance, pending, on show, handed out, next change, recent activity) and since when.
- **Check times**: per watched address, when it was last looked up live in this session; and per wallet, when every address was last looked up ("fully checked").

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a seed wallet of about 60 watched addresses with funds on 2 of them and no new activity, a quiet automatic refresh in which no cold address is due sends at most 12 requests (the hot addresses plus at most one block-height read), down from about 60, as shown by the `/dev/node` benchmark and the automated tests.
- **SC-002**: Over 10 minutes with the wallet open on Home and no activity, the number of explorer requests drops by at least 60% with tiering on compared with off, at both the fastest (15 s) and the slowest (60 s) automatic refresh pace.
- **SC-003**: A payment to a hot address shows within one refresh; a payment to any other watched address shows within 5 minutes, and at once after a manual refresh.
- **SC-004**: Confirming a send from a wallet with funds on 2 of about 60 addresses makes at most 4 coin lookups, down from about 60.
- **SC-005**: Across the test suite's failure cases (timeouts, rate limits, stale summaries), no refresh ever shows a total lower than the sum of the latest summaries, and no send reports insufficient funds while another watched address holds enough.

## Assumptions

- Spec 023 is in place: address summaries, the in-session summary snapshot, change-only history sync, the shared tip lookup, and the `/dev/node` switch and benchmark. This feature builds on them and keeps their rules unless stated here.
- 5 minutes is an acceptable delay for a payment to an old, unused address, given that the address on show and recently handed-out addresses are always hot, and a manual refresh checks everything.
- 7 days of recent activity is a reasonable window for keeping a used address hot; used addresses with no balance and no recent activity rarely receive again.
- The cold-check interval (5 minutes, confirmed in clarification) and the recent-activity window are fixed in this feature; they can become settings later if needed.
- Out of scope: a server-side batch endpoint, push or subscription updates, changes to the light-client gateway, changes to address discovery, and changes to the 0.7.0 history sync beyond deciding which addresses' summaries are read in a refresh.
