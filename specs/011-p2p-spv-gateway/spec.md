# Feature Specification: P2P Light-Client Gateway (SPV Source)

**Feature Branch**: `feature/p2p-spv-gateway` (spec directory `011-p2p-spv-gateway`)

**Created**: 2026-11-14

**Status**: Draft

**Input**: User description: "A server-side P2P light-client gateway ('SPV gateway') for the DigiByte wallet. A small service (no full node required) connects directly to public DigiByte peers over the P2P protocol (DNS-seed discovery), syncs and verifies block headers (proof of work), and, for the wallet's address pool (receive + change, gap limit 20), finds relevant transactions and UTXOs. It exposes HTTP endpoints matching what the wallet's blockchain service already needs (UTXOs for a list of addresses, transaction history, broadcast of signed raw transactions to several peers) and returns merkle proofs so the wallet can verify transactions against headers instead of trusting the service. The wallet sends addresses only, never keys, seed or xpub. It plugs into the existing fallback / node-source-monitor chain as an additional source with health checks. Address matching prefers compact block filters (BIP157/158) if enough public DigiByte peers serve them; otherwise it falls back to downloading and filtering full blocks locally; it never uses BIP37 bloom filters because they leak addresses. Must handle first sync and seed-restore rescans from a wallet birth height. Goal: reduce dependence on third-party explorers and on RPC-only node access, improve resilience of reads and broadcasts, and move toward trust-minimized SPV verification."

## Clarifications

### Session 2026-11-15

- Q: How does the wallet know the headers the gateway supplies are the real chain? → A: The wallet checks each header's proof of work and cumulative work itself, starting from built-in checkpoints, in addition to the explorer height cross-check.
- Q: When healthy, is the gateway used before the explorers or only as a backup? → A: Its position (first or backup) is a per-deployment setting, defaulting to backup at launch, so it can be promoted later without a code change.
- Q: Does the gateway remember a wallet's addresses between visits? → A: No. It is stateless per wallet: the wallet sends its address list with each request, and the gateway keeps only a short-lived cache of match results and discards it.
- Q: Who can use the gateway? → A: Anyone, anonymously. Access is protected by per-client rate limits and caps on addresses and scan range per request, not by a key, because a key shipped in the public wallet would add no protection.
- Q: Do unconfirmed incoming payments count toward the spendable balance? → A: No. They are shown as pending and excluded from the spendable balance until a verified block includes them.
- Q: Where does the wallet get the birth height on a restore? → A: Wallets created after this feature store their birth height. On restore the wallet uses a stored value if the backup carries one, otherwise asks for an approximate date and converts it to a block height, and if the user does not know it scans from the start of the chain with a warning that it may take a while.
- Q: How many blocks deep must a payment be before it is verified and spendable? → A: 6 blocks (about 1.5 minutes). Before that it is shown as pending with its current confirmation count.
- Q: Can the wallet spend change from its own just-sent payment before 6 blocks? → A: Yes. Change from a transaction this wallet signed and sent is spendable immediately while shown as pending. Payments received from others still wait for 6 blocks.

### Session 2026-11-15 (after planning)

- Q: How far back does the gateway serve wallet scans directly? → A: 90 days (about 520,000 blocks) by default, adjustable by the operator per deployment. Older ranges use the explorer-based restore path.

### Session 2026-11-15 (after analysis)

