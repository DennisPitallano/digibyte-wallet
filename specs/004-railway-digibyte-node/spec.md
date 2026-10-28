# Feature Specification: Railway-Hosted DigiByte Core Node

**Feature Branch**: `feature/railway-digibyte-node` (spec directory `004-railway-digibyte-node`)

**Created**: 2026-10-28

**Status**: Draft

**Input**: User description: "Help me check the latest DigiByte core node, how we can deploy it now on Railway with my current plan in a new project, and use it and access it via our DGB wallet and other apps we'll be building that might need it."

## Findings From Initial Research

- **Latest DigiByte Core release**: v9.26.7 (published 2026-10-06). It is a small follow-up to v9.26.6 that fixes slow chain-info requests and a false fatal error on shutdown. A normal upgrade from v9.26.6 needs no reindex.
- **Repo is behind**: the existing node image in this repo pins v8.26.2 (the pre-DigiDollar generation). All full nodes must run v9.26.6 or newer before the mainnet "Thaw Day" rule change at block 24,490,000.
- **Pruning caveat**: v9.26.x requires the DigiDollar block history to be retained, and the transaction index is on by default since v9.26.3. The current repo mainnet config (550 MB prune, no transaction index) is therefore not a safe default for the new deployment.
- **Railway plan (Pro)**: $20 monthly usage credit, up to 1 TB storage, up to 32 vCPU / 32 GB RAM per replica, private networking between services in a project. Usage beyond the credit is billed.

## Clarifications

### Session 2026-10-28

- Q: Should the Railway node keep the full blockchain history with a transaction index, or run pruned? → A: Full node with transaction index (no pruning).
- Q: Should the first release include the access gateway for apps outside the Railway project? → A: No; private access only, gateway deferred to a later feature.
- Q: Where should the wallet node-API run relative to the node? → A: Move (or duplicate) it into the new project next to the node; the wallet API keeps calling it over its public URL.
- Q: What is the monthly budget for the node? → A: Up to $40/month total (including the $20 Pro credit).
- Q: If the full node's real cost exceeds $40/month, what happens? → A: $40 is a target; up to $60/month is accepted, with a cost alert at $40 and a hard usage limit at $60.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run a current, synced DigiByte node in its own Railway project (Priority: P1)

The operator creates a new, separate Railway project and deploys the latest DigiByte Core release as a long-running service with durable storage. The node syncs to the tip of mainnet and stays there, surviving restarts and redeploys without re-downloading the chain.

**Why this priority**: Nothing else (wallet reads, broadcasting, other apps) works until a healthy, current node exists. It is also the only piece that carries ongoing hosting cost and upgrade risk.

**Independent Test**: Deploy the project, wait for sync, then check that the node reports the latest release version and a block height matching public explorers, and that a forced redeploy resumes from saved chain data rather than starting over.

**Acceptance Scenarios**:

1. **Given** a new Railway project and the latest release version selected, **When** the operator deploys, **Then** the node starts, begins syncing, and its reported version equals the selected release.
2. **Given** the node is fully synced, **When** the service is restarted or redeployed, **Then** it resumes within minutes from persisted data and returns to the chain tip without a full resync.
3. **Given** the node is synced, **When** the operator compares its block height to a public explorer, **Then** they match within a few blocks.
4. **Given** the node is syncing, **When** the operator views service status, **Then** sync progress is visible and the service is not marked unhealthy merely for still syncing.

---

### User Story 2 - Wallet and other apps reach the node privately and safely (Priority: P1)

The DGB wallet's server-side API and future apps (e.g. DigiPay) query the node for chain data and broadcast transactions. The node itself is reached only over the project's private network; the wallet's browser app reaches the node through NodeApi's public HTTPS address. The node's control interface is never exposed to the public internet; only the peer-to-peer port may be public so the node can accept peers.

**Why this priority**: The node is only useful if consumers can use it, and an exposed, password-protected control interface on a wallet-adjacent node is a serious security risk.

**Independent Test**: From a consumer service in the same project, read the current block height and broadcast a known-valid test transaction. From the public internet, confirm the control interface is unreachable.

**Acceptance Scenarios**:

