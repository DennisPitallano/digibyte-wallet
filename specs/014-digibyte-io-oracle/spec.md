# Feature Specification: Oracle price and DigiDollar status from digibyte.io

**Feature Branch**: `feature/digibyte-io-oracle`

**Created**: 2026-11-16

**Status**: Draft

**Input**: User description: "Oracle price and DigiDollar status in the wallet from the public digibyte.io API (explorer backend). Fill the oracle gap only: show the oracle price and status panel again using digibyte.io. Display only. If digibyte.io is unreachable, slow or returns something unusable, the wallet shows the oracle as unavailable exactly as it does today. Mainnet only. Controlled by the existing OracleEnabled switch."

## Background

The wallet now reads DigiDollar balances and history from a public block explorer. A public explorer has no oracle data, so with that setup the DigiDollar screens show the oracle price, its freshness, the volatility freeze and the emergency (ERR) state as "unavailable", and the oracle display is switched off. The node service that used to supply that data no longer exists.

The public site digibyte.io publishes the oracle's current price and the DigiDollar protection status as read-only data that any website may read. This feature uses it to bring the oracle display back, for display only. The wallet does not contact digibyte.io itself: the project's own data service fetches the data and relays it (see Clarifications).

Terms used below: "the source" means digibyte.io; "the data service" means the project's own relay; "the oracle data cannot be obtained" covers either of them failing.

## Clarifications

### Session 2026-11-16

- Q: Should the wallet ask digibyte.io for the oracle data directly from each user's browser, or through the project's own data service? → A: Through the project's own data service (option B). Users' network details stay hidden from digibyte.io, one shared cache serves everyone, and the browser's content policy is not widened.
- Q: When the source says every DigiDollar operation is restricted, not only minting, should the wallet only show a warning, or also stop the user from sending DigiDollar? → A: Warning only (option A). The existing protection notice and the confirm-step warning appear, and the user can still send; an unverified third-party answer can never block a send.
- Q: When the source reports the oracle as not active but still gives a price, should the wallet show that price or show the oracle as unavailable? → A: Unavailable (option A). Only an answer whose status is "active" is used; anything else is treated as unusable.
- Q: When the wallet cannot learn the current chain height because the public explorer is failing, how should it judge whether the oracle price is fresh? → A: Fall back to the source's stale flag and the time of the last update, stale after 5 minutes (option A). Refined 2026-11-16: this applies when the height is missing from an otherwise available DigiDollar health answer; when the explorer's DigiDollar data is unavailable altogether, the oracle area shows as unavailable too (FR-004).
- Q: On the home screen, should the dollar value of the user's DGB balance keep using the market price from the price feed, or use the oracle price when it is available? → A: Keep the market price (option A). The oracle price appears only on DigiDollar screens; spec 001's wording on this point (FR-019 and FR-021) is amended to match.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See the oracle price on DigiDollar screens (Priority: P1)

A person who holds or views DigiDollar opens a DigiDollar screen on mainnet and sees what one DGB is worth in US dollars according to the DigiDollar oracle, how fresh that price is, and where it came from.

**Why this priority**: This is the whole gap. Without a price, the DGB-to-dollar context on every DigiDollar screen is missing, and the price banners cannot work.

**Independent Test**: With DigiDollar viewing and the oracle display switched on, open a DigiDollar screen on mainnet while the oracle data can be obtained. The oracle price, the block it was last updated at, how many blocks ago that was, and the source name appear.

**Acceptance Scenarios**:

1. **Given** the oracle display is on, the network is mainnet and the oracle data can be obtained, **When** the user opens a DigiDollar screen, **Then** the oracle price in US dollars per DGB appears with its last-update block and age in blocks.
2. **Given** the price is shown, **When** the user looks for where it came from, **Then** a line names digibyte.io as the source and says the wallet does not verify it.
3. **Given** the price is shown and a new price is published, **When** the screen refreshes on its normal schedule, **Then** the new price replaces the old one without a reload.
4. **Given** the user has the DigiDollar screen open for several minutes, **When** the price has not changed, **Then** the wallet does not ask the data service more often than once every 30 seconds.

