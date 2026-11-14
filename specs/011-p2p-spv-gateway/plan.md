# Implementation Plan: P2P Light-Client Gateway (SPV Source)

**Branch**: `011-p2p-spv-gateway` | **Date**: 2026-11-15 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/011-p2p-spv-gateway/spec.md`

## Summary

Add a small server, `DigiByte.SpvGateway`, that talks the DigiByte peer protocol to public peers, tracks and validates the header chain, and answers the wallet's address questions over plain web requests. Add the matching verification code to the lowest layer (`DigiByte.Crypto`): header-chain checks, per-algorithm proof of work, DigiByte's difficulty retarget rules and merkle proofs. Add `P2pGatewayBlockchainService` to the wallet as one more source in the existing fallback chain, governed by the same readiness monitor as feature 005.

Four research results shape the design (details in [research.md](research.md)):

1. **No public peer serves compact block filters.** A live probe of 8 reachable peers found none with the filter or bloom service bits. v1 therefore matches addresses by downloading blocks, and the gateway builds its own wallet-independent block index while it does so (R1, R2).
2. **Many peers are pruned**, so only peers advertising full history can serve old blocks (R2).
3. **DigiByte proof of work is multi-algorithm with per-algorithm retargeting.** The wallet cannot reuse Bitcoin's header checks. It needs ports of the per-algorithm hashes and the DigiByte retarget rules (R3). This is the biggest risk and drives the milestone order below.
4. **The gateway only knows the last 90 days.** A balance needs an address's whole history, so the wallet keeps its own sync state and asks the gateway only for new blocks. Wallets older than the window do their first refresh through the explorer path, and old transactions are proven one at a time on request (R10).

## Technical Context

**Language/Version**: C# 14 / .NET 10 (`global.json` SDK 10.0.201). Wallet code compiles to WebAssembly.

**Primary Dependencies**: NBitcoin 9.0.5 (already in `DigiByte.Crypto`) for the peer protocol client, transaction and block parsing. SQLite through `Microsoft.Data.Sqlite` (already used by `DigiByte.NodeApi`) for the gateway's header and block-index store. ASP.NET Core minimal APIs for the HTTP surface. No new third-party packages are planned for the hash ports.

**Storage**: Gateway: SQLite file (headers, per-block match index, peer book). Wallet: existing IndexedDB store (headers since checkpoint, scan progress, birth height, and per-wallet sync state with each transaction's origin, see data-model.md). Nothing about a wallet's addresses is stored by the gateway (Clarification 3).

**Testing**: xUnit, matching the existing test projects. New `tests/DigiByte.SpvGateway.Tests`. Additions to `tests/DigiByte.Crypto.Tests` and `tests/DigiByte.Wallet.Tests`. Real mainnet header vectors for every algorithm are test fixtures. An in-process fake peer drives the gateway tests.

**Target Platform**: Gateway: Linux container on the project's existing hosting (Railway), like the other services. Wallet: Blazor WebAssembly PWA.

**Project Type**: Web service plus changes to the library layers and the PWA.

**Performance Goals**: From the spec: balance and history within 3 seconds for an established wallet (SC-002), a 30-day-old restore within 5 minutes (SC-004), a reorganization reflected within one minute (SC-008). To be tuned against measured block sizes and peer throughput (see R2).

**Constraints**: No keys, seed or extended public key ever reaches the gateway. The wallet's CSP must allow the gateway origin (a security-relevant change, flagged in the PR). The gateway holds no per-wallet state. Wallet download of headers is bounded because the wallet starts from a recent built-in checkpoint, never from genesis.

**Scale/Scope**: DigiByte is at about block 24.5 million with 15-second blocks, which makes scanning from genesis through peers impractical (R2). Scope is therefore recent history by design, with an explicit fallback for older restores (see Spec Deltas).

## Spec Deltas (confirmed and applied to spec.md on 2026-11-15)

Planning found two places where the spec as first written could not be met literally. Both are now amended in `spec.md` (User Story 3, FR-008a, FR-013, Assumptions, and the post-planning clarification entries).

1. **User Story 3, scenario 2 (unknown birth date → scan from the start of the chain).** At 24.5 million blocks this is not achievable through peers. Proposed change: when the birth date is unknown or older than the gateway's scan limit, the gateway declines the range and the wallet restores through its existing explorer-based path, with a message saying so. Funds are never missed silently.
2. **FR-013 / Clarification 1 ("wallet checks proof of work itself") needs two delivery stages.** Porting the Qubit and Odocrypt hashes and DigiByte's retarget rules is real work with a risk of mistakes. Proposed change: milestone M1 ships the gateway as a backup source with header linkage, retarget checks, merkle proofs and the explorer cross-check, and the wallet labels data "checked", not "verified". The "verified" label and any promotion above backup wait for M2, which adds full per-algorithm proof of work in the wallet. This keeps the spec's promise intact: the word "verified" is only shown once it is true.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Result | Notes |
|---|---|---|
| I. Self-custody | Pass | Addresses only (FR-014). Seed, keys, xpub never sent. |
| II. In-browser cryptography | Pass | The gateway is a public-chain relay with no part in any signing flow (FR-023). All verification of what it returns happens in the wallet. |
| III. Test-first for crypto and wallet logic | Pass, applies in full | Header, PoW, retarget, merkle and balance-rule code lands with tests written first, including negative cases (tampered proof, wrong network, bad algorithm bits, truncated input, short chain). |
| IV. Boundary discipline | Pass | Shared verification code goes in `DigiByte.Crypto`, the lowest layer that owns the concept. `DigiByte.SpvGateway` references `DigiByte.Crypto` only and is independently deployable. `DigiByte.Wallet` gains a source class and no new project reference. No inverted dependency. |
| V. Resilience | Pass | Unreachable, syncing and behind states are defined (User Story 4, FR-018, FR-019). Gateway health never blocks balance, receive, send or history. |
| CSP | Flagged | `connect-src` must allow the gateway origin. Narrow, deployment-configured, and called out in the PR as security-relevant. |
| Auth boundary | Pass | Anonymous endpoints carry only public chain reads and broadcast of already-signed transactions, are rate- and size-limited, and cannot touch keys or merchant data (Clarification 4). |
| Secret hygiene | Pass | No secrets are held. Logs exclude full addresses and transaction contents (FR-021). |

No violations. The Complexity Tracking table is not needed.

### Post-design re-check (2026-11-15)

Re-evaluated after the data model, contract and quickstart were written, and after the spec amendments (90-day scan window, staged "checked" then "verified" labels, explorer fallback for old restores). No principle changed status. Design confirmations: the contract carries no keys or seed material and accepts addresses only by `POST` (I, FR-014); the gateway stores no per-wallet state (data-model, FR-021); `range_unavailable` and the staged labels keep core flows working when the gateway can't answer (V); shared verification code sits in `DigiByte.Crypto` (IV). The one flagged item stays the same: the gateway origin in the wallet's `connect-src`.

Second re-check after the refresh-model and dispute clarifications: the wallet-side sync state (`WalletSyncState`) is per-wallet data on the device and is never sent to the gateway, so Principle I is unaffected. The `knownOutpoints` field and `POST /v1/proofs` carry only data derived from addresses the wallet already sends, handled under the same no-storage and no-logging rules (II, FR-021). New wallet code (sync state, incremental refresh, proof upgrade, dispute handling) is covered by test-first tasks in the Wallet test project (III). Disputed transactions never count toward the spendable balance, explorer-sourced ones count only as they do today, and a gateway failure leaves the wallet on its other sources (V). No gate changed status.

## Project Structure

### Documentation (this feature)

```text
specs/011-p2p-spv-gateway/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── gateway-api.md   # Phase 1 output
├── probe.py             # Peer service-bit probe used for R1/R2 (reproducible)
├── crawl.py             # Peer crawler built on probe.py
├── checklists/requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks, not created here)
```

### Source Code (repository root)

```text
src/
├── DigiByte.Crypto/
│   └── Headers/                       # NEW: shared by wallet and gateway
│       ├── BlockHeader80.cs           # parse, algo bits, block hash
│       ├── PowAlgorithm.cs            # SHA256D, Scrypt, Skein, Qubit, Odocrypt (+ Groestl for old eras if needed)
│       ├── Hashers/                   # managed per-algorithm hash ports
│       ├── DigiByteRetarget.cs        # V1-V4 difficulty rules (ported from DigiByte consensus)
│       ├── HeaderChainVerifier.cs     # linkage, retarget, PoW, cumulative work from a checkpoint
│       ├── Checkpoints.cs             # built-in per-network anchors
│       └── MerkleProof.cs             # verify a tx against a header
├── DigiByte.SpvGateway/               # NEW: independently deployable service
│   ├── Peers/                         # DNS seeds, peer book, handshake, service-bit filtering
│   ├── Sync/                          # header sync, reorg handling, block fetch, tip tracking
│   ├── Index/                         # self-built per-block match index (wallet-independent)
│   ├── Matching/                      # IBlockMatcher: full-block now, peer filters later
│   ├── Broadcast/                     # relay signed tx to at least 4 peers, collect accept/reject
│   ├── Endpoints/                     # /v1 HTTP surface (contracts/gateway-api.md)
│   └── Health/                        # health report, limits, rate limiting
├── DigiByte.Wallet/
│   └── Services/
│       ├── P2pGatewayBlockchainService.cs   # IBlockchainService over the gateway + proof checks
│       ├── GatewayHealthHttpSource.cs       # feeds the existing NodeSourceMonitor
│       ├── GatewayHeaderStore.cs           # IndexedDB-backed header cache
│       ├── BirthHeightService.cs            # record/estimate birth height (FR-008a)
│       └── GatewaySpendRules.cs             # 6-block rule, own-change exception
│       ├── WalletSyncStateStore.cs          # per-wallet sync state (IndexedDB)
│       ├── GatewayIncrementalSync.cs        # refresh from last height, chunked requests, known outpoints
│       ├── GatewayProofUpgrader.cs          # upgrade explorer-sourced transactions via /v1/proofs
│       ├── GatewayStateBootstrap.cs         # record sync state after explorer-served refreshes
│       ├── GatewayHeaderSync.cs             # background header download and checks from the checkpoint
│       ├── GatewayProofChecker.cs           # merkle proof checks against checked headers
│       ├── GatewayRestoreScanner.cs         # chunked restore scan, explorer fallback
│       ├── ScanProgressStore.cs             # resumable restore progress
│       ├── GatewayCapabilities.cs           # flags for staged delivery (proof of work in the wallet)
│       ├── ExplorerBlockHashSource.cs       # tip and recent block hashes from an independent explorer
│       ├── P2pGatewayOptions.cs             # deployment settings (enabled, origin, position, proving)
│       ├── GatewayOwnSendRecorder.cs        # record the wallet's own broadcasts in the sync state
│   (Models/ WalletSyncState.cs, TransactionVerificationState.cs in src/DigiByte.Wallet/Models/)
└── DigiByte.Web/                      # wiring, source indicator, restore progress UI, CSP

