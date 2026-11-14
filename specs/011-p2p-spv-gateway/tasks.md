---

description: "Task list for the P2P light-client gateway (SPV source)"
---

# Tasks: P2P Light-Client Gateway (SPV Source)

**Input**: Design documents from `/specs/011-p2p-spv-gateway/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/gateway-api.md, quickstart.md

**Tests**: Included, and required. The constitution (Principle III) makes tests a merge gate for every change in `DigiByte.Crypto/` and `DigiByte.Wallet/`, including negative cases. Gateway tests follow the same rule. Write each test first and see it fail before the code it covers.

**Organization**: Tasks are grouped by user story. Stage 2 ("verified" label) is its own phase because it gates the label and the gateway's promotion above the explorers (spec FR-013, plan milestone M2).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: US1 to US5, matching the user stories in spec.md
- Paths are relative to the repository root

## Path Conventions

Per plan.md: shared verification code in `src/DigiByte.Crypto/Headers/`, the new service in `src/DigiByte.SpvGateway/`, wallet source classes in `src/DigiByte.Wallet/Services/`, UI wiring in `src/DigiByte.Web/`, tests in the matching `tests/` project.

---

## Phase 1: Setup and M0 Research

**Purpose**: Create the projects and settle the facts the design still depends on (plan milestone M0).

- [X] T001 Create `src/DigiByte.SpvGateway/DigiByte.SpvGateway.csproj` (Microsoft.NET.Sdk.Web, net10.0, nullable and implicit usings on, reference `DigiByte.Crypto` only, `Microsoft.Data.Sqlite` 10.0.11) and a minimal `Program.cs` that starts and answers `/v1/health` with a placeholder
- [X] T002 Create `tests/DigiByte.SpvGateway.Tests/DigiByte.SpvGateway.Tests.csproj` (xUnit, same package set as `tests/DigiByte.NodeApi.Tests`), referencing `DigiByte.SpvGateway`
- [X] T003 Add both new projects to `DigiByte.slnx` and `src/src.sln`
- [X] T004 [P] Add `docker/spv-gateway/Dockerfile` modeled on `docker/node-api/Dockerfile`, and an `spv-gateway` service to `docker-compose.yml`, `docker-compose.testnet.yml` and `docker-compose.mainnet.yml`
- [X] T005 [P] Re-run `specs/011-p2p-spv-gateway/crawl.py` at larger scale (multiple seed peers, more addresses). Extend it to count peers with the full-history service bit and peers with the compact-filter bit. Record the numbers and date in `specs/011-p2p-spv-gateway/research.md` under R1
- [X] T006 [P] Check whether our own node serves filters: run `digibyted -help` from `docker/digibyted` and test `blockfilterindex` and `peerblockfilters` on a testnet node. Record the result in `specs/011-p2p-spv-gateway/research.md` under R1 (decides whether M3 is worth doing)
- [X] T007 [P] Confirm DigiByte's real DNS seeds from DigiByte Core `chainparams.cpp`, verify each resolves, and update the mainnet seed list in `src/DigiByte.Crypto/Networks/DigiByteNetwork.cs` (it lists only `seed1.digibyte.io`, which did not resolve during research). The testnet list there is empty, so also add testnet seeds or a built-in known-peer list for testnet, without which a testnet gateway has no peers (needed by T116). Add tests in `tests/DigiByte.Crypto.Tests` that assert both seed lists are non-empty and have no duplicates
- [X] T008 Collect real mainnet header fixtures from our node for every active algorithm (SHA256D, Scrypt, Skein, Qubit, Odocrypt) around and after the checkpoint heights chosen in T010, including a spread of Odocrypt key epochs, plus a short testnet run. Save as `tests/DigiByte.Crypto.Tests/Headers/Fixtures/headers-<algo>.json` with height, raw 80-byte hex, expected hash, expected nBits, and the merkle branch plus raw transaction for at least five transactions. Depends on T010 for the heights
- [X] T009 [P] Measure the average block size and per-peer block download throughput on mainnet, then record chunk size, index window sizing and expected first-sync time in `specs/011-p2p-spv-gateway/research.md` under R2
- [X] T010 [P] Choose checkpoint heights for mainnet and testnet: after the last retarget era change (`workComputationChangeTarget` in DigiByte Core `pow.cpp`) and recent enough that the wallet downloads megabytes of headers, not gigabytes. Record height, hash and retarget state needed (the per-algorithm recent-block window) in `specs/011-p2p-spv-gateway/research.md` under R3
- [X] T011 Time-boxed feasibility spike (about 3 days, depends on T008 for fixtures): port Qubit and Odocrypt in a scratch project under `tools/hash-spike/` and check each against the T008 fixtures. Record in `specs/011-p2p-spv-gateway/research.md` under R3 whether each port matches the fixtures, the effort left, and the per-header cost natively and under WebAssembly. Decision gate before T022 to T025: if either port is not feasible, stop and bring the spec back for a decision. FR-002 requires the gateway to check proof of work, so an alternative such as taking headers from our own node would need a spec amendment RESULT: all four remaining ports were feasible and are done (Skein, Odocrypt, Qubit and the shared AES round); the only decision left from the spike is the WebAssembly speed, measured in T098. Natively all 316 real headers after the checkpoint, every algorithm, take about 260 ms to check.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared verification primitives, the gateway's peer and header layer, and the wallet's plumbing for a second source. No user story can start before this is done, except that the Qubit and Odocrypt ports (T022 to T025) only gate the gateway's header sync (T033), its wiring (T038) and what depends on them.

**CRITICAL**: Complete all of Phase 2 before any user story, with the one exception in the Dependencies section: wallet and Crypto work may proceed against fixtures and the fake peer while the hasher ports (T022 to T025) are in progress.

### Tests first (Crypto)

- [X] T012 [P] Write `tests/DigiByte.Crypto.Tests/Headers/BlockHeader80Tests.cs`: parse a real header, algorithm read from the version bits (mask `15 << 8`: Scrypt `0 << 8`, SHA256D `2 << 8`, Groestl `4 << 8`, Skein `6 << 8`, Qubit `8 << 8`, Odocrypt `14 << 8`), reject an unknown algorithm value, reject input that is not exactly 80 bytes
- [X] T013 [P] Write `tests/DigiByte.Crypto.Tests/Headers/MerkleProofTests.cs`: accept five real transactions from the T008 fixtures, reject after changing one byte of the branch, the index, or the transaction, reject a branch of the wrong length
- [X] T014 [P] Write `tests/DigiByte.Crypto.Tests/Headers/HeaderChainVerifierTests.cs`: accept a real run of headers from the checkpoint, reject a broken previous-hash link, reject a header whose difficulty does not match the retarget rule, reject a chain that does not start at the checkpoint hash, reject a chain with less cumulative work than the current one
- [X] T015 [P] Write `tests/DigiByte.Crypto.Tests/Headers/PowHasherTests.cs` using the T008 fixtures: each algorithm's hash of a real header equals the fixture's expected hash, a header with one changed byte fails, a hash above its target fails (All five active algorithms are covered: SHA256D, Scrypt, Skein, Qubit and Odocrypt.)

### Implementation (Crypto)

- [X] T016 [P] Implement `src/DigiByte.Crypto/Headers/PowAlgorithm.cs`: the algorithm enum and the version-bit mask and constants listed in T012, with a `TryFromVersion` that returns false for an unknown value
- [X] T017 Implement `src/DigiByte.Crypto/Headers/BlockHeader80.cs`: parse and serialize 80 bytes, block hash, algorithm (depends on T016)
- [X] T018 [P] Implement `src/DigiByte.Crypto/Headers/MerkleProof.cs`: verify a transaction id plus index plus branch against a header's merkle root
- [X] T019 Implement `src/DigiByte.Crypto/Headers/DigiByteRetarget.cs`: the per-algorithm difficulty rule used at and after the checkpoint (`GetNextWorkRequiredV4` in DigiByte Core `pow.cpp`: `nAveragingTargetTimespanV4`, `nMinActualTimespanV4`, `nMaxActualTimespanV4`, `nLocalTargetAdjustment`). Refuse, with a clear error, to verify any header below the checkpoint (depends on T017, T010)
- [X] T020 [P] Implement `src/DigiByte.Crypto/Headers/Checkpoints.cs`: built-in anchors per network from T010
- [X] T021 Implement `src/DigiByte.Crypto/Headers/HeaderChainVerifier.cs`: link check, retarget check, cumulative work, anchored at the checkpoint, with an `IPowHasher` seam. The gateway always supplies the real hashers (T022 to T025), so it never follows a chain whose proof of work it has not checked (spec FR-002). The wallet's default stays "proof of work not checked" until Phase 8 so callers know which stage they are on (depends on T017, T019, T020) RESULT: link, algorithm, retarget, median-time, proof-of-work and cumulative work are implemented and tested (`HeaderChain`, `BlockWork`). Block work follows DigiByte Core's geometric-mean formula (`GetBlockProofImpl`, `ApproxNthRoot`); it has property tests but is not yet checked against a node's `chainwork` values, which needs a synced DigiByte node.
- [X] T022 [P] Implement `src/DigiByte.Crypto/Headers/Hashers/Sha256dHasher.cs` and `ScryptHasher.cs` (using NBitcoin's primitives where they match DigiByte's parameters; confirm with the fixtures)
- [X] T023 [P] Implement `src/DigiByte.Crypto/Headers/Hashers/SkeinHasher.cs`
- [X] T024 [P] Implement `src/DigiByte.Crypto/Headers/Hashers/QubitHasher.cs` (the chained hash sequence from DigiByte Core's hash library, verified against fixtures)
- [X] T025 [P] Implement `src/DigiByte.Crypto/Headers/Hashers/OdocryptHasher.cs` including the key derived from the block time (`OdoKey`), verified against fixtures from several key epochs

### Gateway foundation

- [X] T026 [P] Implement `src/DigiByte.SpvGateway/SpvGatewayOptions.cs`: network, scan window days (default 90, operator-adjustable), at most 100 addresses per request, at most 2000 headers per request, scan range cap (default 50,000 blocks), rate limits, peer targets. Out-of-range values fall back to defaults like `NodeSourceOptions`
- [X] T027 [P] Write `tests/DigiByte.SpvGateway.Tests/Support/FakePeer.cs`: an in-process peer that speaks the handshake, `getheaders`, `getdata` for blocks, `tx` relay and `reject`, and can serve a scripted chain including a reorganization
- [X] T028 [P] Write `tests/DigiByte.SpvGateway.Tests/Peers/PeerManagerTests.cs`: connects to at least 8 fake peers, prefers peers with the full-history service bit for old blocks, drops a peer that sends an invalid header, backs off failing peers, keeps no more than 2 peers from the same /16 address range, aims for at least 8 peers from at least 4 distinct ranges, and reports not healthy below 4 peers (spec FR-001)
- [X] T029 [P] Implement `src/DigiByte.SpvGateway/Peers/PeerBook.cs`: the PeerRecord fields from data-model.md (address and port unique together, services, last seen, last good, failure count, misbehavior) in SQLite
- [X] T030 Implement `src/DigiByte.SpvGateway/Peers/PeerManager.cs`: DNS seeds plus a built-in address list, handshake with NBitcoin's networking classes on the existing DigiByte network, keep at least 8 connections when reachable, enforce the /16 diversity rule from T028, rank by service bits and behavior (depends on T026, T027, T028, T029) RESULT: done. Connection policy (target, /16 diversity, backoff, ban, full-history preference, health snapshot) is tested with the in-process `FakePeerNetwork`; `NBitcoinPeerConnector` does the real handshake (protocol 70019 is required: lower versions are dropped by DigiByte nodes) and DNS discovery, tested against a loopback `NodeServer` and, with `DGB_LIVE=1`, against real mainnet peers.
- [X] T031 [P] Write `tests/DigiByte.SpvGateway.Tests/Sync/HeaderSyncTests.cs`: sync from the checkpoint, reject a header with a broken link, reject a header whose hash does not meet its target, handle a 6-block reorganization by flipping the best chain in one transaction and recording the depth, keep a losing branch stored
- [X] T032 Implement `src/DigiByte.SpvGateway/Sync/HeaderStore.cs` (SQLite) with the HeaderRecord rules verbatim: "Hash: unique", "PrevHash: must match the stored hash at Height-1 on the same branch", "Algo: must be a known algorithm", `ChainWork` cumulative, `IsBestChain` flag (depends on T017)
- [ ] T033 Implement `src/DigiByte.SpvGateway/Sync/HeaderSync.cs`: download headers from the checkpoint, validate with `HeaderChainVerifier` using the real hashers from T022 to T025, follow the most-work chain, switch branches on reorganization (depends on T021, T030, T031, T032, T022 to T025) PROGRESS: checking, storing, best-chain logic and the download from peers (`HeaderDownloader`, `PeerSyncService`) are done and tested; with `DGB_LIVE=1` the gateway synced 957 real mainnet headers from the checkpoint to the tip with full proof-of-work checks and 0 peers dropped. Remaining: testnet checkpoint.
- [X] T034 [P] Write `tests/DigiByte.SpvGateway.Tests/Endpoints/ErrorAndLimitTests.cs`: error shape and codes from the contract (`bad_request`, `wrong_network`, `over_limit`, `rate_limited`, `range_unavailable`, `not_ready`, `internal`), per-client rate limiting, wrong-network address refused with `400 wrong_network` (spec FR-004, FR-015, FR-022)
- [X] T035 Implement `src/DigiByte.SpvGateway/Health/ClientLimiter.cs` (a small limiter inside the gateway project; do not reference `DigiByte.Server.Shared`, to keep the gateway depending on `DigiByte.Crypto` only) and a JSON error middleware in `src/DigiByte.SpvGateway/Endpoints/ErrorMiddleware.cs` (depends on T034)
- [X] T036 Implement `src/DigiByte.SpvGateway/Health/HealthReporter.cs` and `src/DigiByte.SpvGateway/Endpoints/HealthEndpoints.cs` for `GET /v1/health` per the contract (status, network, header height, lag, peer count, full-history peer count, filter peer count, matching mode, indexed-from height, build version). Write `tests/DigiByte.SpvGateway.Tests/Endpoints/HealthEndpointTests.cs` first (spec FR-017)
- [X] T037 Implement `src/DigiByte.SpvGateway/Endpoints/HeaderEndpoints.cs` for `GET /v1/headers?from=&count=`: binary 80-byte records, "count is at most 2000", headers `X-Tip-Height` and `X-Tip-Hash`, `range_unavailable` below the earliest stored header. Test in `tests/DigiByte.SpvGateway.Tests/Endpoints/HeaderEndpointTests.cs` first
- [ ] T038 Wire everything in `src/DigiByte.SpvGateway/Program.cs` (options, SQLite paths, hosted services for peers and sync, limiter, endpoints) (depends on T030, T033, T035, T036, T037) PROGRESS: options, header store and sync, peer book and manager, header download service, rate limiter, error middleware, health and header endpoints are wired in `Program.cs` (`SpvGateway:PeersEnabled=false` turns the peer loop off; tests do). The block index, indexer and scan/utxo endpoints are wired too; broadcast, tx and proof endpoints are not.

### Wallet foundation

- [X] T039 [P] Write `tests/DigiByte.Wallet.Tests/P2pGatewayOptionsTests.cs`, then implement `src/DigiByte.Wallet/Services/P2pGatewayOptions.cs` read from a `SpvGateway` configuration section: enabled (default off), API origin (https, http only for local), position `first` or `backup` (default `backup`), height tolerance, stable period, health timeout. Invalid values fall back to defaults like `NodeSourceOptions`, plus `BackgroundProving` (default off, enables proof requests for explorer-sourced transactions while the gateway is only a backup)
- [X] T040 [P] Write `tests/DigiByte.Wallet.Tests/GatewayHealthHttpSourceTests.cs`, then implement `src/DigiByte.Wallet/Services/GatewayHealthHttpSource.cs`: map `GET /v1/health` to the shape the existing monitor consumes (read `NodeHealthHttpSource.cs` and `NodeSourceTypes.cs` first). Unreachable or malformed answers map to "unreachable", never to "ok"
- [X] T041 (Done by adapter: gateway health maps to NodeHealthResult and the unchanged NodeSourceMonitor judges it.) Generalize `src/DigiByte.Wallet/Services/NodeSourceMonitor.cs`, `NodeReadiness.cs` and `NodeSourceTypes.cs` minimally so a second instance can watch the gateway without node-only fields leaking in. Extend `tests/DigiByte.Wallet.Tests` with the existing node tests still green plus a gateway instance test (promote after the stable period, demote after three read failures)
- [X] T042 Add a `GatewayCapabilities` class in `src/DigiByte.Wallet/Services/GatewayCapabilities.cs` with `WalletPowVerified = false` (flipped only in Phase 8). Test in `tests/DigiByte.Wallet.Tests/GatewayCapabilitiesTests.cs` that `Position=first` is ignored and treated as `backup` while the flag is false (spec FR-013: no promotion above the explorers before the wallet's own proof-of-work check ships)
- [X] T043 Extend `src/DigiByte.Wallet/Services/FallbackBlockchainService.cs` to take an optional gateway source at the configured position and to report failures to the gateway monitor like the node source does. Tests first in `tests/DigiByte.Wallet.Tests/FallbackGatewayOrderTests.cs`: backup order, first order (once allowed), gateway skipped when not active, core calls never wait on gateway health (spec FR-016, FR-018, FR-019)
- [X] T044 [P] Write `tests/DigiByte.Wallet.Tests/GatewayHeaderStoreTests.cs`, then implement `src/DigiByte.Wallet/Services/GatewayHeaderStore.cs` on the existing IndexedDB storage abstraction, outside the encrypted seed store, keyed by network. Rule verbatim: "a header is stored only after linkage, retarget and (from M2) proof-of-work checks pass"; on a reorganization truncate to the fork point and re-verify
- [X] T045 Allow the gateway origin in the wallet's Content Security Policy `connect-src` the same way the node API origin is allowed (the static `connect-src` list is in `src/DigiByte.Web/wwwroot/index.html`, where the node origin is listed; runtime settings are written by `docker/web/40-write-config.sh` from `WALLET_NODE_*` variables, so add a matching `WALLET_GATEWAY_*` set there and document it beside them). Narrow to the configured origin only. Mark as security-relevant in the PR description (constitution: CSP changes)

**Checkpoint**: Shared checks, a syncing gateway with health and header endpoints, and a wallet that can host a second source behind a monitor.

---

## Phase 3: User Story 1 - Balance and history without trusting an explorer (Priority: P1) MVP

**Goal**: With the gateway active, the wallet shows balance, spendable outputs and history from data whose proofs it has checked against headers it checked itself, labeled "checked" in stage 1.

**Independent Test**: Quickstart scenario 4. Explorers off, known test history, figures match, a tampered proof is rejected and the next source answers.

### Tests for User Story 1

- [X] T046 [P] [US1] Write `tests/DigiByte.SpvGateway.Tests/Index/BlockIndexBuilderTests.cs`: output script hashes and spent outpoints recorded per block, entries removed when a block leaves the best chain
- [X] T047 [P] [US1] Write `tests/DigiByte.SpvGateway.Tests/Endpoints/ScanEndpointTests.cs`: returns exactly the transactions for the watched addresses and nothing for others, includes raw transaction and merkle branch, "at most 100 addresses" enforced with `over_limit`, wrong-network address refused, a range starting before the scan window gives `range_unavailable` and never a partial result presented as complete, `complete: false` when stopped early, and a transaction that spends an outpoint listed in `knownOutpoints` (at most 100 per request) is returned even when that output is older than the scan window
- [X] T048 [P] [US1] Write `tests/DigiByte.SpvGateway.Tests/Endpoints/UtxoEndpointTests.cs`: an output spent in a later block is not returned, unconfirmed outputs are not returned
- [ ] T049 [P] [US1] Write `tests/DigiByte.Wallet.Tests/GatewayProofCheckerTests.cs`: a proof with one changed byte is rejected, the transaction is not counted, the source is reported faulty and the next source is used; a proof for a header the wallet has not verified is rejected (spec SC-001) PROGRESS: checker cases done in `GatewayProofCheckerTests` (11 tests, real proofs). Still to test once the gateway source exists (T075): the source is reported faulty and the next source is used.
- [X] T050 [P] [US1] Write `tests/DigiByte.Wallet.Tests/GatewaySpendRulesTests.cs`: pending with its confirmation count below 6 blocks, spendable at 6 blocks, "the one exception is change from a transaction this wallet signed and sent, which MUST be spendable immediately while still shown as pending", incoming payments from others excluded until 6 blocks, reorganization moves a verified transaction back to pending or removed; pending covers blocks fewer than 6 deep and the wallet's own just-sent transactions, and a payment from someone else is not shown until it is in a block (first-release limit)
- [ ] T051 [P] [US1] Write `tests/DigiByte.Wallet.Tests/P2pGatewayBlockchainServiceTests.cs`: balance, UTXOs, history and height through a fake gateway, empty wallet gives zero balance without errors, a failed read throws instead of returning an empty list (matches the `IBlockchainService` contract in `IBlockchainService.cs`), a mid-request failure falls back with no zero-balance flash, and an established wallet older than the scan window falls back for its first refresh and then refreshes incrementally through the gateway
- [ ] T052 [P] [US1] Write `tests/DigiByte.SpvGateway.Tests/Peers/PeerPrivacyTests.cs`: while running scans and broadcasts against the fake peer, capture every message sent to peers and assert nothing derived from a watched address (no script hash of a watched address, no bloom or filter-load message) is ever sent (spec FR-005, FR-006)
- [ ] T053 [P] [US1] Write `tests/DigiByte.Wallet.Tests/GatewayRequestPrivacyTests.cs`: capture every request the wallet sends to the gateway and assert the bodies contain only addresses, heights and signed transactions, never a key, seed word or extended public key (spec FR-014, constitution Principle I); also assert that while the explorers are serving, no address or outpoint request is sent to the gateway
- [ ] T054 [P] [US1] Write `tests/DigiByte.Wallet.Tests/GatewayTipCrossCheckTests.cs`: a gateway chain whose tip hash differs from the explorer's is marked faulty and not used, an agreeing chain passes, and no explorer answer is inconclusive (neither promote nor demote)
- [ ] T055 [P] [US1] Write `tests/DigiByte.Wallet.Tests/GatewayReorgTests.cs`: a reorganization of up to 6 blocks served by a fake gateway is reflected in balance and history within one minute, a verified transaction moves back to pending or removed, and the header store truncates to the fork point and re-verifies (spec SC-008, FR-003)
- [X] T056 [P] [US1] Write `tests/DigiByte.SpvGateway.Tests/Endpoints/ProofEndpointTests.cs`: a real transaction at a known height is proven for an age beyond the scan window, a wrong height or unknown transaction is reported as unavailable, "at most 20 items" enforced with `over_limit`, nothing about the request appears in logs (FR-012a, FR-021)
- [ ] T057 [P] [US1] Write `tests/DigiByte.Wallet.Tests/WalletSyncStateTests.cs`: the height advances only after every request of a refresh has succeeded, a refresh that fails part way leaves the state unchanged, the wallet rewinds to the fork point when the recorded hash no longer matches, the state is never included in any request to the gateway
- [ ] T058 [P] [US1] Write `tests/DigiByte.Wallet.Tests/GatewayIncrementalRefreshTests.cs`: a wallet older than the scan window refreshes first through the explorer path and then incrementally through the gateway, funds received while offline are found by the next refresh, a spend of an old output is found through `knownOutpoints`, more than 100 addresses or outpoints are split into several requests and combined, and explorer-sourced transactions are upgraded after a proof (FR-009a, SC-002); a transaction the explorer reported that the gateway's proof or chain contradicts (different block or different amounts) becomes disputed: it stays unchecked, is excluded from the spendable balance, and leaves a local note, and the gateway's data is used (FR-012a)
- [ ] T059 [P] [US1] Write `tests/DigiByte.Wallet.Tests/GatewayStateBootstrapTests.cs`: a wallet with no state and an explorer-served refresh gets a sync state with every transaction `explorer-sourced`, the last-refreshed height equal to the explorer's tip height read before the snapshot minus 6 and the hash from the header store; if the header store cannot supply the hash nothing is recorded and the next refresh tries again; the explorer-served result is identical with and without bootstrap; the next refresh then goes through the gateway (FR-009a); a later explorer-served refresh updates the existing state (new transactions added as `explorer-sourced`, `gateway-proven` entries kept when they agree and marked disputed when they do not, the height only moves forward); no address or outpoint is sent to the gateway while the explorers are serving; a state older than the scan window is not used for gateway refreshes
- [ ] T060 [P] [US1] Write `tests/DigiByte.Wallet.Tests/WalletSyncStateLifecycleTests.cs`: the sync state is removed when the wallet is deleted (single and delete-all paths) and by the clear-all-caches action, is absent from every export and backup, and is rebuilt by the bootstrap rule after a deletion (FR-009a)

### Implementation for User Story 1 (gateway)

- [X] T061 [US1] Implement `src/DigiByte.SpvGateway/Sync/BlockFetcher.cs`: request blocks from peers advertising the full-history service bit, check each block's merkle root against the stored header before use, retry on another peer on failure RESULT: done (`BlockFetcher`, `IPeerSession.GetBlockAsync`). Checks the stored header hash and merkle root; a mismatching peer is banned. Note the merkle root covers transaction ids only, so witness data of a returned transaction is not covered by the proof.
- [X] T062 [US1] Implement `src/DigiByte.SpvGateway/Index/BlockIndexStore.cs` (SQLite, BlockIndexRecord fields from data-model.md) and `src/DigiByte.SpvGateway/Index/BlockIndexBuilder.cs`; drop entries older than the scan window, roll back entries when a block leaves the best chain (depends on T032, T061) RESULT: done (`BlockIndexStore`, `BlockIndexBuilder`, short keys: first 8 bytes of SHA-256 of script or outpoint). Real mainnet blocks in `tests/DigiByte.SpvGateway.Tests/Fixtures/blocks-mainnet.json`. Not yet wired into Program.cs: nothing indexes blocks while syncing.
- [X] T063 [P] [US1] Implement `src/DigiByte.SpvGateway/Matching/IBlockMatcher.cs` and `FullBlockMatcher.cs`: answer "which transactions involve these script hashes in this height range" from the index; this is the seam for peer or own-node filters later (M3). Also match inputs that spend any outpoint passed in `knownOutpoints`, so spends of outputs older than the scan window are found
- [X] T064 [US1] Implement `src/DigiByte.SpvGateway/Endpoints/ScanEndpoints.cs` for `POST /v1/scan` and `POST /v1/utxos` per the contract (depends on T062, T063). Addresses only in the body, never in URLs; keep nothing after answering except the seconds-long in-memory result cache. Accept the optional `knownOutpoints` list (at most 100 per request) and match spends of them; treat outpoints under the same no-storage and no-logging rules as addresses RESULT: done (`ScanService`, `ScanEndpoints`, `BlockIndexer`; `MerkleProof.BuildBranch`/`Root` added to Crypto). With real peers the gateway reported ok, 8 full-history peers, header tip level, indexing from the earliest stored header. Known limits: the built-in checkpoint holds only 84 headers, so `indexedFromHeight` is about 83 blocks before the checkpoint until a checkpoint roughly 90 days back is built in (T103); blocks are fetched one at a time, so the first fill of a full 90-day window needs a speed check (T098 area).
- [X] T065 [P] [US1] Implement `src/DigiByte.SpvGateway/Endpoints/TxEndpoints.cs` for `GET /v1/tx/{txid}` with proof when in a block, `height: null` when only seen in a peer's memory pool, `404` otherwise RESULT: done for transactions in indexed blocks (index gained a short transaction-id key). Not done: `height: null` for a transaction only seen in a peer memory pool; it answers 404 `not_found` instead.
- [X] T066 [P] [US1] Implement `src/DigiByte.SpvGateway/Endpoints/ProofEndpoints.cs` for `POST /v1/proofs` per the contract: at most 20 items, fetch the single block at the given height from a full-history peer (any age they still serve), check its merkle root against the stored header, return the proof or an `unavailable` entry with a reason; stores and logs nothing about the request (FR-012a) RESULT: done. Finding fixed on the way: the framework request log wrote full URLs, which carried transaction ids; the request-log categories are now warnings only, with a test.

### Implementation for User Story 1 (wallet)

- [ ] T067 [US1] Implement wallet header sync in `src/DigiByte.Wallet/Services/GatewayHeaderSync.cs`: download from the checkpoint through `GET /v1/headers`, verify with `HeaderChainVerifier`, store in `GatewayHeaderStore`, truncate and re-verify on a reorganization. After syncing, compare the tip block hash and the hashes at tip-1, tip-10 and tip-100 against an independent explorer through `IExplorerBlockHashSource` (new, `src/DigiByte.Wallet/Services/ExplorerBlockHashSource.cs`, built on the existing explorer clients); a mismatch marks the source faulty, and no explorer answer is inconclusive (neither promote nor demote, like the height check) (depends on T021, T037, T044). It runs in the background whenever the gateway monitor reports the gateway ready, whatever its position in the chain, because headers are public data and the bootstrap and proof checks need them
- [X] T068 [US1] Implement `src/DigiByte.Wallet/Services/GatewayProofChecker.cs`: check each returned transaction's merkle proof against a verified header, reject mismatches (depends on T018, T067) RESULT: done (`GatewayProofChecker`; header comes from the wallet store, never from the reply; a header the wallet has not verified yet is not held against the source). Reporting the source faulty and falling back is wired in the gateway source (T075), not here.
- [X] T069 [P] [US1] Implement `src/DigiByte.Wallet/Services/GatewaySpendRules.cs`: the pending and spendable rules in T050 and a per-transaction state (pending with confirmation count, checked, verified, rejected) from data-model.md. "Verified" is only reachable when `GatewayCapabilities.WalletPowVerified` is true. Disputed transactions never count toward the spendable balance until resolved
- [ ] T070 [P] [US1] Implement `src/DigiByte.Wallet/Models/WalletSyncState.cs` and `src/DigiByte.Wallet/Services/WalletSyncStateStore.cs` on the existing IndexedDB abstraction, with the data-model rules verbatim: "advance only after every request of the refresh has succeeded", a failed refresh leaves the height and hash unchanged, origin is `gateway-proven` or `explorer-sourced`, the record never leaves the device. Raw bytes are required for `gateway-proven` entries and optional for `explorer-sourced` ones until a proof returns them. Store it in the wallet's existing per-wallet persistent cache, keyed by wallet id, never in an export or backup
- [ ] T071 [US1] Delete the sync state wherever a wallet's data is removed: in `src/DigiByte.Wallet/Services/WalletService.cs` `DeleteWalletAsync(walletId)` and the delete-all overload (which ends in `ClearAllAsync`), and in the clear-all-caches action `ClearAllCaches` in `src/DigiByte.Web/Pages/Settings.razor`; confirm the Delete Forever flow in `src/DigiByte.Web/Pages/Unlock.razor` goes through the same code. Make sure no export or backup path includes it (depends on T070 and T060)
- [ ] T072 [US1] Implement `src/DigiByte.Wallet/Services/GatewayIncrementalSync.cs`: ask the gateway from the last-synced height minus 6, send the wallet's known unspent outpoints as `knownOutpoints`, split address and outpoint lists into requests of at most 100 each, derive balance and outputs from the wallet's own stored transactions, advance the recorded height only after every request succeeded, rewind to the fork point when the recorded block hash no longer matches the verified chain (depends on T070, T068, T064)
- [ ] T073 [US1] Implement `src/DigiByte.Wallet/Services/GatewayStateBootstrap.cs`: after an explorer-served refresh for a wallet with no sync state (a wallet that existed before this feature, or the first refresh of one older than the scan window), snapshot the explorer's transactions and unspent outputs into `WalletSyncState` as `explorer-sourced` (raw bytes optional, unspent outpoints taken from the explorer's unspent outputs); set the last-refreshed height to the explorer's reported tip height, read before the snapshot, minus 6, and the block hash from `GatewayHeaderStore`; if the header store cannot supply that hash yet, record nothing and try again on the next refresh; never delay or change the explorer-served result (depends on T070). It runs after every explorer-served refresh, not only the first: it merges the new snapshot into the existing state (adds new transactions as `explorer-sourced`, keeps `gateway-proven` entries that agree with the explorer, marks any that disagree as disputed) and only ever moves the last-refreshed height forward, so the state stays fresh while the gateway is a backup. It never contacts the gateway with addresses or outpoints
- [ ] T074 [US1] Implement `src/DigiByte.Wallet/Services/GatewayProofUpgrader.cs`: for explorer-sourced transactions, ask `POST /v1/proofs` (at most 20 per request), check each proof against the header store, upgrade proven ones to `gateway-proven`, and leave the rest unchecked; best effort, rate-limited, never blocks a read (depends on T070, T066, T068). When a proof or the gateway's chain contradicts the explorer's data for a transaction, mark it disputed, keep it unchecked and out of the spendable balance, replace its data with the gateway's proven data, and write a local note; do not show the user a warning in the first release. It runs only when the gateway is the answering source, or when `SpvGateway:BackgroundProving` is enabled (default off), because proof requests reveal transaction ids
- [ ] T075 [US1] Implement `src/DigiByte.Wallet/Services/P2pGatewayBlockchainService.cs` implementing `IBlockchainService`: balance, UTXOs, `GetVerifiedUtxosAsync`, history, `HasAddressHistoryAsync`, transaction lookup, block height. Send addresses only. Throw on any failure or unreadable answer, never return an empty list for one (depends on T043, T064, T068, T069) (also depends on T072). Reads use `GatewayIncrementalSync`. `GetUtxosAsync` for a wallet with no sync state whose history lies inside the scan window may use `/v1/utxos`; for any other wallet with no sync state it throws so the next source answers and the explorer path performs the first refresh. After an explorer-served refresh for a wallet with no sync state, call `GatewayStateBootstrap` (also depends on T073)
- [ ] T076 [P] [US1] Add the verification-state model in `src/DigiByte.Wallet/Models/TransactionVerificationState.cs` and use it where history rows are built in `src/DigiByte.Wallet/Services/WalletService.cs`, without changing today's behavior for other sources. Add the state `unchecked (explorer-sourced)` for transactions that came from the explorer path and have no proof yet
- [ ] T077 [US1] Show pending count and "checked" state in `src/DigiByte.Web/Pages/Transactions.razor` and `src/DigiByte.Web/Pages/Home.razor`; pending transactions are shown but excluded from the spendable amount. The status detail explains what "checked" means and must not describe it as protection against a dishonest gateway (spec FR-013). Show explorer-sourced transactions as unchecked until proven
- [ ] T078 [US1] Register the gateway source, options, monitor, header store and sync in `src/DigiByte.Web/Program.cs` (next to the `NodeSourceMonitor` registrations around lines 96 to 160)

**Checkpoint**: User Story 1 works on its own: reads through the gateway with proof checks, as a backup source labeled "checked".

---

## Phase 4: User Story 2 - Sending keeps working when explorers are down (Priority: P1)

**Goal**: A signed transaction reaches at least 4 independent peers through the gateway, and sends still work through other sources when the gateway is down.

**Independent Test**: Quickstart scenario 5.

### Tests for User Story 2

- [X] T079 [P] [US2] Write `tests/DigiByte.SpvGateway.Tests/Broadcast/BroadcastRelayTests.cs`: relayed to at least 4 fake peers (and reports how many when fewer are connected), one peer's `reject` is reported with its reason, no answer gives `no_response`, a resubmitted identical transaction returns the same txid and is not a new payment
- [X] T080 [P] [US2] Write `tests/DigiByte.SpvGateway.Tests/Endpoints/BroadcastEndpointTests.cs`: outcome shapes `accepted`, `rejected`, `no_response` per the contract, malformed transaction refused with `bad_request`, oversized body refused
- [ ] T081 [P] [US2] Write `tests/DigiByte.Wallet.Tests/GatewayBroadcastTests.cs`: `accepted` is sent, `rejected` is a failure with the reason and no funds shown as spent, `no_response` is "sent, not yet confirmed by peers" and keeps watching, gateway down falls back to the next source, duplicate submit shows no false error
- [ ] T082 [P] [US2] Write `tests/DigiByte.Wallet.Tests/OwnSendRecordingTests.cs`: after a successful broadcast through the gateway and through an explorer the transaction is in the sync state as unconfirmed, its inputs are no longer offered and its change is spendable at once; a rejected broadcast removes it; once it is in a block the entry becomes the gateway-proven version; a payment from someone else is not shown until it is in a block (spec FR-009a, FR-013)

### Implementation for User Story 2

- [X] T083 [US2] Implement `src/DigiByte.SpvGateway/Broadcast/BroadcastRelay.cs`: `inv` then `tx` to at least 4 distinct peers (fewer only when fewer are connected, and then report how many), collect `reject` messages and "seen back from" counts (research R8) (spec FR-011) RESULT: done (`BroadcastRelay`, `IPeerSession.RelayTransactionAsync`, legacy reject message parsed by hand because NBitcoin 9 has no reject payload). `accepted` = at least one peer took the transaction and no rejection outweighs it; modern DigiByte nodes may not send reject at all, so `accepted` is not proof of mempool acceptance. Not yet exercised against a real peer.
- [X] T084 [US2] Implement `src/DigiByte.SpvGateway/Endpoints/BroadcastEndpoints.cs` for `POST /v1/broadcast` per the contract; the only write path, signed transactions only (depends on T083) (spec FR-023)
- [ ] T085 [P] [US2] Implement `src/DigiByte.SpvGateway/Endpoints/FeeEndpoints.cs` for `GET /v1/fee`, advisory only, from recent blocks (spec FR-010)
- [ ] T086 [US2] Implement `BroadcastTransactionAsync` and `GetFeeRateAsync` in `src/DigiByte.Wallet/Services/P2pGatewayBlockchainService.cs` mapping the outcomes in T081; keep the existing fee source authoritative in `FallbackBlockchainService.cs` until the gateway proves reliable
- [ ] T087 [US2] Implement `src/DigiByte.Wallet/Services/GatewayOwnSendRecorder.cs` and call it from the wallet's send path in `src/DigiByte.Wallet/Services/WalletService.cs` for a broadcast through any source: record the transaction in the sync state at once as unconfirmed, with its inputs marked spent and its change output added; remove it if the broadcast is rejected; replace it with the gateway-proven version once it is in a block (depends on the sync state store task)
- [ ] T088 [US2] Show the three send outcomes in `src/DigiByte.Web/Pages/Send.razor` using the existing result display, with the rejection reason when known

**Checkpoint**: Reads and sends both work through the gateway, and sends fall back cleanly.

---

## Phase 5: User Story 3 - Restoring a wallet from a seed (Priority: P2)

**Goal**: Restore finds all funds from a birth height using chunked gateway scans with resumable progress, and falls back to the explorer path when the range is too old or the date is unknown.

**Independent Test**: Quickstart scenario 6.

### Tests for User Story 3

- [ ] T089 [P] [US3] Write `tests/DigiByte.Wallet.Tests/BirthHeightServiceTests.cs`: height recorded at creation for new wallets, a date converts to an approximate height, an unknown date gives origin `unknown`. Origin values verbatim from data-model.md: "recorded, estimated-from-date, unknown"
- [ ] T090 [P] [US3] Write `tests/DigiByte.Wallet.Tests/ScanProgressStoreTests.cs`: "NextHeight advances only after a chunk is fully processed", resume after an interruption, progress never leaves the device
- [ ] T091 [P] [US3] Write `tests/DigiByte.Wallet.Tests/GatewayRestoreScanTests.cs`: finds funds on receive and change addresses across the 20-address gap limit, a deeper scan uses the existing deep-scan gap setting (`WalletService.DeepScanGapLimit`), `range_unavailable` switches to the explorer-based restore with a message, never skips funds silently
- [ ] T092 [P] [US3] Write `tests/DigiByte.SpvGateway.Tests/Endpoints/ScanRangeTests.cs`: a range longer than the configured cap (default 50,000 blocks) gets `over_limit`, a range before the scan window gets `range_unavailable`, a chunk response gives `scannedFrom`, `scannedTo` and `complete` so the wallet can continue

### Implementation for User Story 3

- [ ] T093 [US3] Implement `src/DigiByte.Wallet/Services/BirthHeightService.cs` (record at creation, date-to-height estimate using the verified headers' timestamps, origin tracking per data-model.md) and store the value with the wallet record (FR-008a)
- [ ] T094 [US3] Implement `src/DigiByte.Wallet/Services/ScanProgressStore.cs` on IndexedDB with the ScanProgress fields from data-model.md (start height, next height, highest used receive and change index, mode, source)
- [ ] T095 [US3] Implement `src/DigiByte.Wallet/Services/GatewayRestoreScanner.cs`: iterate chunks through `POST /v1/scan`, apply the gap limit, resume from `NextHeight`, switch to the existing restore path on `range_unavailable`, and hook it into the restore flow in `src/DigiByte.Wallet/Services/WalletService.cs` (depends on T075, T093, T094). When a restore ends, write the `WalletSyncState` (last-synced height and hash, origin per transaction) through `WalletSyncStateStore`; transactions found through the explorer path are stored as explorer-sourced
- [ ] T096 [US3] Record the birth height when a wallet is created in `src/DigiByte.Web/Pages/CreateWallet.razor`; on restore in `src/DigiByte.Web/Pages/RecoverWallet.razor` ask for an approximate date of first use, show progress and "may take a while" text, and show the explanation when the explorer fallback is used
- [ ] T097 [US3] Enforce the scan range cap and the scan window in `src/DigiByte.SpvGateway/Endpoints/ScanEndpoints.cs` and keep the block worker pool bounded so a heavy restore cannot starve normal reads (SC-009), with a test in `tests/DigiByte.SpvGateway.Tests/Endpoints/FairnessTests.cs`

**Checkpoint**: Restore works through the gateway for recent wallets and falls back safely for old ones.

---

## Phase 6: User Story 4 - The gateway never breaks the wallet (Priority: P2)

**Goal**: Syncing, behind, short of peers or down: the wallet behaves as it does today and switches back automatically.

**Independent Test**: Quickstart scenario 7.

### Tests for User Story 4

- [ ] T098 [P] [US4] Write `tests/DigiByte.Wallet.Tests/GatewaySourceMonitorTests.cs`: syncing gateway not used, lag beyond the height tolerance not used, promoted only after the stable period, demoted at once when no longer ready, demoted after three failed reads, and a missing explorer height neither promotes nor demotes (same rules as the node monitor) (spec FR-018, SC-006)
- [ ] T099 [P] [US4] Write `tests/DigiByte.Wallet.Tests/GatewayDegradedFlowsTests.cs`: with the gateway stopped, syncing or behind, balance, receive, send and history return the same results as today and raise no new errors (spec FR-019, SC-005)

### Implementation for User Story 4

- [ ] T100 [US4] Implement `src/DigiByte.Web/Services/GatewayMonitorLoop.cs` modeled on `NodeSourceMonitorLoop.cs`, start it in `src/DigiByte.Web/Program.cs` next to the node loop; never block a screen
- [ ] T101 [US4] Extend `src/DigiByte.Web/Services/NodeSourceStatusService.cs` and its indicator so the status detail names the active source including the gateway; a notice appears only in a degraded state (banner rules from feature 005)
- [ ] T102 [US4] Review every read and send path in `src/DigiByte.Wallet/Services/FallbackBlockchainService.cs` and `WalletService.cs` and confirm none awaits gateway health; add a test in `tests/DigiByte.Wallet.Tests/GatewayDegradedFlowsTests.cs` that fails if a read waits on an unresponsive health check
- [ ] T103 [P] [US4] Add the gateway's state to `src/DigiByte.Web/Pages/DeploymentInfo.razor` and, behind the existing developer-view setting, `src/DigiByte.Web/Pages/DevNode.razor`

**Checkpoint**: The gateway is an optional, self-healing source and never a dependency.

---

## Phase 7: User Story 5 - Operators can see and trust the gateway (Priority: P3)

**Goal**: Operators can see peers, mode, heights and errors, no log leaks addresses, and users are told what the service sees.

**Independent Test**: Quickstart scenarios 3 and 8.

### Tests for User Story 5

- [ ] T104 [P] [US5] Write `tests/DigiByte.SpvGateway.Tests/Health/LogRedactionTests.cs`: run scans and broadcasts, capture all log output and the health report, and scan every SQLite file the gateway wrote, asserting no full address, no raw transaction and no secret appears anywhere (FR-021, SC-007)
- [ ] T105 [P] [US5] Write `tests/DigiByte.SpvGateway.Tests/Health/HealthReportCompletenessTests.cs`: the report states status, peer count, full-history peer count, filter peer count, header height, lag, matching mode, indexed-from height and build version, and contains no wallet data

### Implementation for User Story 5

- [ ] T106 [US5] Add log redaction in `src/DigiByte.SpvGateway/Health/LogRedaction.cs` (log only a short hash prefix of an address, never a transaction body) and use it in every endpoint and the broadcast relay. The result cache is in memory only and expires within 30 seconds (FR-021)
- [ ] T107 [US5] Complete the fields and states in `src/DigiByte.SpvGateway/Health/HealthReporter.cs`: matching mode `blocks` until filter matching ships, indexed-from height, full-history peer count, and `FilterPeerCount` (connected peers advertising the compact-filter service bit from distinct address ranges, spec FR-007), lag against peers' reported heights
- [ ] T108 [P] [US5] Write `tests/DigiByte.SpvGateway.Tests/Sync/RestartRecoveryTests.cs`: after a restart, header sync and the block index resume from the stored state without rebuilding, and the health report moves from `starting` to `ok`
- [ ] T109 [US5] Add the gateway service definition to `railway.toml`, a persistent volume for the SQLite files sized from the T009 measurements (index for the 90-day window plus headers), and a short operator note in `docs/RAILWAY_NODE.md` covering volume size, the scan window setting and how to reset the index
- [ ] T110 [P] [US5] Add the one-line note "the service sees the addresses the wallet asks about" to the status detail in `src/DigiByte.Web/Services/NodeSourceStatusService.cs` and wherever feature 005's privacy note is defined (search for its text and put the gateway line beside it) (FR-020)
- [ ] T111 [P] [US5] Update `docs/ARCHITECTURE.md` (source chain and new service), `docs/PROCESS_FLOWS.md` (gateway read, send and restore flows), `docs/SECURITY.md` (gateway threat model: dishonest or eclipsed peers, anonymous access, CSP origin) and the privacy wording in `SELF_CUSTODY.md`

**Checkpoint**: All five stories work. The gateway can be run in production as a backup source labeled "checked".

---

## Phase 8: Stage 2 - Wallet proof-of-work check ("verified" label) (US1)

**Purpose**: The wallet checks proof of work itself, which turns "checked" into "verified" and allows the gateway above the explorers. The hashers it needs (T015 and T022 to T025) are built in Phase 2, because the gateway also needs them from the first release (spec FR-002). High risk: Qubit and Odocrypt have no known managed implementation.

- [ ] T112 [US1] Enable the proof-of-work check in the wallet's header verification (`GatewayHeaderSync`) using the hashers from T022 to T025, check each header's hash against its target, and add the negative cases to `tests/DigiByte.Crypto.Tests/Headers/HeaderChainVerifierTests.cs`
- [ ] T113 [US1] Measure per-header cost of every hasher under Blazor WebAssembly in `tests/DigiByte.Wallet.Tests` or a small benchmark under `tools/`, set a bound on headers verified per session, and record the result in `specs/011-p2p-spv-gateway/research.md` under R3. If any active algorithm cannot be made practical, the wallet label stays "checked", the gateway stays a backup source, and work stops before T114. There is no partial mode that earns "verified" (spec FR-013)
- [ ] T114 [US1] Set `GatewayCapabilities.WalletPowVerified = true` in `src/DigiByte.Wallet/Services/GatewayCapabilities.cs`, enable the "verified" label in `GatewaySpendRules.cs` and the UI, allow `Position=first`, and update the tests from T042 and T050 accordingly. Do this only when T112 and T113 pass

---

## Phase 9: Polish and Cross-Cutting Concerns

- [ ] T115 [P] Add a CHANGELOG entry in `CHANGELOG.md` for the gateway, the new setting names, and the staged "checked" then "verified" labels
- [ ] T116 Run every scenario in `specs/011-p2p-spv-gateway/quickstart.md` against a testnet gateway and fix gaps; record results, including the measured broadcast success rate over at least 20 test sends (SC-003)
- [ ] T117 Tune the timing targets against measured data (SC-002 balance within 3 seconds, SC-004 a 30-day restore within 5 minutes) and confirm the reorganization timing test from the US1 tests (SC-008) still passes under load. Update `plan.md` and `spec.md` if a target changes
- [ ] T118 Security review of the PR: CSP origin change (T045), anonymous endpoints and limits, logs (T104), reorganization handling, eclipse resistance via the explorer height and block-hash cross-check. Flag the CSP and any auth-boundary change in the PR description
- [ ] T119 Run `dotnet build` and `dotnet test` across `tests/DigiByte.Crypto.Tests`, `tests/DigiByte.Wallet.Tests`, `tests/DigiByte.SpvGateway.Tests` and the existing projects to confirm nothing regressed (merge gate)

---

## Dependencies and Execution Order

### Phase Dependencies

- **Setup and M0 (Phase 1)**: starts immediately. T008 needs T010 for heights. The hasher spike (T011) needs T008 and gates T022 to T025. T007 and T010 should finish before Phase 2 code that depends on them.
- **Foundational (Phase 2)**: depends on Phase 1. Blocks every user story. Within it, only the gateway's header sync (T033), the wiring (T038) and what depends on them wait for the hashers (T022 to T025). The Crypto tests, wallet foundation (T039 to T045) and the other gateway pieces can proceed against fixtures and the fake peer while the Qubit and Odocrypt ports are in progress.
- **User Stories (Phases 3 to 7)**: all depend on Phase 2. US1 and US2 can proceed in parallel after it. US3 needs US1's wallet source (T075) and the wallet sync state (T070), which its restore hands off to. US4 needs the Phase 2 monitor work and US1's service. US5 can start once the gateway endpoints exist.
- **Stage 2 (Phase 8)**: depends on Phase 2 (which now includes the hashers) and the T008 fixtures. T112 also needs T067 (US1's wallet header sync), and T114 needs T042 and T050. It can run in parallel with the other user-story phases once those exist. T114 is the only task that changes visible behavior and must come last.
- **Polish (Phase 9)**: after the phases being released.

### User Story Dependencies

- **US1 (P1)**: after Phase 2. No dependency on other stories.
- **US2 (P1)**: after Phase 2 and T075 (the wallet source class exists). Independent of US1's proofs.
- **US3 (P2)**: after US1 (needs the wallet source and header sync).
- **US4 (P2)**: after Phase 2. Integrates with US1 but is testable with a fake gateway.
- **US5 (P3)**: after the gateway endpoints exist (US1, US2).

### Within Each Story

- Tests first, and they must fail before the code
- Crypto and gateway primitives before wallet services, wallet services before UI
- Models and stores before the services that use them

### Parallel Opportunities

- Phase 1: T004 to T007, T009 and T010 are independent (T008 waits for T010)
- Phase 2: T012 to T015 together, T016 and T018 and T020 together, the hashers T022 to T025 together, T026 to T029 together, T039 and T040 and T044 together
- US1: T046 to T060 (all tests) together, then T063, T065, T066, T069, T070 and T076 together
- US2: T079 to T082 together
- US3: T089 to T092 together

---

## Parallel Example: User Story 1

```bash
# Launch all US1 tests together:
Task: "Write tests/DigiByte.SpvGateway.Tests/Index/BlockIndexBuilderTests.cs"
Task: "Write tests/DigiByte.SpvGateway.Tests/Endpoints/ScanEndpointTests.cs"
Task: "Write tests/DigiByte.SpvGateway.Tests/Endpoints/UtxoEndpointTests.cs"
Task: "Write tests/DigiByte.Wallet.Tests/GatewayProofCheckerTests.cs"
Task: "Write tests/DigiByte.Wallet.Tests/GatewaySpendRulesTests.cs"
Task: "Write tests/DigiByte.Wallet.Tests/P2pGatewayBlockchainServiceTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1 and Phase 2
2. Complete Phase 3 (User Story 1)
3. Stop and validate with quickstart scenarios 1 to 4, with the gateway configured as a backup source labeled "checked"
4. Deploy behind the off-by-default setting and watch it

### Incremental Delivery

1. Setup and foundation, then US1: reads through the gateway (stage 1, backup, "checked")
2. US2: sends through the gateway
3. US3: restore, US4: degraded-mode polish and status UI, US5: operator and docs work
4. Phase 8 in parallel: when it passes, flip the capability flag, enable "verified" and allow promotion above the explorers
5. Each step adds value without breaking the previous ones, because the gateway is never required (constitution Principle V)

### Notes

- Do not promote the gateway above the explorers or show "verified" until T114
- Re-run the peer probe (T005, T006) before release; if peers or our own node serve filters, add an `IBlockMatcher` implementation (M3) rather than changing endpoints
- Commit after each task or small group, using Conventional Commits
- Keep any change to `NodeSourceMonitor` and related types behavior-neutral for the existing node source; the existing feature 005 tests are the safety net
