---

description: "Task list for Railway-Hosted DigiByte Core Node"
---

# Tasks: Railway-Hosted DigiByte Core Node

**Input**: Design documents from `/specs/004-railway-digibyte-node/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included for every `DigiByte.NodeApi` code change (plan: constitution merge gate; tests in `tests/DigiByte.NodeApi.Tests/`). Deployment steps are verified with the scripted checks in quickstart.md instead of unit tests.

**Organization**: Grouped by user story. User Story 3 (outside-project gateway) is deferred by clarification and has no tasks here.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1, US2, US4 (US3 deferred)
- Tasks marked **(operator)** are done by hand in the Railway dashboard/CLI, not by code. Never paste secret values into the repo, chat, or logs.

## Path Conventions

Existing monorepo: `docker/`, `src/DigiByte.NodeApi/`, `tests/DigiByte.NodeApi.Tests/`, `docs/`, `railway.toml`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: New Railway-specific node assets, kept separate from the regtest/testnet/pruned-mainnet files so local compose setups keep working.

- [X] T001 [P] Create `docker/digibyted/digibyte-railway.conf`: `server=1`, `disablewallet=1` (FR-014: the node holds no wallets), `txindex=1`, no `prune`, `printtoconsole=1`, `listen=1`, `rpcport=14022`, no `rpcuser`/`rpcpassword` lines (injected at start), conservative `maxconnections`; comment that pruning is incompatible with the chosen full-node setup (spec FR-004)
- [X] T002 [P] Create `docker/digibyted/entrypoint-railway.sh`: require `DGB_RPC_USER` and `DGB_RPC_PASSWORD` (exit non-zero with a clear message if missing, or if the password is `changeme`), write them plus `dbcache=${DGB_DBCACHE_MB:-512}` into the data-dir `digibyte.conf` at start, restrict `rpcallowip` to the range in required variable `DGB_RPC_ALLOW_IP` (exit non-zero if unset; the real private range is only learned during T015, so it is not hard-coded), never echo the password, then `exec digibyted -printtoconsole`
- [X] T003 [P] Create runbook skeleton `docs/RAILWAY_NODE.md` with section headings: First deploy, Credential rotation, Upgrade, Recovery/reindex, Cost review, Teardown (filled in Phases 3-5)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Hardened images and a safe public NodeApi. MUST be complete before anything is deployed to the public internet.

**⚠️ CRITICAL**: The audit in `src/DigiByte.NodeApi/Endpoints/` found routes that are not key-gated today: `/api/keys/dump/{address}`, `/api/keys/import/*`, `/api/wallet/*` (send, sign, create, load, backup, encrypt), `/api/mining/generate/{count}`, `/api/util/stop`, `/api/util/rescan`, `/api/util/utxo/lock|unlock`, `/api/blockchain/block/invalidate|reconsider`, `/api/tx/create|fund`, `/api/faucet/*`, `/api/psbt/*`, `/api/descriptor/*`. The wallet itself appears to use only `/api/tx/broadcast` and the `/api/digidollar` read routes (a first search; T039 confirms this across all callers).

- [X] T004 Update `docker/digibyted/Dockerfile`: default `DGB_VERSION=9.26.7`; download `SHA256SUMS` from the same release and verify the tarball before extracting (fail the build on mismatch); copy `digibyte-railway.conf` and `entrypoint-railway.sh`, `chmod +x`; keep existing regtest/testnet/mainnet files untouched (depends on T001, T002)
- [X] T005 [P] Update `docker/node-api/Dockerfile`: remove the `changeme` RPC password, `IsTestnet=true` and `FaucetEnabled=true` defaults (mainnet, faucet off, no password default)
- [X] T006 [P] Write failing tests in `tests/DigiByte.NodeApi.Tests/ProductionConfigTests.cs`: in Production, startup config validation rejects empty RPC user/password and the literal `changeme`; Development still allows defaults
- [X] T007 Add `src/DigiByte.NodeApi/Services/NodeConfigValidator.cs` and call it from `src/DigiByte.NodeApi/Program.cs` right after config binding so Production refuses to start on empty/`changeme` credentials (makes T006 pass; no secret values in the error message)
- [X] T039 Audit every caller of each deny-listed NodeApi route before gating: search `src/` (Web, Wallet, Api, Pay.Api, Pay.Web), `sdk/`, `samples/`, `tests/`, `digipay.postman_collection.json` and `test-nodeapi.ps1` for the route paths; also include the costly read routes (`/api/util/txoutset`, `/api/network/mempool/raw`, `/api/network/peers`, `/api/descriptor/scan`) and decide for each whether to gate it or leave it open (the public NodeApi has no rate limiting in this release); write the result (route, caller, keep-open or gate) as a table in `specs/004-railway-digibyte-node/research.md` under a new heading "R6a Route audit"; any route a real consumer needs stays open (read-only) or gets its own key requirement, and the T008/T009 deny-list MUST match this table exactly (must be done before T008)
- [X] T008 [P] Write failing tests in `tests/DigiByte.NodeApi.Tests/OperatorRouteGateTests.cs` using the deny-list from T039: in non-Development, each deny-listed route above returns 503 with no key configured, 401 with a wrong `X-NodeApi-Key`, and reaches the handler with the right key; `/api/tx/broadcast`, `/api/blockchain/height`, `/api/address/*` and `/api/digidollar` read routes stay open; Development behaviour is unchanged
- [X] T009 Add `src/DigiByte.NodeApi/Middleware/OperatorRouteGate.cs` (deny-list of path prefixes/patterns, reuses `OperatorKey.Check` from `src/DigiByte.NodeApi/Middleware/OperatorKey.cs`, fails closed when `DigiDollar:WriteKey` is unset) and register it in `src/DigiByte.NodeApi/Program.cs` before endpoint mapping (makes T008 pass)
- [X] T010 Verify against a local regtest node started with `disablewallet=1` (`docker/digibyted/entrypoint.sh` plus the new conf) that every DigiDollar read route and `/api/tx/broadcast` still work without a wallet; if any needs a wallet, record which one and decide in `specs/004-railway-digibyte-node/research.md` (R7) before T015; keep `DIGIDOLLAR_ENABLED` read-only and the hot wallet unset in `specs/004-railway-digibyte-node/contracts/railway-services.md` (FR-014)
- [X] T011 Run `dotnet build DigiByte.slnx` and `dotnet test tests/DigiByte.NodeApi.Tests/DigiByte.NodeApi.Tests.csproj` from the repo root and confirm green

**Checkpoint**: Images build; NodeApi fails closed and exposes no dangerous routes without the operator key.

---

## Phase 3: User Story 1 - Run a current, synced DigiByte node in its own Railway project (Priority: P1) 🎯 MVP

**Goal**: A v9.26.7 full node syncing and staying synced on a persistent volume, restart-safe, with a health signal that does not flag syncing as failure.

**Independent Test**: quickstart steps 1, 2 and 6: node reports v9.26.7, height within 3 blocks of an explorer, redeploy resumes from the saved height.

### Tests for User Story 1

- [X] T012 [P] [US1] Write failing tests in `tests/DigiByte.NodeApi.Tests/NodeHealthTests.cs` per [contracts/nodeapi-health.md](contracts/nodeapi-health.md): status `starting` / `ok` / `syncing` / `unhealthy`; an RPC error meaning the node is still loading its block index (code -28) yields `starting` with HTTP 200, not `unhealthy`; `lag = headers - blocks`; HTTP 200 for `starting`, `ok` and `syncing`, 503 only when RPC is unreachable or rejects credentials; response never contains RPC credentials, URLs with credentials, or file paths

### Implementation for User Story 1

- [X] T013 [US1] Add `src/DigiByte.NodeApi/Services/NodeHealthService.cs` building the health model from `getblockchaininfo`, `getnetworkinfo` and a txindex check (cheap calls only, no walk over historical blocks) (makes T012 pass)
- [X] T014 [US1] Replace the inline `/api/health` handler in `src/DigiByte.NodeApi/Program.cs` with the new service; keep the legacy `network`/`timestamp` fields and add `status`, `nodeVersion`, `blocks`, `headers`, `lag`, `peers`, `txindex`; first grep consumers of the old `status` value (`connected`/`disconnected`) in `src/` and `tests/` and keep a compatible `connected` boolean if any exist; remove the `nodeUrl` field so no internal address is leaked; the old handler returned 200 even when disconnected, so also check docker-compose healthchecks, `railway.toml` and scripts that call `/api/health` and update any that relied on that
- [ ] T015 [US1] (operator) Create a **new** Railway project; add service `digibyted` from `docker/digibyted/Dockerfile` with start command `/etc/digibyte/entrypoint-railway.sh`, build arg `DGB_VERSION=9.26.7`, a 100 GB volume at `/root/.digibyte`, secret variables `DGB_RPC_USER`, `DGB_RPC_PASSWORD` (long random), and `DGB_RPC_ALLOW_IP` (the project's private address range, verified now per research R3; not a secret), `DGB_DBCACHE_MB` high for initial sync; **no public domain, no RPC TCP proxy** ([contracts/railway-services.md](contracts/railway-services.md))
- [ ] T016 [US1] (operator) Watch the initial sync; record start time, finish time, peak RAM, disk used; lower `DGB_DBCACHE_MB` afterwards; compare to SC-001 (24 h target) and write the measurements into `docs/RAILWAY_NODE.md` (First deploy)
- [ ] T017 [US1] (operator) Run quickstart steps 2 and 6: version equals v9.26.7, height within 3 of an explorer, `txindex` true, redeploy `digibyted` and confirm it resumes from the previous height within 10 minutes; record results
- [X] T018 [US1] Fill the "First deploy" and "Recovery/reindex" sections of `docs/RAILWAY_NODE.md` with the exact steps and variable names used; add a note that inbound P2P is optional (outbound-only is enough) and how to enable it if wanted (FR-007)

**Checkpoint**: User Story 1 is complete when T017 passes; the node is usable on its own.

---

## Phase 4: User Story 2 - Wallet and other apps reach the node privately and safely (Priority: P1)

**Goal**: NodeApi runs in the new project, talks to the node over the private network, is the only public HTTP surface, and the wallet uses it with no code change.

**Independent Test**: quickstart steps 3, 4, 5, 7: RPC unreachable from the internet, reads and broadcast work through NodeApi, dangerous routes closed, wallet degrades gracefully when the node is down.

- [X] T040 [US2] Find where NodeApi writes local files: read `DigiDollarConfig` in `src/DigiByte.NodeApi/DigiDollar/` for the default `IndexDbPath` and `MetricsDbPath` and any other file paths; record them and the chosen mount path (e.g. `/data`) in `specs/004-railway-digibyte-node/contracts/railway-services.md`; the DigiDollar output index must survive redeploys so it is not rebuilt from the node each time (must be done before T019)
- [ ] T019 [US2] (operator) Add service `node-api` to the new project from `docker/node-api/Dockerfile` (repo-root build context), public domain, healthcheck `/api/health` with a timeout long enough for cold start; variables per [contracts/railway-services.md](contracts/railway-services.md) (names as the code reads them): `ASPNETCORE_ENVIRONMENT=Production`, `DIGIBYTE_HOST` = `digibyted`'s private DNS name (verify exact form), `DIGIBYTE_RPC_USER` / `DIGIBYTE_RPC_PASSWORD` by reference to the `digibyted` variables, `DIGIBYTE_TESTNET=false`, `DIGIDOLLAR_ENABLED=true` (read routes only), `NODEAPI_WRITE_KEY` (secret), faucet left off, `NODEAPI_WALLET_ORIGINS` set to the wallet origins only; a small Railway volume (start 1 GB) mounted at the path from T040, with `DIGIDOLLAR_INDEX_DB` and `NODEAPI_METRICS_DB` pointing into it; after a redeploy confirm the DigiDollar index is kept (not re-synced from height 0)
- [ ] T020 [P] [US2] (operator) Run quickstart step 3: confirm nothing answers on port 14022 from the public internet and that `digibyted` has no public domain; record only pass/fail and the date in `docs/RAILWAY_NODE.md` (no raw output, hostnames or addresses)
- [ ] T021 [P] [US2] (operator) Run quickstart step 4: height, address balance and a broadcast of a known-valid signed test transaction through `/api/tx/broadcast`; DigiDollar read routes (`/api/digidollar/health`, address history/UTXOs, tx status)
- [ ] T022 [P] [US2] (operator) Run quickstart step 5: call each deny-listed route from T008 without `X-NodeApi-Key` on the live service and confirm 401/403/404/503, never 200; wrong RPC credentials must also fail
- [X] T023 [US2] Update `railway.toml` comments: describe the new project's two services, remove the pruned-mainnet instructions that contradict this feature, and state that the RPC port must never get a public domain
- [ ] T024 [US2] Repoint a **staging** wallet to the new NodeApi by changing only `NodeApiUrl` (config in `src/DigiByte.Web/wwwroot/appsettings.json` for the deployed web service) and verify balance, UTXOs, send/broadcast and DigiDollar views; confirm CORS behaves as built: public read routes answer any origin (public chain data), wallet-scoped DigiDollar routes answer no browser origin, and the usage-counter route answers only `NODEAPI_WALLET_ORIGINS` (quickstart step 7, part 1); also check `src/DigiByte.Api` and `src/DigiByte.Pay.Api` for calls to NodeApi (use the T039 audit) and repoint any found by configuration only (FR-008)
- [ ] T025 [US2] (operator) Stop `digibyted` and confirm the wallet falls back to its explorer mode with a degraded-mode notice and DGB balance/receive/send still work (FR-015, constitution V); while the node is down, confirm `node-api` and any in-project consumer keep running and retry instead of crashing (spec edge case: consumers start before the node is ready); restart the node and confirm recovery without a wallet redeploy
- [ ] T026 [US2] Production cut-over: switch the production wallet's `NodeApiUrl` to the new service only after T020-T025 pass; keep the old endpoint untouched as rollback and note the rollback step in `docs/RAILWAY_NODE.md`

**Checkpoint**: User Stories 1 and 2 together deliver the MVP: wallet running against the new node.

---

## Phase 5: User Story 4 - Know it is healthy, current and affordable (Priority: P2)

**Goal**: Alerts for down/behind/peerless/low-disk/outdated, cost guardrails, and a low-risk upgrade procedure.

**Independent Test**: quickstart step 8: stopping the node triggers an alert within 10 minutes; the upgrade runbook moves a copy to a newer version without a resync.

### Tests for User Story 4

- [X] T027 [P] [US4] Add failing tests to `tests/DigiByte.NodeApi.Tests/NodeHealthTests.cs` for `recommendedVersion` and `versionOk` (semantic compare of the running version against `DGB_RECOMMENDED_VERSION`; missing setting yields `versionOk` true and no `recommendedVersion`)

### Implementation for User Story 4

- [ ] T028 [US4] (code done; operator step pending) Add `recommendedVersion` / `versionOk` to `src/DigiByte.NodeApi/Services/NodeHealthService.cs` (makes T027 pass); set `DGB_RECOMMENDED_VERSION=9.26.7` on the `node-api` service (operator)
- [ ] T029 [P] [US4] (operator) Set up an external uptime monitor on `GET /api/health` that checks every 1 minute (or less, so SC-006's 10 minutes holds) and alerts when: non-200 for 5 minutes, `lag > 10`, `peers == 0`, `versionOk == false`, or `txindex == false` after sync; pick the alert channel (email or chat); document the rules in `docs/RAILWAY_NODE.md`; the monitor treats `status: starting` as not-yet-healthy but only alerts if it lasts over 30 minutes
- [ ] T030 [P] [US4] (operator) Configure Railway: deploy-failure/crash notifications, a cost alert at $40 and a hard usage limit at $60 (clarified ceiling; verify the exact options in the current Railway plan), and a disk-usage alert at 80% of the volume
- [ ] T031 [US4] (operator) Run quickstart step 8: stop the node for 10 minutes and confirm the alert fires within 10 minutes (SC-006)
- [ ] T041 [US4] (operator) Rehearse an upgrade on a throwaway copy (a second `digibyted` service or a branch deploy with a copy of the volume): change `DGB_VERSION`, redeploy, record operator effort and node downtime, and confirm it resumes without a resync (SC-007: under 30 minutes effort, under 10 minutes downtime); feed the timings into the "Upgrade" section written in T032
- [X] T032 [US4] Write the "Upgrade", "Credential rotation" and "Teardown" sections of `docs/RAILWAY_NODE.md`: change `DGB_VERSION` and redeploy (checksum verified in the image build), back up first if any wallet exists, rotate `DGB_RPC_PASSWORD` and `NODEAPI_WRITE_KEY` on both services without a resync, update both `DGB_VERSION` (build) and `DGB_RECOMMENDED_VERSION` (health check value) on every upgrade, enlarge the volume, and the Thaw Day (block 24,490,000) minimum-version warning
- [ ] T033 [US4] (operator) After one week of running, record actual RAM/CPU/volume/egress usage and the projected monthly cost against the $40 target / $60 ceiling in the "Cost review" section of `docs/RAILWAY_NODE.md`; if above $60, stop and revisit the design (spec clarification needed); also record, from the monitor history, the share of time the node was within 3 blocks of an explorer (SC-003 evidence; the full 99%-over-a-month check continues after sign-off)

**Checkpoint**: All in-scope stories done.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T034 [P] Update `docs/ARCHITECTURE.md` with the new Railway project layout and the public/private boundary
- [X] T035 [P] Add a CHANGELOG.md entry for the Railway node deployment, hardened NodeApi defaults and the new health fields
- [X] T036 Run `dotnet test` across all test projects affected (`tests/DigiByte.NodeApi.Tests`, plus any project that consumes `/api/health`) and fix regressions
- [X] T037 Security review of the NodeApi diff (route gate, config validator, health response) against the constitution's secret-hygiene and auth-boundary rules; flag it in the PR description as security-relevant
- [ ] T038 Run the full quickstart.md end to end once more on the production setup and tick off SC-001 to SC-009 in `docs/RAILWAY_NODE.md`, including timing how long it takes to point a new in-project test app at the node by configuration only (SC-009, under 15 minutes)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none
- **Foundational (Phase 2)**: needs Phase 1 for T004; blocks all deployment tasks
- **US1 (Phase 3)**: needs Phase 2; code tasks T012-T014 can run while the node syncs
- **US2 (Phase 4)**: needs US1 (a running node) and Phase 2 (hardened NodeApi)
- **US4 (Phase 5)**: needs US1/US2 deployed; T027-T028 only need Phase 3's health service
- **Polish (Phase 6)**: needs everything above

### Task-Level Notes

- T041 should run before T032 is finalised. T004 depends on T001, T002. T007 depends on T006. T008 depends on T039. T009 depends on T008. T019 also depends on T040 (T039 and T040 were added after analysis; IDs are not in execution order). T013 depends on T012. T014 depends on T013. T028 depends on T027 and T013. T015 needs T004 and T010. T019 needs T005, T007, T009, T014. T026 needs T020-T025.
- T007/T009/T014 all edit `src/DigiByte.NodeApi/Program.cs`; do them in order, not in parallel.

### Parallel Opportunities

- Phase 1: T001, T002, T003
- Phase 2: T005, T006, T008 (different files)
- Phase 3: T012 in parallel with T015-T016 (code and operator work)
- Phase 4: T020, T021, T022
- Phase 5: T027, T029, T030
- Phase 6: T034, T035

## Implementation Strategy

### MVP First (User Stories 1 + 2)

1. Phases 1 and 2 (assets and hardened NodeApi)
2. Phase 3: deploy the node, let it sync (the long wait; do Phase 2/4 code meanwhile)
3. Phase 4: add NodeApi, validate privately, then cut the wallet over
4. **Stop and validate** with quickstart steps 1-7

### Incremental Delivery

1. Foundation, then US1 (node alone)
2. US2 (wallet uses it) is the MVP
3. US4 (alerts, cost, upgrade runbook), then Polish
4. User Story 3 (gateway) is a later feature

## Notes

- Work on a branch named `feature/railway-digibyte-node` (constitution branching rule); the spec directory name `004-railway-digibyte-node` is independent of it
- [P] tasks = different files, no dependencies
- Commit after each task or logical group using Conventional Commits
- Secrets live only in Railway variables; never commit or log them
- Railway specifics marked "verify" in research.md must be confirmed while doing the matching operator task