- Q: Does the gateway check proof of work in the first release? → A: Yes. The gateway checks it from the start (FR-002). Only the wallet's own check is staged.
- Q: What does "checked" protect against before the wallet's own proof-of-work check ships? → A: Not a dishonest gateway. It adds a tip block-hash comparison against an independent explorer, and the wallet does not present "checked" as protection. No partial proof-of-work mode earns "verified".
- Q: What are "several peers", "enough peers" and "short-lived"? → A: At least 8 connected peers from at least 4 address ranges (at most 2 per /16), relay to at least 4 peers, filters used when at least 3 diverse peers advertise them, and a cache expiry of 30 seconds.
- Q: What happens on restore when the birth date is unknown or too old for the gateway? → A: The wallet restores through its existing explorer-based path, with a message saying why. Scanning from the start of the chain through peers is not offered (about 24.5 million blocks).
- Q: Can "verified" be shown before the wallet checks proof of work itself? → A: No. Delivery is staged: data is labeled "checked" and the gateway stays a backup source until the wallet's own proof-of-work check ships, then "verified" and promotion above explorers are enabled.
- Q: How does a routine refresh work, given the gateway only knows the last 90 days? → A: The wallet keeps its own record of its transactions, outputs and last-refreshed height and block hash, and asks the gateway only for blocks since that height minus 6, sending its known unspent outpoints so spends of older outputs are found. A wallet older than the scan window does its first refresh through the explorer path, marks those transactions "unchecked (explorer-sourced)", and the gateway can later prove them one by one at a known height.
- Q: What happens when the gateway disagrees with an explorer about a transaction the wallet got earlier from the explorer? → A: The gateway's proven data wins. The disputed transaction stays "unchecked", is held out of the spendable balance until the disagreement is resolved, and the wallet keeps a local note of it. No warning is shown to the user in the first release.
- Q: When does a wallet with no sync state get one? → A: After an explorer-served refresh. The wallet records the explorer's transactions and unspent outputs as explorer-sourced, with the last-refreshed height set to the explorer's tip height minus 6 and the hash taken from its own checked headers, or tries again next time if those headers cannot supply it yet. This covers wallets that existed before this feature.
- Q: How does the wallet's sync state stay fresh while the gateway is only a backup? → A: It is re-recorded after every explorer-served refresh from the explorer data the wallet already fetched, with the height only moving forward. The wallet sends no addresses or outpoints to the gateway just to keep it fresh. Headers may sync in the background, and proofs for explorer-sourced transactions are requested only when the gateway is the answering source or an off-by-default deployment setting enables background proving.
- Q: Where is the wallet's sync state kept, and when is it deleted? → A: In the wallet's existing per-wallet persistent cache on the device. It is deleted when the wallet is deleted or reset and in every clear-data path, is never included in an export or backup, and is rebuilt through the bootstrap rule afterwards.
- Q: Are unconfirmed payments visible through the gateway, and how does the wallet treat its own sends before they are in a block? → A: In the first release the gateway reports only transactions in blocks, so a payment from another person shows once it is in a block. The wallet records its own broadcast transactions in its sync state at once (inputs spent, change added), so its own change is spendable immediately and its spent inputs are never offered again.

## Findings From Initial Research

- **Today every read and broadcast goes through a trusted HTTP server.** The wallet asks public explorers first, then our own node's REST wrapper, then (development only) mock data. Each of those can misreport balances or history, and a single explorer outage degrades the whole app. Nothing the wallet receives is checked against the blockchain itself.
- **A browser cannot speak the DigiByte network protocol.** The wallet runs in the browser, which has no raw network sockets, and DigiByte nodes do not offer a browser-reachable transport. A server-side component has to do the peer-to-peer work and hand results to the wallet over the same kind of web requests it already uses.
- **The wallet already hands the blockchain layer a list of addresses.** Balance, spendable-output and history lookups take an address list, and the wallet maintains a bounded pool of receive and change addresses (20 consecutive unused addresses end a scan). A light-client source can be driven by exactly that list.
- **A source-selection mechanism already exists** (feature 005): a monitor decides from a health report plus a height cross-check whether an additional source is usable, and the wallet switches in both directions without user action. This feature is another source for that mechanism, not a new one.
- **No full node is required.** Light clients keep only block headers (a few dozen bytes per block) plus data for the watched addresses, so the service is small compared with the pruned node that feature 004 runs.
- **Address matching without leaking addresses is the hard part.** The peer protocol has no "give me the history of this address" request. The privacy-preserving options are compact block filters (only useful if enough public peers serve them) or downloading blocks and filtering locally. The older bloom-filter method sends address fingerprints to strangers and is excluded.
- **Whether public DigiByte peers serve compact block filters is unknown.** Resolving this is the first research task of planning. It decides how efficient first sync and restore are, but not what the user sees.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Balance and history without trusting an explorer (Priority: P1)