tests/
├── DigiByte.Crypto.Tests/Headers/     # real header vectors per algorithm, retarget, merkle, negatives
├── DigiByte.SpvGateway.Tests/         # fake-peer integration, reorg, limits, privacy-of-logs
└── DigiByte.Wallet.Tests/             # source selection, proof rejection, spend rules, restore resume
```

**Structure Decision**: One new service project and one new Crypto folder. This follows the repository's existing layering: verification primitives go to the lowest layer, the service stands alone, and the wallet only gains a source class. The service does not reference `DigiByte.Wallet`, unlike `DigiByte.NodeApi`, because it needs none of the wallet's logic and the constitution prefers small, auditable boundaries.

## Milestones

- **M0 — Facts and vectors**: gather mainnet header fixtures per algorithm and era, confirm retarget parameters against DigiByte's `pow.cpp` and `chainparams.cpp`, and measure real block sizes and peer throughput. Decide checkpoint heights. Run a time-boxed feasibility spike on the Qubit and Odocrypt ports before the schedule depends on them (gateway header sync needs them from M1).
- **M1 — Backup source, "checked" data**: the per-algorithm hashers (used by the gateway to check proof of work from day one, FR-002), gateway sync and endpoints, broadcast, wallet source with linkage, retarget, merkle proofs and the explorer tip-hash cross-check, health monitoring, restore with chunked scans and the explorer fallback. Deployed as backup (Clarification 2). "Checked" is not protection against a dishonest gateway.
- **M2 — "Verified" data**: the wallet runs the per-algorithm proof-of-work check itself (under WebAssembly). Enables the verified label and lets the deployment promote the gateway above explorers.
- **M3 — Optional speedups**: peer-served filters if any appear, or filters from our own node, behind the existing `IBlockMatcher` seam.

## Complexity Tracking

No constitution violations. Not applicable.