---

### User Story 2 - Never trust a missing or old price (Priority: P1)

A person uses the wallet while the oracle data cannot be obtained: the data service or the source is down, slow, blocked, or answering with something unusable. DigiDollar and DGB screens still work, and the oracle shows as unavailable instead of showing a guess or an old price.

**Why this priority**: The wallet holds real funds and must never block core actions on a third party, and must never present a stale price as current.

**Independent Test**: Make the data service or the source unreachable (or have either return unusable data) and open the DigiDollar and DGB screens. Balance, history, receive and send still work, the oracle shows as unavailable, and no earlier price is presented as current.

**Acceptance Scenarios**:

1. **Given** the oracle data cannot be obtained (the data service or the source cannot be reached), **When** the user opens a DigiDollar screen, **Then** the oracle shows as unavailable, exactly as it does today, and every other part of the screen works.
2. **Given** a price was shown a few minutes ago and the oracle data then stops arriving, **When** the last good answer is older than 5 minutes, **Then** the price is no longer shown as current and the oracle shows as unavailable.
3. **Given** the source answers with a missing, zero, negative or non-numeric price, or with a status other than "active", **When** the wallet reads it, **Then** the oracle shows as unavailable.
4. **Given** the oracle data is slow, **When** the data service has not answered within 5 seconds, **Then** the wallet stops waiting, shows unavailable, and no other screen is delayed.
5. **Given** the oracle is unavailable, **When** the user sends DigiDollar, **Then** the send is still possible with the same "price unavailable" warning the wallet shows today.

---

### User Story 3 - See when DigiDollar is under protection or the price is stale (Priority: P2)

A person sees a clear notice when the DigiDollar system is in a protective or degraded state: the price is stale, a volatility freeze is on, the emergency (ERR) state is active, or minting or every operation is restricted. The wallet uses the same notices and rules it already has, with new wording for the two restrictions.

**Why this priority**: These states change what is safe to do with DigiDollar. They matter, but they build on the price in User Story 1.

**Independent Test**: Feed the wallet each state (stale price, volatility freeze, emergency, minting restricted, all operations restricted) and confirm the matching notice appears; feed it a normal state and confirm no notice appears.

**Acceptance Scenarios**:

1. **Given** the last update is more than 20 blocks behind the chain tip, **When** the user opens a DigiDollar screen, **Then** the price shows as stale and the existing stale-price notice appears.
2. **Given** the source reports that a volatility protection is active, **When** the user opens a DigiDollar screen, **Then** the existing volatility notice appears.
3. **Given** the source reports the emergency (ERR) state, **When** the user opens a DigiDollar screen, **Then** the existing emergency notice appears.
4. **Given** the source reports a normal state, **When** the user opens a DigiDollar screen, **Then** no protection notice appears.
5. **Given** the source says the price is stale but the block gap is small, **When** the wallet judges freshness, **Then** it treats the price as stale (the stricter of the two).
6. **Given** the explorer's DigiDollar health is available but its chain height is missing, **When** the wallet judges freshness, **Then** it uses the source's stale flag and the time of the last update, and treats the price as stale once that update is more than 5 minutes old.
7. **Given** the source reports that every DigiDollar operation is restricted, **When** the user opens a DigiDollar screen or reaches the confirm step of a send, **Then** the protection notice and confirm-step warning appear, and the user can still send.
8. **Given** the source reports that minting is restricted, **When** the user opens a DigiDollar screen or reaches the confirm step of a send, **Then** the matching notice and confirm-step warning appear, and the user can still send.

---

### User Story 4 - Keep full control over whether and when it is used (Priority: P3)

The person who runs the wallet deployment can switch the oracle display off without releasing a new version, and the wallet makes no oracle requests when it is off, on test networks, or when the node service is the DigiDollar data source.

**Why this priority**: It limits exposure and keeps behaviour predictable, but it is not needed for the display itself.