A wallet user opens the wallet and sees their balance, spendable funds and transaction history. When the light-client source is active, those figures come from data the wallet has checked against the blockchain's own block headers, not from an explorer's word, and the wallet tells the user the figures are "checked" (and "verified" once the wallet's own proof-of-work check has shipped, see FR-013).

**Why this priority**: This is the point of the feature. Without it there is nothing new for users: the same trusted-server model with a different server.

**Independent Test**: Point a test wallet with known history at the gateway with explorers switched off. Confirm balance, spendable outputs and history match the chain, each confirmed transaction is marked verified, and a tampered response (altered amount, or a proof that does not match the headers) is rejected and not shown as fact.

**Acceptance Scenarios**:

1. **Given** the gateway is healthy and synced, **When** the user opens the wallet, **Then** balance, spendable outputs and history are shown, and the status detail reports the source as the light-client gateway with transactions labeled "checked" (or "verified" once the wallet's own proof-of-work check has shipped).
2. **Given** the gateway returns a transaction whose proof does not match the block headers, **When** the wallet receives it, **Then** the wallet does not count it toward the balance or history, marks the source as faulty, and falls back to the next source.
3. **Given** a transaction is in a block fewer than 6 blocks deep, or is one the wallet itself just sent and is not yet in a block, **When** it appears in history, **Then** it is shown as pending with its confirmation count and not as verified, is excluded from the spendable balance (except change from the wallet's own sends), and becomes verified and spendable only after it is at least 6 blocks deep in the verified chain.
4. **Given** the user has never used the wallet before, **When** the gateway is active, **Then** an empty wallet shows a zero balance and no history, without errors.
5. **Given** a wallet whose first use is older than the gateway's scan window, **When** the user opens it for the first time with the gateway active, **Then** the first refresh goes through the existing explorer-based path, those transactions are shown as "unchecked (explorer-sourced)", the result of that first refresh is recorded as the wallet's sync state, and later refreshes ask the gateway only for new blocks, and transactions become "checked" as they are proven.

---

### User Story 2 - Sending keeps working when explorers are down (Priority: P1)

A user sends DGB. The signed transaction is relayed to at least 4 independent peers on the DigiByte network through the gateway, so a send does not depend on any one explorer or on our own node being up.

**Why this priority**: Broadcast resilience is a stated goal, and a failed send at the wrong moment is the worst experience a wallet can give.

**Independent Test**: With all public explorers and our own node unreachable, send a signed test transaction through the gateway and confirm it reaches the network and later appears confirmed. Confirm that sending still works through the other sources when the gateway is down.

**Acceptance Scenarios**:

1. **Given** the gateway is healthy, **When** the user sends DGB, **Then** the signed transaction is relayed to at least 4 peers and the user sees it as sent, with its identifier.
2. **Given** none of the peers accept the transaction, **When** the send is attempted, **Then** the user sees a clear failure with the reason where known, and no funds are shown as spent.
3. **Given** the gateway is unreachable, **When** the user sends, **Then** the wallet falls back to the existing sources and the send succeeds if any of them is available.
4. **Given** the user has just sent a payment, **When** they send again before it confirms, **Then** the change from the first payment is available to spend, and incoming payments from others are not.
5. **Given** the same signed transaction is submitted twice, **When** the gateway receives the second copy, **Then** it is not treated as a new payment and the user is not shown a false error.

---

### User Story 3 - Restoring a wallet from a seed (Priority: P2)

A user restores a wallet from their recovery phrase on a new device. The wallet finds all used addresses and their funds by scanning forward from a starting point, using the gateway as its source, and shows a progress indication until the scan is done.

**Why this priority**: Restore is where a light-client source is slowest and where users are most anxious. It must work, but it can come after everyday balance and send.

**Independent Test**: Restore a known test seed with funds spread across receive and change addresses, including a gap of unused addresses, with a known earliest-use block. Confirm all funds are found, progress is shown, and restoring from an earlier starting point finds the same funds more slowly.

**Acceptance Scenarios**:

1. **Given** a restored wallet with a known birth height, **When** the scan runs, **Then** it examines blocks only from that height forward and finds every used address within the usual gap limit.
2. **Given** the recovery phrase carries no birth height, **When** the user restores, **Then** the wallet asks for an approximate date of first use and converts it to a block height. **Given** the user does not know the date, or the date is older than the gateway can scan, **Then** the wallet restores through its existing explorer-based path instead and tells the user why. Funds are never skipped silently.
3. **Given** the scan is interrupted by closing the app, **When** the user reopens it, **Then** the scan resumes rather than starting over, and the wallet shows what is done and what remains.
4. **Given** funds sit beyond the normal gap, **When** the user chooses a deeper scan, **Then** the wallet finds them using the existing deep-scan setting.

---

### User Story 4 - The gateway never breaks the wallet (Priority: P2)

If the gateway is still syncing, behind, short of peers, or down, the wallet keeps working exactly as it does today through its existing sources. The user sees which source is active, and a notice only when something is degraded.

**Why this priority**: The constitution forbids core flows depending on a new backend. This is a release gate.

**Independent Test**: Stop the gateway, start it with an empty data store, and let it lag behind the chain. In each state confirm balance, receive, send and history behave as before, no errors appear, and the source indicator is accurate. Confirm the wallet switches back to the gateway on its own once it is healthy.

**Acceptance Scenarios**:

1. **Given** the gateway is syncing headers, **When** the user uses the wallet, **Then** existing sources are used and nothing about the experience changes.
2. **Given** the gateway's chain height trails a public explorer by more than the allowed margin, **When** the monitor checks, **Then** the gateway is not used for reads.
3. **Given** the gateway becomes healthy and stays so for the stability window, **When** the monitor checks, **Then** the wallet starts using it without user action or data loss.
4. **Given** the gateway fails mid-request, **When** a read is in flight, **Then** the wallet retries the next source and the user sees no error and no zero-balance flash.

---

### User Story 5 - Operators can see and trust the gateway (Priority: P3)

An operator running the gateway can see how many peers it has, whether it is matching with filters or full blocks, its chain height against the network, and its recent errors. The wallet's status detail shows a plain-language one-line note about what the service learns.

**Why this priority**: Operators need to run it safely, but users can benefit from the feature without it.

**Independent Test**: Start the gateway and confirm the health report states the matching mode (`blocks` in the first release), the peer count, the number of peers advertising filters, header height and lag, and that no address or secret appears in logs or the report. Testing the filter mode is added when that mode is built.

**Acceptance Scenarios**:

1. **Given** the gateway is running, **When** an operator requests its health, **Then** the report states status, peer count, the number of peers advertising filters, header height, lag against peers and matching mode, and contains no per-wallet data.
2. **Given** the gateway has served requests, **When** an operator inspects logs, **Then** no full addresses, transaction contents or secrets appear.
3. **Given** the gateway is active, **When** the user opens the status detail, **Then** a one-line note says the service sees the addresses the wallet looks up.

---

### Edge Cases

- **Chain reorganization**: a transaction shown as confirmed is orphaned. The wallet must revert it to pending or removed, and balances must follow.
- **Dishonest or eclipsed peers**: every peer the gateway reaches feeds it a false, shorter chain. Headers must be checked for proof of work and cumulative work, peers must be diverse, and a height cross-check against an independent explorer must catch a fork the gateway cannot detect on its own.
- **Few or no peers reachable**: the gateway reports degraded status and the wallet falls back, instead of serving stale data as current.
- **Large pool or deep scan**: hundreds of addresses must not overload the gateway. Requests are bounded and rejected with a clear reason when too large.
- **Many wallets at once**: one wallet's heavy restore must not starve others.
- **Address pool growth**: when the wallet hands out a new address, the wallet includes it in its next request, so funds sent to it are found on the next lookup.
- **Funds received while the wallet was offline**: found by the next refresh, which starts from the wallet's last recorded height minus 6 blocks, and shown with correct dates.
- **Gateway and explorer disagree**: the gateway's proven data wins, and the disputed transaction stays unchecked and unspendable until resolved (FR-012a).
- **Sync state older than the scan window**: the gateway cannot refresh from it, so the wallet falls back to its other sources and does not use the gateway for that wallet until the state is refreshed (FR-009a).
- **Wrong network**: a testnet address sent to a mainnet gateway, or the reverse, is rejected.
- **Malformed or hostile input**: oversized, duplicate or invalid addresses and malformed transactions are rejected without crashing the service.
- **Cold start**: a new gateway has no header history and needs time to sync. A first-time restore is the slowest case.

## Requirements *(mandatory)*

### Functional Requirements

**Network and chain tracking**

- **FR-001**: The gateway MUST find DigiByte peers on its own, without a node of ours, and MUST keep connections to at least 8 peers at once when that many are reachable, drawn from at least 4 distinct address ranges (no more than 2 peers from the same /16 range), and MUST report itself as not healthy while it has fewer than 4 peers.
- **FR-002**: The gateway MUST download block headers and accept a chain only if every header meets the proof-of-work rules, and MUST follow the chain with the most cumulative work. This check applies from the first release: the gateway MUST NOT follow a header chain whose proof of work it has not checked.
- **FR-003**: The gateway MUST handle chain reorganizations, updating what it reports for affected transactions.
- **FR-004**: The gateway MUST support mainnet and testnet, and MUST refuse requests whose addresses belong to a different network.

**Address matching and privacy**

- **FR-005**: The gateway MUST find the transactions and spendable outputs that involve a given set of addresses without revealing those addresses to DigiByte peers.
- **FR-006**: The gateway MUST NOT use bloom filters or any method that sends address-derived data to peers.
- **FR-007**: The gateway MUST examine full blocks itself and MUST report which matching mode is in use and how many connected peers advertise the compact-filter service. The first release ships block matching only and always reports mode "blocks". Matching with compact block filters is a later addition behind the same interface, to be built once at least 3 connected peers from different address ranges advertise the filter service, or our own node serves filters. The gateway MUST NOT send any address-derived data to peers in either mode.
- **FR-008**: The gateway MUST support a first sync and a rescan from a wallet-supplied starting block height, with resumable progress.
- **FR-008a**: The wallet MUST record the block height at creation for every wallet created after this feature ships, and MUST use it as the restore starting point when available. Otherwise, on restore, the wallet MUST ask for an approximate date of first use and convert it to a block height, and MUST fall back to the wallet's existing explorer-based restore, with a message saying why, when the user does not know the date or the range is older than the gateway's scan window. The scan window defaults to 90 days (about 520,000 blocks) and MUST be adjustable by the operator per deployment without a code change. The gateway MUST refuse ranges it cannot serve with a clear "range unavailable" answer, and MUST NOT return a partial result as if it were complete.

**Wallet-facing interface**

- **FR-009**: The gateway MUST answer, for a list of addresses, the spendable outputs, the transaction history and the confirmed balance, in the same shape the wallet's existing sources provide, so the wallet can use it through its existing blockchain-source abstraction.
- **FR-009a**: The wallet MUST keep its own record, on the device only, of its transactions and outputs and of the block height and hash it last refreshed to. A routine refresh MUST ask the gateway only for blocks from that height minus 6, MUST send the wallet's known unspent outpoints so spends of outputs older than the gateway's scan window are found, MUST split address and outpoint lists into requests within the per-request limit (at most 100 each), MUST advance the recorded height only after every request of the refresh has succeeded, and MUST rewind to the fork point when the recorded block hash no longer matches. A wallet older than the scan window MUST do its first refresh through the existing explorer-based path, MUST mark the transactions that path returns as "unchecked (explorer-sourced)" until the gateway has proven them, and MUST use the gateway for refreshes after that. A wallet that has no sync state (a wallet that existed before this feature, or one whose first refresh was served by the explorer path) MUST create it from the result of an explorer-served refresh: the explorer's transactions and unspent outputs recorded as "explorer-sourced", with the last-refreshed height set to the explorer's reported tip height, read before the snapshot, minus 6, and the block hash taken from the wallet's own checked headers. If those headers cannot supply that hash yet, the wallet MUST skip recording and try again on the next refresh, and MUST NOT delay or change the explorer-served result. The wallet MUST also refresh its sync state after every explorer-served refresh, using the explorer data it already fetched (those transactions stay "explorer-sourced", gateway-proven entries that agree with the explorer are kept, and the last-refreshed height only ever moves forward), so the state stays fresh while the gateway is a backup. The wallet MUST NOT send addresses or outpoints to the gateway merely to keep the state fresh while the explorers are serving. Header downloads, which are public data, MAY run in the background whenever the gateway is ready, whatever its position in the chain. The wallet MUST request proofs for explorer-sourced transactions only when the gateway is the answering source, or when a deployment setting that is off by default enables background proving, because proof requests reveal transaction ids. A sync state whose last-refreshed height is older than the gateway's scan window MUST NOT be used for gateway refreshes, and the wallet falls back to its other sources. The wallet MUST keep this record in the wallet's existing per-wallet persistent cache on the device, MUST delete it whenever the wallet is deleted or reset and in every "clear data" path, MUST NOT include it in any export or backup, and rebuilds it through the bootstrap rule after such a deletion. When the wallet broadcasts a transaction, through any source, it MUST record it in the sync state at once as unconfirmed, with its inputs marked spent and its change output added, MUST remove it if the broadcast is rejected, and MUST replace it with the gateway-proven version once it is in a block.
- **FR-010**: The gateway MUST also answer current chain height and a fee estimate.
- **FR-011**: The gateway MUST accept a signed raw transaction, relay it to at least 4 distinct peers (fewer only when fewer are connected, and then say how many), and report acceptance, rejection with reason, or no response.
- **FR-012**: For each transaction and output it reports, the gateway MUST provide a proof tying it to a block header, and MUST provide the headers needed to check that proof, so the wallet can verify without trusting the gateway.
- **FR-012a**: On request, the gateway MUST prove a specific transaction at a given height, for any age that a full-history peer can still serve, with a small per-request limit (at most 20 transactions), so the wallet can upgrade explorer-sourced transactions to "checked" without a history scan. When it cannot, it MUST say so, and the wallet leaves the transaction as "unchecked". When a proof or the gateway's chain contradicts what the explorer reported for a transaction (it is not in the block the explorer named, or its amounts differ), the wallet MUST treat the gateway's proven data as correct, MUST keep the disputed transaction "unchecked" and out of the spendable balance until the disagreement is resolved, and MUST keep a local note of the dispute.
- **FR-013**: The wallet MUST verify those proofs against headers before treating a transaction as confirmed, MUST reject mismatches, and MUST show whether data is verified. The wallet MUST itself check the proof of work and cumulative work of the supplied headers, starting from checkpoints built into the wallet, and MUST NOT accept a header chain merely because the gateway supplied it. Delivery is staged. Until the wallet checks proof of work itself, data from the gateway MUST be labeled "checked" (header linkage, difficulty rules, merkle proof, and a match of the chain tip's block hash and a few recent block hashes against an independent explorer), MUST NOT be labeled "verified", and the gateway MUST NOT be configured above the explorers. "Checked" is consistency and cross-source agreement, not protection against a dishonest gateway, and the wallet MUST NOT present it as such. The "verified" label and promotion above the explorers are enabled only when the wallet's own proof-of-work check ships. If that check cannot be delivered for every active algorithm, the label stays "checked" and the gateway stays a backup: no partial proof-of-work mode earns "verified". A transaction MUST be shown as pending, with its current confirmation count, and MUST NOT count toward the spendable balance until it is at least 6 blocks deep in the verified chain. The one exception is change from a transaction this wallet signed and sent, which MUST be spendable immediately while still shown as pending.
- **FR-014**: The wallet MUST send addresses only. It MUST NOT send keys, seed material, recovery words or an extended public key to the gateway.
- **FR-015**: Requests MUST be limited in address count, scan range and rate per client, and over-limit requests MUST be refused with a clear reason. The gateway's read and broadcast interface is anonymous: it MUST NOT require a key, and MUST NOT rely on one for protection. It can only read public chain data and relay signed transactions.

**Integration and resilience**

- **FR-016**: The gateway MUST be an additional source in the wallet's existing source-selection chain, governed by the same health-and-height-check mechanism as feature 005, and MUST be switchable on or off per deployment without a code change. Its position in the chain (preferred first for reads, or backup after the explorers) MUST also be a per-deployment setting, defaulting to backup at launch.
- **FR-017**: The gateway MUST publish a health report giving status (starting, syncing, ok, unhealthy), header height, lag against peers, peer count, number of peers advertising filters, and matching mode. It holds no per-wallet state, so it reports no wallet counts.
- **FR-018**: The wallet MUST use the gateway only while it reports healthy and its height is within the allowed margin of an independent public explorer, and MUST return to the other sources otherwise.
- **FR-019**: Balance, receive, send and history MUST keep working through other sources whenever the gateway is not usable, with no new errors.
- **FR-020**: The wallet MUST show which source is active, a degraded-mode notice only when relevant, and a one-line note that the service sees the addresses it is asked about.

**Operations and security**

- **FR-021**: The gateway MUST NOT log full addresses, transaction contents or any secret, and MUST NOT store any wallet's address set between requests. The wallet resends its address list with each request, and the gateway MAY keep only a short-lived in-memory cache of match results, which expires within 30 seconds and is never written to durable storage.
- **FR-022**: The gateway MUST reject malformed, oversized and hostile input without affecting other users.
- **FR-023**: The gateway reads public chain data and relays signed transactions only. It MUST NOT be a participant in any signing flow.

### Key Entities

- **Chain view**: the gateway's verified picture of the header chain: heights, cumulative work, current tip and recent reorganizations.
- **Peer set**: the network peers the gateway is connected to, with their capabilities (such as serving block filters) and recent behavior.
- **Watched address set**: the addresses a wallet asks about in one request, the network they belong to, and the block height from which to look. It exists only for the length of that request and any short-lived result cache.
- **Match result**: the transactions and spendable outputs found for a watched set, each with its block and a proof.
- **Inclusion proof**: the evidence tying a transaction to a block header, plus the headers needed to check it.
- **Source health report**: the status, heights, peer count and mode the gateway publishes, used by the wallet's selection mechanism.
- **Restore progress**: how far a scan has gone, from what start height, so an interrupted restore can resume. It is kept by the wallet on the user's device, not by the gateway.
- **Wallet sync state**: the wallet's own record of its transactions and outputs, where each one came from (gateway-proven or explorer-sourced), and the height and block hash of its last completed refresh. It is kept on the device, never sent to the gateway, and is what lets a routine refresh ask only for new blocks.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With the gateway active, every confirmed transaction shown to the user has passed the wallet's proof and header checks (and, once the wallet's own proof-of-work check has shipped, is labeled "verified"), and any response whose proof fails is rejected in 100% of tested tamper cases.
- **SC-002**: A wallet with an established history shows balance and history within 3 seconds of opening on a typical connection, once the gateway is synced.
- **SC-003**: A signed transaction sent while all explorers and our own node are unreachable reaches the DigiByte network and confirms in at least 95% of attempts under normal network conditions.
- **SC-004**: A wallet restored from a seed with a known birth height finds 100% of funds within the gap limit, and the scan completes within 5 minutes for a wallet whose birth was within the last 30 days.
- **SC-005**: With the gateway stopped, syncing, or behind the chain, balance, receive, send and history behave as they do today, with zero new user-visible errors.
- **SC-006**: After the gateway has been healthy for the stability window, the wallet switches to it, and after it becomes unhealthy the wallet switches away, each within 3 minutes and without user action or data loss.
- **SC-007**: A review of gateway logs and health output after a test run shows no full addresses, transaction contents or secrets.
- **SC-008**: A chain reorganization of up to 6 blocks is reflected correctly in balance and history within one minute of the gateway seeing it.
- **SC-009**: The gateway stays responsive for other wallets while one wallet performs a deep restore scan.

## Assumptions

- The gateway is a new, independently deployable service. It is a public-chain relay, which the constitution permits, and it is never required for a signing flow.
- It runs on the project's hosting alongside the existing services and does not need our own node, but MAY use our own node as an additional trusted peer.
- The gateway sees the addresses it is asked about, as explorers and our own node do today. It cannot see keys or seed. The wallet tells the user this in plain language.
- The existing height cross-check against a public explorer (feature 005), plus the block-hash cross-check in FR-013, remains the defense against a gateway fed a false chain by an eclipsed peer set.
- In the first release, payments from other people are not visible through the gateway until they are in a block (about 15 seconds), because the gateway reports only transactions that are in blocks. Matching unconfirmed payments to addresses is a later addition behind the same interface. The wallet's own unconfirmed sends are known to the wallet and shown as pending.
- The wallet's existing address pool and gap limits (20 consecutive unused, deeper scan available) define what the wallet asks the gateway about.
- Sending the wallet's known unspent outpoints to the gateway reveals nothing beyond the addresses it already sees, because each outpoint belongs to an address the wallet asks about. The gateway handles outpoints under the same no-storage and no-logging rules as addresses.
- Full verification of every transaction by the wallet depends on the gateway supplying proofs and headers. Unconfirmed transactions cannot be verified and are labeled pending.
- Planning found that no sampled public peer serves compact block filters (8 reachable peers probed on 2026-11-15), so v1 matches by downloading blocks and the gateway builds its own wallet-independent block index. Peer-served or own-node filters are a later speed-up. Many peers are pruned, so only recent history is served directly (see research.md).
- Fee estimates from the gateway are advisory. The wallet's existing fee source remains authoritative until the gateway proves reliable.
- Out of scope: running the gateway inside the browser or a native app, serving compact filters to others, DigiDollar-specific lookups, multisig-specific proofs, and any wallet-to-wallet transport.
- Mainnet and testnet are supported. Regtest support is desirable for tests but not required for release.
