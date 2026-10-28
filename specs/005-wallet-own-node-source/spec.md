# Feature Specification: Wallet Uses Our Own Node When Ready

**Feature Branch**: `feature/wallet-own-node-source` (spec directory `005-wallet-own-node-source`)

**Created**: 2026-10-28

**Status**: Draft

**Input**: User description: "Make the DGB wallet app ready to use our own Railway-hosted DigiByte node (feature 004) as a first-class blockchain data source while that node is still syncing and after it is synced. Today the wallet's cascading blockchain service treats the node API as the last-resort backend behind public explorers. We want the wallet to prefer our own node only when it reports itself healthy and fully synced (status starting/syncing/ok/unhealthy, lag, peers, txindex, versionOk), keep using the existing explorers while the node is syncing, starting, unhealthy or unreachable, switch automatically in both directions without user action or data loss, stay configurable per deployment (node API URL, on/off) without a code change, never block core DGB flows (balance, receive, send, history) on the node, and show the user clearly which source is active and when a degraded mode is in effect. It must keep self-custody guarantees (no keys leave the device) and the existing broadcast fallback behaviour, and must be testable now against the node while it syncs so cut-over can happen the moment it reports ok."

## Findings From Initial Research

- **Today's order**: the wallet reads from public explorers first and uses our node only as the last resort. For sending, it tries our node first and then the explorers. There is no check of whether the node is actually in a fit state.
- **Sending through a node that is still syncing is a gap**: today a broadcast tries the node first even if the node is not synced. It should only be tried first when the node reports itself ready.
- **Address lookups on the node are slow**: measured against the real node, an address balance took about 14 seconds (the node answers by scanning its whole set of spendable outputs; the transaction index does not help), against under a second from an explorer. Transaction lookups, fees, chain height and sending are fast.
- **Nothing in the app shows the active source**: the service tracks which backend answered last, but no screen displays it.
- **The node's health report exists** (feature 004): `status` is one of `starting`, `syncing`, `ok`, `unhealthy`, with block lag, peers, transaction-index state and version check. It answers 200 for all states except unreachable.
- **The node API address is already a deployment setting**; there is no on/off switch and no readiness logic.

## Clarifications

### Session 2026-10-28

- Q: Should the wallet cross-check the node's block height against a public explorer before and while using the node? → A: Yes; the node is used only while its height is within 5 blocks of a public explorer, and the wallet drops back to explorers if it drifts further.
- Q: Who can open the operator diagnostics and node-comparison view? → A: Only when a deployment setting turns on a developer view; users never see it by default.
- Q: Should every user always see which source the wallet is using, or only when something is wrong? → A: A small status indicator opens a status detail on demand that names the active source; a banner appears only in degraded states.
- Q: Should users be told that using our own node means the wallet team's servers see the addresses they look up? → A: Yes; a one-line plain-language note in the status detail when the own node is active, plus the project's privacy documentation.

### Session 2026-10-29

- Q: How should the wallet use the node for address data, given that node address lookups take about 14 seconds? → A: Node first only for sending, transaction lookups, fees and chain height; address balances and spendable outputs stay explorer-first (the ready node is only their fallback) until the node has an address index, which is a later feature.

- Q: The 2-minute switch target conflicts with a 2-minute stability window plus a 30-second check interval; relax the target or shorten the window? → A: Keep the 2-minute stability window and change SC-002 to "within 3 minutes".

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Nothing changes for users while the node is still syncing (Priority: P1)

The node will take a day or more to sync. During that time the wallet must behave exactly as it does today: balances, history, receive, send and fees work from the public explorers, with no errors, delays or warnings caused by the node being unfinished.

**Why this priority**: Real users hold real funds in the wallet. Turning on node support must never make things worse while the node is not ready.

**Independent Test**: Point a wallet at a node that reports `syncing` (or `starting`). Use balance, history, receive and send. Verify the results match a wallet with node support off, and that the node supplied no balance, history or fee data.

**Acceptance Scenarios**:

1. **Given** node support is on and the node reports `syncing`, **When** the user opens the wallet, **Then** balance, history and fees load from the public explorers as they do today.
2. **Given** the node reports `starting`, `syncing` or `unhealthy`, or cannot be reached, **When** the user sends a transaction, **Then** it is broadcast through the public explorers without first waiting on or failing because of the node.
3. **Given** the node is unreachable or very slow, **When** the wallet checks its status, **Then** the check never delays the screen the user is looking at by more than a short, bounded time.