**Independent Test**: Turn the oracle switch off and watch network traffic: the wallet makes no oracle request and no oracle display appears. Switch to a test network: same result.

**Acceptance Scenarios**:

1. **Given** the oracle switch is off, **When** the user uses DigiDollar screens, **Then** no oracle display appears and the wallet makes no oracle request.
2. **Given** a test network is selected, **When** the user opens DigiDollar screens, **Then** the wallet makes no oracle request and the oracle shows as unavailable.
3. **Given** the DigiDollar data comes from the node service instead of a public explorer, **When** the user opens DigiDollar screens, **Then** the node's own oracle data is used and the wallet makes no oracle request to the data service.

---

### Edge Cases

- The source reports a price of zero, a negative number, or text instead of a number.
- The source gives a valid price but reports the oracle status as anything other than "active" (treated as unusable).
- The source reports a last-update block that is ahead of the chain tip the wallet knows.
- The chain height is missing from an otherwise available DigiDollar health answer: freshness falls back to the source's stale flag and the age of the last update.
- The explorer's DigiDollar data is unavailable (it is failing): the oracle area shows as unavailable together with the rest of the DigiDollar data, even if digibyte.io is up.
- The source says "stale" but the last-update block is recent, or the reverse.
- The source reports that every DigiDollar operation is restricted (the user is warned, never blocked).
- The answer is well formed but a value the wallet needs is missing.
- The source answers with an error, rate-limits the data service, or answers very slowly.
- The project's data service is down while digibyte.io is up (the wallet cannot get oracle data and shows it as unavailable).
- digibyte.io is down while the data service holds a recent answer (the wallet shows that answer with its age, until it is 5 minutes old).
- The user's device is offline.
- The price changes while the user is looking at the screen.
- The first oracle answer arrives after the screen has appeared: the screen shows the loading placeholder and then the price, without waiting for the next regular refresh, and without holding up any other part of the screen.
- Several screens are open or opened in quick succession (they must share one request, not each make their own).
- The source changes its response format in the future (the wallet must fall back to unavailable, not show wrong numbers).
- The price is very small (for example $0.002725), so it must be shown with enough decimals to be useful.

## Requirements *(mandatory)*

### Functional Requirements

**The price**

- **FR-001**: When DigiDollar viewing and the oracle display are on, the network is mainnet and DigiDollar data comes from a public explorer, the wallet MUST show the oracle price in US dollars per DGB, the block it was last updated at, and how many blocks ago that was, on the DigiDollar screens that showed the oracle price before.
- **FR-002**: The price MUST be shown with enough decimals to read a sub-cent price (at least six decimal places).
- **FR-003**: Wherever the price is shown, the wallet MUST show its source as digibyte.io and state that the wallet does not verify it.

**Freshness and protection states**

- **FR-004**: The wallet MUST treat the price as stale when the last update is more than 20 blocks behind the chain tip the wallet knows, or when the source itself reports the price as stale. When the chain tip is missing from an otherwise available DigiDollar health answer, the wallet MUST judge freshness from the source's stale flag and the time of the last update, treating an update more than 5 minutes old as stale. When the explorer's DigiDollar data is unavailable altogether, the oracle area MUST show as unavailable too (the DigiDollar screens are unavailable then).
- **FR-005**: The wallet MUST show the existing stale-price notice, volatility notice and emergency notice (spec 001, FR-019 and FR-027) from the source's state, and MUST show no notice when the state is normal. A report that minting is restricted, or that every DigiDollar operation is restricted, MUST be shown in the existing protection banner and confirm-step warning list, each with its own wording.

**Safety and limits of use**

- **FR-006**: The oracle price and status MUST be used for display and warnings only. They MUST NOT be used to build, change, sign, approve or block any transaction, amount, fee or output. Sending MUST remain possible while the oracle is unavailable and while the source reports any restriction; the user sees a warning and decides.
- **FR-007**: The wallet MUST get the oracle data only from the project's own data service, which fetches it from digibyte.io and relays it. No request from the wallet, and none from the data service, may carry wallet data: no addresses, transaction ids, balances, wallet identifiers, or credentials. They MUST be the same fixed public requests for every user, and neither the wallet nor the data service may log them in a way tied to a user (spec 001, FR-023). digibyte.io MUST NOT see any user's network address or browser details.

