# Research: Wallet Uses Our Own Node When Ready

Items marked **(verify)** were not confirmed against running code and must be checked while implementing.

## R1 - Where the decision lives

- **Decision**: A `NodeSourceMonitor` in `DigiByte.Wallet` owns the state machine; `FallbackBlockchainService` only reads a small `INodeSourceState` (is the node `Active`? is the feature on?). The Web layer wraps the monitor for display.
- **Rationale**: The fallback service already holds the ordering logic and is the one place every read and broadcast passes through. Keeping the monitor in `DigiByte.Wallet` keeps it unit-testable with a fake clock and no browser (constitution III, IV).
- **Alternatives**: put the check inside `NodeApiBlockchainService` (it would need to know about explorers for the cross-check); put it in the UI layer (untestable, and core flows would depend on a component being mounted).

## R2 - State machine

- **Decision**: States `Disabled`, `Unavailable`, `NotReady(reason)`, `Stabilizing`, `Active`.
  - Each check yields a snapshot. It is **ready** only if: state `ok`, `txindex` true, `versionOk` true (or absent), and the node height is within 5 blocks of an explorer height.
  - Ready check -> `Stabilizing`; after the stable period of consecutive ready checks (default 120 s, with a 30 s interval = 4 checks) -> `Active`.
  - Any not-ready or failed check -> `NotReady`/`Unavailable` immediately, from any state (prompt fallback).
  - If no explorer height can be obtained, a node in `Active` stays `Active` for that round (do not drop on a missing explorer answer) and a node that is not yet `Active` stays where it is (FR-005a); never mark the node wrong because the explorer was silent.
- **Rationale**: Promotion is slow and demotion is instant, which is the stable choice for a data source whose wrong answers cost more than its absence. 30 s checks plus the 3 s probe meet the 35 s fallback target (SC-003); the 120 s stable period plus up to one 30 s interval means the switch happens 2 to 2.5 minutes after the node first reports ready, which meets the 3 minute target (SC-002, relaxed from 2 minutes in clarification so the stability window stays at 2 minutes).
- **Read-failure demotion (analysis U1)**: while `Active`, each node read gets a 5 second budget; a failed or over-budget read is answered by an explorer and reported to the monitor, and 3 consecutive failures demote the node immediately (`NotReady(ReadFailures)`), without waiting for the next health check. A success resets the count.
- **Alternatives**: symmetric hysteresis (a bad node would be trusted for another window), per-request health probing (adds latency to every read).

## R3 - Reads and sends while the feature is on

- **Decision**:
  - **Active**: reads try the node first, then the explorers for that request if the node fails (FR-003). Broadcast tries the node first, then the explorers.
  - **Any other state with the feature on**: reads and broadcasts use only the explorers. The node is **not** used as a last resort. A syncing node answers address lookups with incomplete data (for example a zero balance), which is worse than an error.
  - **Feature off**: exactly today's order (explorers first for reads, node last resort; node first for broadcast). No health calls.
- **Rationale**: FR-002, FR-004, FR-010 and SC-005 (no transaction first sent to a node that is not ready). Today's code tries the node first for every broadcast, even while syncing; this is the one existing behaviour that changes when the feature is on.
- **Alternatives**: keep the node as last resort when explorers fail (rejected for the wrong-data reason above); broadcast to node and explorer in parallel (risk of confusing duplicate-broadcast errors; spec keeps sequential fallback).

## R4 - CSP and per-deployment node address

- **Finding**: the wallet's CSP is a meta tag in `src/DigiByte.Web/wwwroot/index.html` and its `connect-src` lists only `localhost:5260` for a node, the two explorers, the price API and analytics. A Railway node API origin is blocked today. The current `wwwroot/appsettings.json` has `NodeApiUrl` set to `http://localhost:5260`, so production effectively has no usable node.
- **Decision**: Add the node API's exact origin to `connect-src` once, and recommend giving the node API a stable custom domain so the origin never changes. The runtime config can then switch the feature on/off and change thresholds without a rebuild; moving to a different origin needs a CSP edit (a reviewed, security-relevant change).
- **Rationale**: The meta CSP cannot be changed at container start without touching hashed PWA assets. A per-deployment CSP via a header is possible but moves a security control and an extra header cannot loosen the meta tag anyway.
- **Consequence for the spec**: "node address settable without a code change" holds within the origins the CSP allows. This is called out in the contract and was accepted explicitly by the maintainer on 2026-10-29 (see plan Complexity Tracking).
- **Alternatives**: wildcard on `*.up.railway.app` (rejected, too broad); remove the meta tag and use only an nginx header with a templated value (rejected for this feature, larger security change).