---

### User Story 2 - The wallet switches to our own node automatically once it is ready, and back if it stops being ready (Priority: P1)

When the node reports it is fully synced and healthy, the wallet starts using it first for sending, transaction lookups, fees and chain height (address balances and spendable outputs still come from the explorers first, because the node answers those slowly). If the node falls behind, loses its peers, restarts or becomes unreachable, the wallet goes back to the public explorers by itself. Users do nothing and lose nothing.

**Why this priority**: This is the point of running our own node: independence from third-party explorers, with a safety net.

**Independent Test**: Run a test node whose reported status can be changed. Move it from `syncing` to `ok`, then to `unhealthy`, then back. Watch which source the wallet uses and confirm no data is lost or shown wrongly at any switch.

**Acceptance Scenarios**:

1. **Given** the node reports `ok` with a current chain tip, a ready transaction index and an acceptable version, **When** the wallet next checks the node, **Then** it prefers the node for sending, transaction lookups, fees and chain height, and keeps it as a fallback for address balances and spendable outputs.
2. **Given** the wallet is using the node, **When** the node stops reporting `ok` (lagging, no peers, outdated version, unreachable), **Then** the wallet switches to the public explorers for the next requests without user action.
3. **Given** the node flips between ready and not ready several times in a few minutes, **When** the wallet follows it, **Then** it does not switch sources on every flip; it waits for the node to stay ready for a short stable period before switching to it, and falls back promptly when it stops being ready.
4. **Given** the wallet is using the node and one particular read fails, **When** the user asks for the data, **Then** that request is answered by an explorer and the user sees correct data without an error.
5. **Given** a switch has happened in either direction, **When** the user looks at their balance and history, **Then** nothing is lost, duplicated, or shown as an error; pending transactions stay tracked.

---

### User Story 3 - Users can see which source is active and when the wallet is in a degraded mode (Priority: P2)

A user, or the support person helping them, can see whether the wallet is currently using our own node or public explorers. When the wallet cannot reach any source, or can only reach a fallback, it says so in plain words.

**Why this priority**: Trust and support. It also makes the cut-over visible and testable. It is lower than the two behaviours above because the wallet works without it.

**Independent Test**: Force each state (own node, explorer, all unavailable) and check the wording shown in the wallet's status area.

**Acceptance Scenarios**:

1. **Given** the wallet is using our own node, **When** the user opens the status area, **Then** it shows that the source is the DigiByte node run by the wallet team.
2. **Given** the wallet is using public explorers, **When** the user opens the status area, **Then** it shows that source, and if node support is on but the node is not ready, it says the own node is "syncing" or "unavailable".
3. **Given** every source has failed, **When** the user opens the wallet, **Then** it shows the last known balance and history clearly marked as not up to date, and sending explains the situation.
4. **Given** everything is healthy, **When** the user uses the wallet normally, **Then** no warning banner or interruption is shown.

---

### User Story 4 - Operators control node use per deployment without changing code (Priority: P2)

Whoever deploys the wallet can switch node use on or off, and set the node address, by changing deployment settings only. With the switch off, the wallet behaves exactly as it does today.

**Why this priority**: Safe rollout and instant rollback, per environment (staging, production).

**Independent Test**: Deploy with the setting off, on, and on with a wrong address. Check behaviour in each, with no rebuild.

**Acceptance Scenarios**:

1. **Given** node use is off, **When** the wallet runs, **Then** it never contacts the node's health report and behaves exactly as before this feature.
2. **Given** node use is on with a valid address, **When** the wallet starts, **Then** it begins checking the node's readiness.
3. **Given** node use is on with a wrong or unreachable address, **When** the wallet runs, **Then** it behaves as "node unavailable" and works from explorers, with no repeated visible errors.

---

### User Story 5 - The team can try the node now and see when to cut over (Priority: P3)

While the node is still syncing, an operator can see its readiness from the wallet side, and once it is ready can compare what it returns against the public explorers before real users depend on it.

**Why this priority**: It lets cut-over happen the moment the node is ready, with confidence, but it is a tool for the team, not for users.

**Independent Test**: Against a syncing node, view the readiness details. Against a ready node, run the comparison for a set of known addresses and see differences, if any.

**Acceptance Scenarios**:

1. **Given** the node is syncing, **When** an operator opens the diagnostics view, **Then** it shows the node's reported state, block lag, peer count, version and transaction-index state, and that the wallet is not using the node.
2. **Given** the node reports `ok`, **When** an operator runs the comparison for known test addresses, **Then** it reports whether balances, spendable outputs and chain height from the node match the public explorers.
3. **Given** the comparison finds a mismatch, **When** it is shown, **Then** the wallet keeps using explorers until an operator decides otherwise.

---

### Edge Cases

- The node says `ok` but is behind (or ahead of) the public explorers by more than 5 blocks, for example because it is on a stale or wrong chain: it counts as not ready and the wallet uses the explorers.
- Public explorers cannot be reached for the height cross-check: the wallet keeps the source it is using and does not switch to the node until the check can be done.
- The node's readiness report is blocked by the browser, times out, or returns something unexpected: treated as "unavailable", never as ready.
- The node is ready but the version check says it is older than recommended: treated as not ready.
- The node and an explorer disagree on a balance: the wallet does not guess; it keeps the preferred source for that session and the comparison tool (User Story 5) exists to catch it before cut-over.
- A broadcast through the node is accepted but the transaction never shows up elsewhere: the existing behaviour of trying the next source on a failed broadcast stays; a successful broadcast is never repeated blindly.
- The user is offline: existing offline behaviour (cached balance and history, clearly marked) is unchanged.
- The wallet is open for days: readiness is re-checked regularly without user action and without noticeable battery or data use.
- Testnet and regtest: node use applies only where an own node is configured for that network; other networks behave as today.
- The DigiDollar features use the node API for their own data: their separate setting is unchanged by this feature.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The wallet MUST decide whether to use our own node by reading the node's own readiness report, and MUST treat the node as ready only when it reports `ok`, a transaction index that is ready, and an acceptable version, and its block height is within 5 blocks of a public explorer's height.
- **FR-002**: While the node is not ready (starting, syncing, unhealthy, version too old, unreachable, or the report is missing or invalid), the wallet MUST obtain balance, history, fees and chain height from the public explorers exactly as it does today.
- **FR-003**: While the node is ready, the wallet MUST prefer it for transaction lookups, fee estimates and chain height, MUST keep explorers first for address balances and spendable outputs (using the ready node as their fallback), and MUST fall back to the explorers for any single node read that fails or runs too long, and MUST stop preferring the node after a few consecutive failed reads without waiting for the next readiness check.
- **FR-004**: For sending, the wallet MUST try the own node first only when it is ready; otherwise it MUST use the explorers first. A failed broadcast MUST still fall through to the next source, as today.
- **FR-005**: The wallet MUST switch from explorers to the node only after the node has stayed ready for a short stable period, and MUST switch away from the node promptly when it stops being ready.
- **FR-005a**: The height cross-check MUST be repeated with each readiness check; if no explorer height can be obtained, the wallet MUST keep its current source rather than switch to the node, and MUST NOT treat the missing explorer answer as the node being wrong.
- **FR-006**: Readiness MUST be re-checked regularly in the background, with a bounded wait per check, and a slow or dead node MUST NOT delay any screen.
- **FR-007**: Core flows (balance, receive, send, history) MUST NOT depend on the node being up; their availability MUST equal what the explorers alone provide.
- **FR-008**: Switching sources MUST NOT lose, duplicate or misreport balance, history or pending transactions, and MUST NOT require user action.
- **FR-009**: Node use MUST be switchable on or off, and the node address MUST be settable, per deployment through settings only, with no code change or rebuild, for any address whose origin the page's content-security policy already allows (see Assumptions).
- **FR-010**: With node use off, the wallet MUST behave exactly as it did before this feature, including never contacting the node's readiness report.
- **FR-011**: The wallet MUST show which source is currently active in a status detail that the user can open on demand from a small status indicator (no permanent source label on the main screen), and MUST show a clear degraded-mode message when it is running on a fallback because a preferred source is unavailable, or when no source is available.
- **FR-012**: When no source is available, the wallet MUST show the last known data clearly marked as out of date, and MUST explain why sending is unavailable.
- **FR-013**: The wallet MUST give operators a diagnostics view of the node's reported state and a way to compare the node's answers against the explorers for chosen addresses. The view MUST exist only when a deployment setting turns on a developer view (off by default, changeable without a rebuild), MUST NOT be reachable or visible to users otherwise, and neither it nor the comparison MUST affect what normal users see.
- **FR-014**: No private key, seed, password or signing operation MUST be sent to, or depend on, the node; only public chain requests and already-signed transactions go to it.
- **FR-014a**: When the own node is the active source, the status detail MUST include a one-line plain-language note that the wallet team's servers (rather than a third-party explorer) see the public transactions and addresses the wallet sends or looks up through it; the privacy documentation MUST say the same.
- **FR-015**: A node whose report cannot be read for any reason (blocked, timed out, malformed) MUST be treated as unavailable and MUST NOT produce repeated visible errors.
- **FR-016**: The behaviour above MUST be verifiable against a test node whose reported state can be set to each value, without needing the real node to finish syncing.