**Failure behaviour**

- **FR-008**: If the oracle data cannot be obtained, the wallet MUST show the oracle as unavailable, exactly as it does today. That covers the data service being unreachable, not answering within 5 seconds, or returning an error, and digibyte.io being unreachable, not answering within 5 seconds, returning an error, or returning unusable data (a missing, zero, negative or non-numeric price, a missing required field, or an oracle status other than "active").
- **FR-009**: Neither the wallet nor the data service may present an earlier answer as current once it is older than 5 minutes (about 20 blocks), counted from when the data service obtained it from digibyte.io. Whether the oracle's own last update is too old is the separate freshness rule in FR-004.
- **FR-010**: An oracle failure MUST NOT block or delay balance, receive, send or history for DGB or DigiDollar, and MUST NOT break any screen.

**Efficiency**

- **FR-011**: The wallet MUST reuse one answer for 30 to 60 seconds, share it across all screens, and make no more than one request to the data service every 30 seconds. The data service MUST keep one shared answer per kind of data (the oracle reading and the protection status) and make no more than one attempt per kind to reach digibyte.io every 30 seconds, whether the previous attempt succeeded or failed, however many wallets are asking.

**Scope and control**

- **FR-012**: The feature MUST apply only on mainnet. On a test network the wallet MUST make no oracle request and show the oracle as unavailable.
- **FR-013**: The feature MUST be controlled by the existing oracle switch. When it is off, the wallet MUST make no oracle request and show no oracle display.
- **FR-014**: The feature MUST apply only when DigiDollar data comes from a public explorer. When it comes from the node service, the node's oracle data MUST be used and the wallet MUST NOT ask the data service.
- **FR-015**: Neither the wallet nor the data service may use digibyte.io for balances, history, unspent outputs, sending or fees.

**Security of the connection**

- **FR-016**: The wallet's content policy MUST NOT be widened for this feature (no allowance for digibyte.io): the wallet talks only to the project's own data service, which the policy already allows. The data service's oracle route MUST be read-only, serve only the fixed oracle data, accept no user input that changes what it fetches, and be rate-limited per visitor. It is a public data relay, which the project constitution allows (Principle II), and MUST NOT become a required part of any core wallet flow.

**Quality**

- **FR-017**: All new user-facing text MUST go through the wallet's existing localization, and the screens MUST stay operable with the keyboard and screen readers like the rest of the DigiDollar screens (spec 001, FR-024 and FR-025).

**Other screens**

- **FR-018**: The dollar value of the user's DGB balance on the home screen MUST keep using the market price in the user's chosen currency, and MUST NOT change when the oracle is on, off or unavailable. The oracle price is shown only on DigiDollar screens. Spec 001 (FR-019 and FR-021) is amended in the same change so that it no longer says the home value comes from the oracle price.

### Key Entities *(include if feature involves data)*