1. **Given** the node is synced and a consumer service is configured with the node's internal address and credentials, **When** the consumer requests the current block height, **Then** it receives the correct value.
2. **Given** the same setup, **When** the consumer broadcasts a valid signed transaction, **Then** the node accepts it and it propagates to the network.
3. **Given** the deployment, **When** anyone on the public internet attempts to reach the node's control interface, **Then** the connection is refused or times out.
4. **Given** wrong or missing credentials, **When** any client calls the node, **Then** access is denied.
5. **Given** the wallet node-API service relocated into the new project and the wallet API repointed to it, **When** users use the wallet, **Then** wallet features that depend on the node (balance, UTXOs, broadcast, DigiDollar reads) work as before.

---

### User Story 3 - Share the node across several apps with controlled access (Priority: P2, deferred to a later feature)

The operator wants one node to serve the wallet and later apps living in other Railway projects or hosts. A single, narrow, authenticated entry point exposes only the node operations those apps need, so each app can be granted or revoked access independently.

**Why this priority**: Raises the value of one node across the product family, but the wallet works without it using private access only.

**Independent Test**: Issue an access credential to a test app outside the project; confirm it can perform allowed reads, is denied disallowed operations (wallet management, node shutdown), and stops working once revoked.

**Acceptance Scenarios**:

1. **Given** an app outside the node's project with a valid access credential, **When** it requests an allowed read, **Then** it succeeds over an encrypted connection.
2. **Given** the same app, **When** it attempts a restricted operation (key/wallet management, stop, config change), **Then** it is denied.
3. **Given** a credential has been revoked, **When** the app retries, **Then** it is denied without any change to the node itself.
4. **Given** an app exceeds a reasonable request rate, **When** it continues, **Then** it is throttled and other apps are unaffected.

---

### User Story 4 - Know it is healthy, current and affordable (Priority: P2)

The operator can see at a glance whether the node is up, synced, connected to peers, running the recommended version, and what it costs against the Pro plan credit. Upgrades to future releases follow a documented, low-risk procedure.

**Why this priority**: A chain-following node silently falling behind or running an outdated version (especially before a rule-change height) breaks wallets without obvious errors.

**Independent Test**: Stop the node briefly and confirm the operator is alerted; follow the upgrade runbook to move to a newer release on a staging copy and confirm no resync is needed.

**Acceptance Scenarios**:

1. **Given** the node falls behind the public chain by more than a set threshold or loses all peers, **When** the check runs, **Then** the operator is notified.
2. **Given** a new DigiByte Core release is published, **When** the operator follows the upgrade runbook, **Then** the node is upgraded after verifying the release checksum, with data backed up first and downtime limited to a restart.
3. **Given** a month of operation, **When** the operator reviews usage, **Then** the node's cost is known and compared to the plan credit, with disk growth projected.

---

### Edge Cases