## R5 - Runtime configuration without a rebuild

- **Finding**: the Web container serves static files with nginx; `nginx.conf` marks every `*.json` as cached for a year and immutable. Blazor's PWA service worker also keeps an asset manifest with hashes, so editing published files at container start would break it.
- **Decision**: A new `config.json`, written at container start by a script in `/docker-entrypoint.d/`, from variables such as `WALLET_NODE_ENABLED`, `WALLET_NODE_API_URL`, `WALLET_DEV_VIEW`. `nginx.conf` gets an exact-match location for `/config.json` with no-cache headers, placed so it wins over the generic `*.json` rule. `Program.cs` fetches it before building services and merges it into configuration; a missing or invalid file means defaults (feature off), which is the safe default. **(verify)** the service worker does not cache `config.json` forever (it must not be in the precached manifest, and network-first or bypass applies).
- **Rationale**: Deployment-only change (FR-009, SC-006) without touching hashed build output.
- **Alternatives**: bake settings into `appsettings.json` at build (needs a rebuild); query string or local storage settings (not a per-deployment control, user-tamperable).

## R6 - Health check client

- **Decision**: A dedicated `HttpClient` for `/api/health` with a 3 second timeout and no retries. The existing `NodeApi` client (2 retries, 8 s per attempt, 20 s total) is too slow for a probe.
- **Rationale**: FR-006 and SC-001: a dead node must never delay a screen. The probe runs in the background and never on a user's request path.

## R7 - Explorer height for the cross-check

- **Decision**: Reuse an explorer service's existing block-height call directly (not the cached fallback wrapper), once per check, trying explorers in their usual order. The tolerance is 5 blocks either way.
- **Rationale**: Cheap, already implemented, and independent of the node. Catches a node that is stale or on the wrong chain while claiming `ok`.
- **Time budget (analysis T1)**: the explorer client's own retry policy allows up to 45 seconds, so the lookup is cancelled after 5 seconds and counts as "no answer", and it runs in parallel with the node probe. A whole check therefore takes at most the probe timeout plus 5 seconds.
- **Risk**: explorers can themselves lag by a block or two; 5 blocks leaves room (about 75 seconds of chain time).

## R8 - What counts as "degraded" for the banner

- **Finding**: with the feature on, the node will spend days as `syncing`. A banner for every user during that time would be noise, and the spec wants a banner only for degraded states.
- **Decision**: The banner shows only when the node **was** `Active` this session and dropped back (an unexpected fallback), or when **all** sources are failing. A node that has never become ready, or is still syncing, is shown in the status detail only ("Own node: syncing"). Health-related states are informational unless something that was working stops working.
- **Rationale**: Matches FR-011/FR-012 and SC-007 while keeping the wallet calm. Accepted by the maintainer on 2026-10-29.

## R9 - Status UI placement

- **Decision**: A small indicator (a dot) in the existing layout opens an on-demand detail panel; a banner component next to the existing offline banner; no permanent source label on the main screen (spec clarification). The detail panel carries the one-line privacy note when the own node is active (FR-014a).
- **Rationale**: Reuses the layout patterns already used for the offline banner.
- **Language**: new strings are English only and centralised in the status mapper (the offline banner is English only too); adding `en`/`es` locale keys later is a copy change, not a logic change.

## R10 - Developer view and comparison

- **Decision**: A page present only when `WALLET_DEV_VIEW` is on, showing the latest readiness snapshot and a comparison of balance, spendable outputs and height for entered addresses, node versus explorer. It calls the node directly for the comparison, regardless of the monitor state, and never changes what regular views show.
- **Rationale**: FR-013, User Story 5. Without it we cannot test against a syncing node before cut-over (FR-016).

## R11 - Testing against a syncing or controllable node

- **Decision**: Unit tests use fakes (clock, health source, explorer height). For manual testing a tiny stub health endpoint (a few lines in the quickstart) returns each state, so every transition can be exercised without waiting for the real node. The real node is used for the final comparison.
- **Rationale**: FR-016.

## R12 - Dependencies on feature 004

- The wallet depends on the node API address, the `/api/health` contract (`status`, `blocks`, `headers`, `lag`, `peers`, `txindex`, `versionOk`) and on the node API being allowed to answer browser requests (it answers any origin on public read routes today, see 004 research). The health endpoint has no secrets and no auth, so the browser can call it.