### Key Entities

- **Node Readiness**: The wallet's current view of the own node: reported state, lag, peers, transaction-index state, version check, when last checked, and whether it has stayed ready long enough to be used.
- **Active Source**: Which backend is serving reads and sends right now (own node, a named public explorer, none), and why (preferred, fallback, unavailable).
- **Deployment Settings**: Node use on or off, node address, readiness thresholds (stable period, check interval, wait limit).
- **Comparison Report**: For chosen addresses, node answers versus explorer answers (balance, spendable outputs, height) and whether they match.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With node use on and the node not ready, a user's balance, history, receive and send flows behave and perform the same as with node use off, with no added error messages and no screen delayed by more than 1 second because of the node.
- **SC-002**: Within 3 minutes of the node reporting ready (and staying ready), the wallet is using it, with no user action.
- **SC-003**: Within 35 seconds (one 30 second check interval plus the 3 second probe) of the node stopping being ready (including drifting more than 5 blocks from the public explorers), new requests are served by the explorers, with no user-visible error.
- **SC-004**: Across 20 forced switches in both directions, zero cases of lost, duplicated or wrong balance, history entries or pending transactions.
- **SC-005**: Zero transactions are first sent to a node that is not ready.
- **SC-006**: Turning node use on or off, or changing the node address to another already-allowed origin, takes effect through deployment settings alone, in under 15 minutes, with no rebuild.
- **SC-007**: A user or support person can tell which source is active within 10 seconds, and 100% of degraded states (fallback in use, nothing available) show a message in plain language.
- **SC-008**: Before cut-over, the comparison for at least 20 sample addresses shows matching balances and spendable outputs between the node and the explorers.

## Assumptions

- "Ready" means exactly what the node's health report says: state `ok`, transaction index ready, version acceptable. The wallet does not invent its own definition of ready.
- Default preference when both sources work: own node for sending, transaction lookups, fees and chain height; explorers first for address balances and spendable outputs. The node's address lookups take about 14 seconds, so putting them first would slow every balance screen. An address index on the node (a later feature, like the one DigiDollar already has) would let the node lead for balances too.
- A short stable period (2 minutes of consecutive ready checks, so the switch happens 2 to 2.5 minutes after the node becomes ready) before switching to the node, and prompt fallback when it stops being ready, is a reasonable default to avoid flapping; exact values are planning details.
- The readiness check is public and safe to call from the wallet (it carries no credentials or private data).
- The active source is shown in a status detail opened from a small status indicator, plus a visible banner only in degraded states; there is no permanent source label on the main screen and no new top-level screen.
- Using our own node means our infrastructure sees the addresses a user looks up, instead of a third-party explorer. This is public chain data, no keys are involved, and it is consistent with the existing node-API path; users are told in the status detail and in the documentation, but are not asked to opt in separately.
- The operator diagnostics and comparison view is for the team, not a feature advertised to users; it appears only where the deployment's developer-view setting is on.
- DigiDollar data sources are configured separately and are out of scope here.
- Where this spec says the node is "ready" in the sense of choosing a source, it means the wallet's active state: the node has reported ready and has stayed ready for the stable period. A single ready report only starts the stability window.
- The wallet's strict content-security policy lists exactly which servers the page may contact. The node's address can therefore be changed by settings alone only within origins the policy already allows (one stable first-party origin is added for the node API). Moving to a new origin needs a reviewed policy change first; until then the browser blocks the calls and the wallet behaves as "node unavailable". The wildcard alternative was rejected as too broad.
- "Degraded" means a source that was working is lost (the own node was active and dropped back) or every source is failing. A node that is simply still syncing is shown in the status detail only, never as a banner.
- The public explorers stay in the product as the fallback; removing them is out of scope.
- Feature 004 (the Railway node and its node API) is the dependency: its readiness report and API are used as they are.