- The storage volume fills up as the chain grows: the operator is warned before exhaustion and can enlarge the volume.
- The node is redeployed mid-sync or mid-write: data stays consistent and recovers on its own; a documented fallback exists if a reindex is required.
- The node runs a version older than the mandatory rule-change release as mainnet approaches block 24,490,000: a visible warning prompts an upgrade.
- The public peer port is closed or throttled by the platform: the node still syncs outbound but inbound peer count is low; documented as acceptable.
- Credentials leak: rotating them requires only a configuration change and consumer updates, no resync.
- The node is temporarily down: the wallet shows its existing graceful-degradation behaviour (public-explorer fallback where one exists) rather than errors.
- Consumers on the private network start before the node is ready: they retry rather than crash.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The deployment MUST run in a new Railway project separate from the existing wallet and DigiPay projects.
- **FR-002**: The node MUST run the latest stable DigiByte Core release (currently v9.26.7), with the version set by a single build setting and verifiable against the published release checksum (the recommended version used by the health check is a second value, updated alongside it on each upgrade).
- **FR-003**: Chain data MUST persist on durable storage across restarts and redeploys, sized for the full chain plus growth headroom within the plan's storage limit.
- **FR-004**: The node MUST run unpruned with the transaction index enabled, retaining all block history (including DigiDollar data), so any historical transaction can be looked up.
- **FR-005**: The node's control interface MUST NOT be reachable from the public internet; consumers in the project MUST reach it via private networking.
- **FR-006**: The node MUST require strong, secret credentials that are not stored in the repository; defaults such as "changeme" MUST NOT be accepted by the Railway deployment (local development files may keep their placeholders).
- **FR-007**: Only the peer-to-peer port MAY be publicly reachable, to allow inbound peers.
- **FR-008**: The wallet node-API MUST run in the new project alongside the node and reach it over the private network; the wallet (web app and any backend that calls it) MUST be repointed to the relocated node-API by configuration change only, with no wallet code change.
- **FR-009**: The node MUST expose a health signal distinguishing "starting/syncing", "healthy and current", and "unhealthy".
- **FR-010**: The first release MUST reach the node only through NodeApi (the public HTTPS surface exposing allow-listed operations) or from services inside the node's Railway project over the private network. A dedicated access point for outside apps (allow-listed operations, per-app revocable credentials, rate limiting, encryption) is deferred to a later feature; this release MUST NOT preclude adding it.
- **FR-011**: The operator MUST be notified when the node is down, far behind the chain tip, peerless, low on disk, or running a version older than the recommended one.
- **FR-012**: A written runbook MUST cover first deploy, credential rotation, upgrade to a new release (with backup and checksum verification), recovery/reindex, and teardown.
- **FR-013**: The deployment MUST stay within the Pro plan's resource limits and the $40/month target budget (hard ceiling $60/month, enforced by a usage limit); the expected monthly cost MUST be documented against the $20 included credit.
- **FR-014**: The node MUST NOT hold user keys or wallets; it serves only public chain data and relays signed transactions, consistent with the project's self-custody principle.
- **FR-015**: Consumer apps MUST keep working in degraded mode (existing fallbacks) if the node is unavailable.

### Key Entities

- **Node Service**: The running DigiByte Core instance; attributes: version, sync state, block height, peer count, health status.
- **Chain Data Volume**: Durable storage holding the blockchain; attributes: size, free space, growth rate.
- **Node Credentials**: Secret used by consumers to authenticate; rotated independently of the node.
- **Consumer App**: A wallet service or other app using the node; attributes: location (in-project or external), permitted operations, access credential, status.
- **Access Gateway** (optional): Narrow entry point for external consumers; attributes: allow-listed operations, rate limits, per-app credentials.
- **Runbook**: Operator procedures for deploy, upgrade, rotate, recover.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator following the runbook can bring a new project from empty to a fully synced node in a single session, with initial sync completing within 24 hours.
- **SC-002**: After any restart or redeploy, the node is serving correct data again within 10 minutes without a full resync.
- **SC-003**: The node's block height stays within 3 blocks of public explorers at least 99% of the time over a month.
- **SC-004**: Zero node control endpoints are reachable from the public internet, verified by an external check.
- **SC-005**: Wallet balance, UTXO, broadcast and DigiDollar reads work against the new node with no user-visible difference versus current behaviour.
- **SC-006**: The operator is alerted within 10 minutes of the node being down or more than 10 blocks behind.
- **SC-007**: Upgrading to a new release takes under 30 minutes of operator effort and under 10 minutes of node downtime.
- **SC-008**: Steady-state total monthly cost for the new project targets $40 (including the $20 Pro credit) and never exceeds $60; the operator is alerted when projected spend passes $40, and spending is hard-capped at $60.
- **SC-009**: A new in-project app can be pointed at the node by configuration alone in under 15 minutes.

## Assumptions

- The target is DigiByte **mainnet**; a testnet instance may be added later but is not in scope.
- The node runs as a **full (unpruned) node with transaction index** (confirmed in clarification). This implies several tens of GB of storage and a higher monthly cost than the current pruned config; pruning is out of scope.
- The existing repo assets (node image definition, node-API service, Railway notes) are the starting point and will be updated rather than replaced.
- The operator has Pro plan Railway access with permission to create projects, volumes and private networking.
- Wallet and DigiPay services that need the node run in (or are moved into) the node's project; cross-project access via a gateway (User Story 3) is out of scope for this release, and the raw control interface is never exposed.
- Alert delivery uses a channel the operator already uses (email or chat); the specific channel is a planning detail.
- Railway's public TCP proxy may limit inbound peer connectivity; outbound sync is sufficient for the purposes here.
- Mining, staking and wallet hosting on the node are out of scope.