- **Oracle reading**: The current price in US dollars per DGB, the block it was last updated at, its age in blocks, whether it is stale, and when the wallet received it. It exists only in memory and is replaced by each new answer.
- **Protection status**: Whether a volatility protection is active, whether the emergency (ERR) state is active, whether minting is restricted, and whether every operation is restricted. Informational; it drives the existing notices and the confirm-step warning and never blocks a send.
- **Source note**: The line that names digibyte.io as the source and says the wallet does not verify the data.
- **Oracle data service**: The project-run relay that fetches the oracle data (two kinds: the oracle reading and the protection status) from digibyte.io, keeps one shared answer per kind of data for 30 to 60 seconds, drops it after 5 minutes, and serves it to wallets. It holds no user data.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With the oracle data available, the oracle price appears on a DigiDollar screen within 3 seconds of opening it in at least 95% of loads on a normal connection (a mobile connection of about 10 Mbit/s and 100 ms delay, typical of 4G).
- **SC-002**: The price shown matches the source's price at the time of the last refresh in 100% of checks (compared in testing with the source's own answer).
- **SC-003**: With the oracle data unobtainable or unusable, 100% of DGB and DigiDollar screens still load and work, and the oracle shows as unavailable within 6 seconds.
- **SC-004**: No answer older than 5 minutes (counted from when the data service obtained it) is ever shown as current (0 occurrences in testing across source outages).
- **SC-005**: Inspecting the wallet's traffic shows requests only to the project's own data service and none to digibyte.io, and inspecting the data service's requests to digibyte.io shows only the fixed public requests and no wallet data (0 addresses, transaction ids or balances, and 0 user network details).
- **SC-006**: A user who keeps a DigiDollar screen open for 10 minutes causes at most 20 requests to the data service.
- **SC-007**: With the oracle switch off, on a test network, or with the node service as data source, the wallet makes 0 oracle requests.
- **SC-008**: On 100% of screens that show the oracle price, a user can see that it comes from digibyte.io and is not verified by the wallet.
- **SC-009**: Each of the five notices (stale price, volatility protection, emergency, minting restricted, all operations restricted) appears in testing for its own state and in no other state.
- **SC-010**: In testing, a DigiDollar send can be completed in every state the source can report, including "every operation restricted" and "oracle unavailable" (0 blocked sends caused by this feature).
- **SC-011**: The home screen's dollar value of the DGB balance is identical with the oracle on, off and unavailable (0 differences in testing).
- **SC-012**: However many wallets are open at once, and including while digibyte.io is failing, digibyte.io receives at most 2 requests per kind of data per minute from the project.

## Assumptions

- The source is the public digibyte.io site, reached only through the project's data service. It publishes the oracle price and the DigiDollar protection status as read-only data that any website may read, without a key. It has no published availability promise and may change, so every failure falls back to "unavailable".
- On 2026-11-15 the source's oracle price data included: the price in micro-dollars (for example 2725, meaning $0.002725 per DGB), the last-update block and time, a validity window of 20 blocks, a stale flag, and a volatility-protection block. Its DigiDollar status data included the emergency state and minting restrictions. The exact field names are a planning detail.
- The data service is the project's existing public data service for prices. A new oracle route there is a small addition, and it can be deployed and switched off without a wallet release.
- The chain tip used to judge freshness is the one the wallet already gets from the public explorer as part of its DigiDollar health answer; if only the height is missing from an available answer, the time of the last update is used (see FR-004).
- The source's data is not verified by the wallet. Checking the price against the signed oracle data in each block is a possible later feature and is out of scope here.
- Out of scope: using digibyte.io for balances, history, unspent outputs, sending or fees (it offers none of these); talking to the oracle servers directly; showing the source's 24-hour high and low (they appear to be in cents, not dollars); showing the system collateral ratio, DigiDollar supply or number of positions (the source has them, and they could be a follow-up).
- While the first answer is loading (up to 5 seconds), the oracle area shows a neutral loading placeholder and not the "unavailable" message, so a normal load does not flash an error.
- The operator sees the health of the oracle data service through the project's usual service monitoring; no new operator screen is part of this feature.
- Production turns the oracle display on at deployment; its current default of off was chosen because no oracle data was available.
- Constitution check: Principle V is met by FR-008 to FR-010 (defined unavailable behaviour, no blocking of core flows); the content-policy rule is met by FR-016 (no new site in the browser's allow-list); Principle II allows the data service as a public data relay (FR-016); Principle III requires tests for the new wallet logic and the relay; Principle IV is respected because the shared rules (validity, freshness, protection states) belong to the wallet layer and the relay stays a separately deployable service.
- This feature builds on spec 001 (DigiDollar wallet: oracle and degraded-mode requirements FR-018, FR-019 and FR-027, and the public-explorer backend described in its quickstart under "Backend choice", which is the default again since 2026-11-16).
