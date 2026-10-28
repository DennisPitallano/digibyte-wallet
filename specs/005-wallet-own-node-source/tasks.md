---

description: "Task list for Wallet Uses Our Own Node When Ready"
---

# Tasks: Wallet Uses Our Own Node When Ready

**Input**: Design documents from `/specs/005-wallet-own-node-source/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included for all `DigiByte.Wallet` logic, written first and failing before the code (constitution principle III, including negative paths). The Web layer only renders state computed in `DigiByte.Wallet`, so it is verified with the quickstart. Existing `FallbackBroadcastTests` must keep passing.

**Organization**: Grouped by user story. Tasks marked **(operator)** are done by hand (Railway, DNS) or are manual checks; never commit secrets.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 to US5

## Path Conventions

Monorepo: `src/DigiByte.Wallet/Services/`, `src/DigiByte.Web/`, `tests/DigiByte.Wallet.Tests/`, `docker/web/`, `docs/`.

---

## Phase 1: Setup (Shared Declarations)

**Purpose**: The small types everything else depends on.

- [X] T001 Create `src/DigiByte.Wallet/Services/NodeSourceTypes.cs` with: enum `NodeSourceState` (`Disabled`, `Unavailable`, `NotReady`, `Stabilizing`, `Active`); enum `NodeNotReadyReason` (`None`, `Disabled`, `Unreachable`, `Starting`, `Syncing`, `Unhealthy`, `Txindex`, `Version`, `HeightDrift`, `Malformed`, `ReadFailures`); interface `INodeReadFailureSink` (`ReportNodeReadFailure()`, `ReportNodeReadSuccess()`, called by the fallback service); interface `INodeSourceState` (read-only: `State`, `Reason`, `IsActive`, `NodeWasActive`, `LastSnapshot`, `event Action? Changed`); interface `INodeHealthSource` (`Task<NodeHealthResult> GetAsync(CancellationToken)`); interface `IExplorerHeightSource` (`Task<int?> GetHeightAsync(CancellationToken)`, null when no explorer answered); an `IClock` with `UtcNow` for tests

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The pure, fully tested decision logic. No user story can start until this is done.

**⚠️ CRITICAL**: Tests first; they must fail before the code exists.

- [X] T002 [P] Write failing tests in `tests/DigiByte.Wallet.Tests/NodeSourceOptionsTests.cs`: defaults `enabled` false, `checkInterval` 30 s, `stablePeriod` 120 s, `heightTolerance` 5, `healthTimeout` 3 s, `devView` false; out-of-range values fall back to the default for that field: `checkInterval` 10 to 300, `stablePeriod` at least two check intervals, `heightTolerance` 1 to 50, `healthTimeout` 1 to 10; an empty or non-absolute-http(s) `apiUrl` means unavailable (not a crash)
- [X] T003 Implement `src/DigiByte.Wallet/Services/NodeSourceOptions.cs` (parse from key/value configuration, clamp, defaults as in T002) (makes T002 pass)
- [X] T004 [P] Write failing tests in `tests/DigiByte.Wallet.Tests/NodeReadinessTests.cs` for the interpretation table in [contracts/node-readiness.md](contracts/node-readiness.md): timeout/network error/blocked/non-JSON -> unreachable or malformed; HTTP 503 -> unavailable; `starting`, `syncing` -> not ready with that reason; `ok` with `txindex` false -> `Txindex`; `ok` with `versionOk` false -> `Version`; absent `versionOk` -> treated as true; `ok` and all good with `|heightDelta| <= tolerance` -> ready; delta above tolerance in either direction -> `HeightDrift`; no explorer height -> inconclusive (neither ready nor wrong); unknown fields ignored
- [X] T005 Implement `src/DigiByte.Wallet/Services/NodeReadiness.cs` (health JSON parsing and the ready predicate: "reachable, status == ok, txindex, versionOk, |heightDelta| <= heightTolerance") (makes T004 pass)
- [X] T006 [P] Write failing tests in `tests/DigiByte.Wallet.Tests/NodeHealthHttpSourceTests.cs` using a fake `HttpMessageHandler`: valid 200 body parsed; 503, 500, timeout (honours the 3 s limit), `HttpRequestException`, invalid JSON and an empty body each give an unavailable or malformed result and never throw; no retry (exactly one request per call); request goes to `{apiUrl}/api/health`
- [X] T007 Implement `src/DigiByte.Wallet/Services/NodeHealthHttpSource.cs` (single `GET /api/health`, timeout from options, no retries, exceptions turned into results) (makes T006 pass)
- [X] T008 [P] Write failing tests in `tests/DigiByte.Wallet.Tests/NodeSourceMonitorTests.cs` with fake health source, fake explorer-height source and fake clock, covering the transition table in [data-model.md](data-model.md): ready check -> `Stabilizing`; ready for the full stable period (120 s default) -> `Active`; any not-ready or failed check from `Stabilizing` or `Active` -> not ready immediately (the next check, within the 30 s interval); flapping every other check never reaches `Active`; no explorer height: `Active` stays `Active`, `Stabilizing` and below do not advance; `NodeWasActive` becomes true once `Active` is reached and stays true; `Changed` fires on each state change only; while `Active`, 3 consecutive reported node read failures demote immediately to `NotReady(ReadFailures)` without waiting for the next check, and a reported success resets the count; the health call and the explorer lookup run in parallel and a whole check never takes longer than the health timeout plus 5 s; the check method never throws, whatever the sources do
- [X] T009 Implement `src/DigiByte.Wallet/Services/NodeSourceMonitor.cs` (state machine and `CheckAsync()`, implements `INodeSourceState`) (makes T008 pass)
- [X] T010 [P] Write failing tests in `tests/DigiByte.Wallet.Tests/ExplorerHeightSourceTests.cs`: tries explorers in order, returns the first answer, skips one that throws, returns null when all fail, abandons any single lookup (and the whole call) after 5 seconds and returns null, never throws
- [X] T011 Implement `src/DigiByte.Wallet/Services/ExplorerHeightSource.cs` using each explorer's `GetBlockHeightAsync` directly (not the cached fallback wrapper), with a 5 second cancellation so the explorer client's own retry policy (up to 45 s in total) cannot stretch a check (makes T010 pass)
- [X] T057 [P] Write failing tests in `tests/DigiByte.Wallet.Tests/NodeApiStrictModeTests.cs` using a fake `HttpMessageHandler`: with strict mode on, each of `GetBalanceAsync`, `GetUtxosAsync` (single and batch), `GetTransactionAsync`, `GetFeeRateAsync`, `GetBlockHeightAsync` and `BroadcastTransactionAsync` throws on a transport error, a timeout, an HTTP 500 and an unreadable body, and never returns 0, an empty list, null, the default fee or height 0 for those; `GetTransactionAsync` returns null only for an HTTP 404 or an explicit not-found body; with strict mode off (the default) every method keeps exactly today's lenient results (0, `[]`, null, default fee, height 0), so feature-off behaviour is unchanged
- [X] T058 Implement the strict-mode option in `src/DigiByte.Wallet/Services/NodeApiBlockchainService.cs` (constructor parameter `strictErrors`, default false; lenient code path untouched) (makes T057 pass); `GetAddressTransactionsAsync` stays as is and is never used for history when the feature is on (T019)
- [X] T012 Run `dotnet test tests/DigiByte.Wallet.Tests/DigiByte.Wallet.Tests.csproj` from the repo root and confirm green

**Checkpoint**: The decision logic is complete and tested; nothing in the app uses it yet.

---

## Phase 3: User Story 1 - Nothing changes for users while the node is still syncing (Priority: P1) 🎯 MVP

**Goal**: With node use on and the node not ready, the wallet works from the explorers exactly as today, never touches the node for data, and a dead node never delays a screen.

**Independent Test**: [quickstart.md](quickstart.md) steps 1 and 2: feature off is unchanged; feature on against the syncing real node shows explorer-only data calls and one health call every 30 s.

### Tests for User Story 1

- [X] T013 [P] [US1] Write failing tests in `tests/DigiByte.Wallet.Tests/FallbackNodePreferenceTests.cs` (not-ready half): with the monitor reporting any state other than `Active` (feature on), reads never call the node even when every explorer throws (the call throws the existing "all backends unavailable" error); broadcast calls explorers first and never the node; with `Disabled` (feature off) the order is exactly today's (explorers first, node last resort for reads; node first for broadcast); the existing `tests/DigiByte.Wallet.Tests/FallbackBroadcastTests.cs` still passes unchanged

### Implementation for User Story 1

- [X] T014 [US1] Change `src/DigiByte.Wallet/Services/FallbackBlockchainService.cs`: add optional `INodeSourceState` and `INodeReadFailureSink` constructor parameters (the monitor implements both); in `TryRead`, step 2 (own node as last resort) runs only when the state is `Disabled` or no monitor is supplied; in `BroadcastTransactionAsync`, the node is tried first only when the state is `Disabled`/absent (today's behaviour); with the feature on and the state not `Active` it is never tried first (the `Active` case arrives in T019) (research R3, FR-002, FR-004, SC-005); update the class summary comment
- [X] T015 [US1] In `src/DigiByte.Web/Program.cs` register a dedicated `HttpClient` named `NodeHealth` with a 3 second timeout and **no** resilience handler, register `NodeSourceOptions` (read from the `OwnNode` configuration section, defaults off; the health source and the node blockchain client use `OwnNode:ApiUrl` when set and `NodeApiUrl` otherwise, while the DigiDollar client and the usage counter keep using `NodeApiUrl` unchanged because DigiDollar is out of scope), `NodeHealthHttpSource`, `ExplorerHeightSource` (over the explorer list already built for `FallbackBlockchainService`), and `NodeSourceMonitor`, and pass the monitor to `FallbackBlockchainService` as both `INodeSourceState` and `INodeReadFailureSink`; create the node blockchain client with `strictErrors` equal to the feature being enabled (T058)
- [X] T016 [US1] Create `src/DigiByte.Web/Services/NodeSourceMonitorLoop.cs`: a service started once at app start that calls `NodeSourceMonitor.CheckAsync()` every `checkInterval` using a periodic timer, never overlapping, never throwing, doing nothing when the state is `Disabled`; start it from `src/DigiByte.Web/Program.cs`
- [ ] T017 [US1] (operator) Run quickstart steps 1 and 2 against the real node while it syncs: with the feature off there is no health request and no change; with it on, balances, history and fees come from the explorers, the log shows only the health call to the node every 30 s, no errors; measure with the browser developer tools that no screen waits more than 1 second on the node (SC-001); until US4 exists, toggle the feature through a local `appsettings.Development.json` override of the `OwnNode` section

**Checkpoint**: User Story 1 is complete when T017 passes; the wallet can ship with the feature on while the node syncs.

---

## Phase 4: User Story 2 - The wallet switches to our own node automatically once it is ready, and back if it stops being ready (Priority: P1)

**Goal**: Once ready and stable, the node is preferred for sending, transaction lookups, fees and height (address balances and spendable outputs stay explorer-first); when it stops being ready the explorers take over within 35 seconds; one failed read falls through for that request; no data is lost or duplicated.

**Independent Test**: [quickstart.md](quickstart.md) steps 3 and 4 against a stub health endpoint.

### Tests for User Story 2

- [X] T018 [P] [US2] Add failing tests to `tests/DigiByte.Wallet.Tests/FallbackNodePreferenceTests.cs` (active half): with state `Active`, fast reads (transaction lookup, fee, height) try the node first and address reads (balance, spendable outputs) try the explorers first with the node only as a fallback when every explorer fails; address history never calls the node, even when every explorer fails (the existing "all backends unavailable" error is thrown); a node transaction lookup that returns null (not found) is not authoritative and the explorers are asked next, and a node request that errors is answered by an explorer, never as height 0, a default fee, zero balance or an empty list; one failing node read is answered by an explorer for that request with correct data and no exception; broadcast tries the node first, falls through to the explorers on failure, and a successful broadcast is never repeated; across 20 forced switches between `Active` and not-ready, balance, UTXO list and history from the fake backends are never lost, duplicated or mixed incorrectly (SC-004); `TransactionTracker` pending entries are unaffected by a switch; a fast node read that exceeds a 5 second budget (20 seconds for an address read used as the fallback) is abandoned and answered by an explorer; 3 consecutive node read failures demote the node immediately (the monitor receives the reports) and later requests skip the node; a successful node read resets the count

### Implementation for User Story 2

- [X] T019 [US2] Change `src/DigiByte.Wallet/Services/FallbackBlockchainService.cs` for `Active`: classify each call as `fast` or `address` (route classes in [contracts/node-readiness.md](contracts/node-readiness.md)); route classes are `fast`, `address` and `history` (history = explorers only, never the node); for `fast` calls in `TryRead`, try the node first (then explorers for that request on failure, each fast node read gets a 5 second budget; for `address` calls keep explorers first and use the node only as the fallback with a 20 second budget; a failed or over-budget node read is answered by an explorer and reported to the monitor with `ReportNodeReadFailure`, and a success is reported too, so repeated failures demote the node without waiting for the next health check); keep the per-explorer cooldown logic as is; in `BroadcastTransactionAsync` try the node first when `Active` and fall through to the explorers on failure, never repeating a successful broadcast (makes T018 pass)
- [X] T020 [US2] In `src/DigiByte.Wallet/Services/FallbackBlockchainService.cs` subscribe to the monitor's `Changed` event and call `InvalidateCache()` when the active source flips, so a cached answer from one source is not served as if it came from the other; record in the code comment that UTXOs are never cached
- [X] T021 [US2] Create `scripts/stub-node-health.ps1`: a small local HTTP listener answering `GET /api/health` with a status chosen from the command line or a keyboard command (`starting`, `syncing`, `ok`, `ok` with `txindex` false, `ok` with `versionOk` false, `blocks` offset, stop), listening on port 5260 by default (the only local node origin the CSP already allows), with permissive CORS for local testing; document the use in the header comment (used by quickstart step 3)
- [ ] T022 [US2] (operator) Run quickstart steps 3 and 4 with the stub: promotion 2 to 2.5 minutes after the node first reports ready (target: within 3 minutes), fallback within 35 s (30 s interval plus the 3 s probe), flapping never promotes, version and drift cases, explorer-unreachable case, sending order in both modes; confirm no flicker or duplicate entries in balance and history across switches

**Checkpoint**: User Stories 1 and 2 deliver the MVP: the wallet follows the node's readiness automatically.

---

## Phase 5: User Story 3 - Users can see which source is active and when the wallet is in a degraded mode (Priority: P2)

**Goal**: A small indicator opens a detail panel naming the source (with the privacy line when the node is active); a banner appears only on real degradation; "nothing available" shows last-known data and explains why sending is unavailable.

**Independent Test**: [quickstart.md](quickstart.md) steps 3 (UI part) and 5.

### Tests for User Story 3

- [X] T023 [P] [US3] Write failing tests in `tests/DigiByte.Wallet.Tests/NodeSourceStatusTests.cs` for a pure `NodeSourceStatus` mapper: every row of the detail-panel table in [contracts/status-ui.md](contracts/status-ui.md) (own node active; explorer with node syncing / unreachable / out of step; feature off; nothing available); the banner shows **only** when the node `NodeWasActive` and fell back, or when all sources failed; a node that is still syncing or never became ready never produces a banner; the privacy line is present only when the own node is active; dot colour per state
- [X] T024 [P] [US3] Write failing tests in `tests/DigiByte.Wallet.Tests/FallbackAllSourcesFailedTests.cs`: `FallbackBlockchainService` exposes an `AllSourcesFailed` flag that is set when a read exhausts every source and cleared by the next successful read, raises a change event, and does not alter the thrown error type

### Implementation for User Story 3

- [X] T025 [US3] Create `src/DigiByte.Wallet/Services/NodeSourceStatus.cs` (pure mapper from monitor state, route class and the all-failed flag, not from the last backend that answered, to dot colour, detail text, banner text and banner visibility, with the exact copy from [contracts/status-ui.md](contracts/status-ui.md); all strings are English only and kept in this one place, matching the existing offline banner, so `en`/`es` locale keys can be added later without touching the logic) (makes T023 pass)
- [X] T026 [US3] Add `AllSourcesFailed` and its change event to `src/DigiByte.Wallet/Services/FallbackBlockchainService.cs` (makes T024 pass)
- [X] T027 [US3] Create `src/DigiByte.Web/Services/NodeSourceStatusService.cs` (scoped; combines the monitor state, `AllSourcesFailed` and `NodeSourceStatus`; the displayed source comes from the monitor state and route class, not from `FallbackBlockchainService.ActiveBackend`, raises one change event for the UI) and register it in `src/DigiByte.Web/Program.cs`
- [X] T028 [P] [US3] Create `src/DigiByte.Web/Components/Shared/NodeSourceIndicator.razor` (small dot, rendered only when the feature is on, opens the detail panel; accessible label)
- [X] T029 [P] [US3] Create `src/DigiByte.Web/Components/Shared/NodeSourceDetail.razor` (the detail panel text from `NodeSourceStatus`, including the privacy line only when the own node is active; closes with the button, overlay or Escape like the other dialogs)
- [X] T030 [P] [US3] Create `src/DigiByte.Web/Components/Shared/NodeDegradedBanner.razor` modelled on `src/DigiByte.Web/Components/Shared/OfflineBanner.razor` (amber banner, shown only per `NodeSourceStatus`, clears by itself)
- [X] T031 [US3] Host the indicator and banner in `src/DigiByte.Web/Layout/MainLayout.razor` (next to the offline banner); no permanent source label on the main screen
- [X] T032 [US3] Inspect what the app does today when the device is online but every source fails: read `src/DigiByte.Web/Pages/Home.razor`, `src/DigiByte.Wallet/Services/WalletService.cs` and the offline cache use in `src/DigiByte.Web/Services/NetworkStatusService.cs`; record in `specs/005-wallet-own-node-source/research.md` under a new heading "R13 Last-known data when all sources fail" what is shown today and what is missing for FR-012 (last known balance and history clearly marked as out of date, and a clear explanation that sending is unavailable); T055 and T056 implement exactly the gaps found
- [X] T055 [US3] Implement the balance and history gaps recorded in R13: when `AllSourcesFailed` is set, show the last known balance and history clearly marked as out of date, reusing the existing out-of-date or cached-data marking where there is one (files named in R13, expected `src/DigiByte.Web/Pages/Home.razor`)
- [X] T056 [US3] In `src/DigiByte.Web/Pages/Send.razor` show a clear message when `AllSourcesFailed` is set explaining that sending is unavailable until the connection returns, instead of a raw error
- [ ] T033 [US3] (operator) Run quickstart steps 3 (UI part) and 5: wording and dot colour for each state, banner only on real degradation, nothing-available behaviour, banner clears by itself

**Checkpoint**: Users and support can see the source and degraded states.

---

## Phase 6: User Story 4 - Operators control node use per deployment without changing code (Priority: P2)

**Goal**: On/off, node address and thresholds come from deployment settings alone; with it off the wallet never contacts the node's health endpoint.

**Independent Test**: [quickstart.md](quickstart.md) steps 1 and 7 using only environment variables.

### Tests for User Story 4

- [X] T034 [P] [US4] Write failing tests in `tests/DigiByte.Wallet.Tests/NodeSourceMonitorTests.cs` (additional cases): with `enabled` false the monitor never calls the health source or the explorer-height source and stays `Disabled`; with `enabled` true but an invalid or unreachable `apiUrl` it reports `Unavailable` without throwing and without repeated state-change events; check intervals from options are respected

### Implementation for User Story 4

- [X] T035 [US4] In `src/DigiByte.Web/Program.cs`, before the services are built, fetch `config.json` from the app's own origin and merge it into configuration (the `OwnNode:*` keys, including `OwnNode:ApiUrl`, from [contracts/runtime-config.md](contracts/runtime-config.md)); a missing, unreadable or invalid file means the feature is off and the app starts normally; never log values
- [X] T036 [P] [US4] Create `docker/web/40-write-config.sh` (executable, runs from `/docker-entrypoint.d/`): writes `/usr/share/nginx/html/config.json` from `WALLET_NODE_ENABLED`, `WALLET_NODE_API_URL`, `WALLET_NODE_CHECK_SECONDS`, `WALLET_NODE_STABLE_SECONDS`, `WALLET_NODE_HEIGHT_TOLERANCE`, `WALLET_DEV_VIEW` with the defaults from the contract, escaping values so the output is always valid JSON, and writes `{}` when nothing is set; check it locally with a small shell test (run the script into a temporary directory with variables unset, set, and containing quotes) and confirm the output parses as JSON
- [X] T037 [US4] Update `docker/web/Dockerfile` runtime stage to copy `40-write-config.sh` into `/docker-entrypoint.d/` and make it executable
- [X] T038 [P] [US4] Update `docker/web/nginx.conf`: an exact-match `location = /config.json` with `Cache-Control: no-cache, no-store, must-revalidate`, placed so it wins over the generic `*.json` one-year immutable rule
- [X] T039 [US4] Check `src/DigiByte.Web/wwwroot/service-worker.published.js`: confirm `config.json` is not in the precached asset manifest (it is written at container start, not at build) and that a request for it goes to the network, not to the cache; adjust the worker if it does not, and keep the change minimal
- [ ] T040 [US4] (operator) Give the node API service from feature 004 a stable custom domain on Railway and record its origin; this origin is the one exact origin added to the CSP in the next task (spec 004 task T019 must be done first)
- [ ] T041 [US4] Add exactly that one origin (https, no wildcard) to `connect-src` in the Content-Security-Policy meta tag of `src/DigiByte.Web/wwwroot/index.html`; flag this as security-relevant in the PR description (constitution: CSP changes)
- [X] T042 [US4] Document the six web-service variables and the CSP limitation (the node address can only move within origins the policy allows) in `docs/RAILWAY_NODE.md`
- [ ] T043 [US4] (operator) Run quickstart steps 1 and 7 using only environment variables on a staging web service: off, on, on with a wrong address, and a rollback by turning it off, each taking effect after a restart with no rebuild

**Checkpoint**: Per-deployment control and instant rollback work.

---

## Phase 7: User Story 5 - The team can try the node now and see when to cut over (Priority: P3)

**Goal**: A developer-only page shows the readiness snapshot and compares the node against the explorers for chosen addresses.

**Independent Test**: [quickstart.md](quickstart.md) step 6.

### Tests for User Story 5

- [ ] T044 [P] [US5] Write failing tests in `tests/DigiByte.Wallet.Tests/NodeSourceComparerTests.cs`: equal balances, UTXO counts and heights within tolerance give `matches`; a differing balance or count gives a mismatch naming the field; one side throwing gives an error on that side and no crash; works for many addresses (20) and returns one row per address; it calls the node directly regardless of the monitor state

### Implementation for User Story 5

- [ ] T045 [US5] Create `src/DigiByte.Wallet/Services/NodeSourceComparer.cs` (compares `NodeApiBlockchainService` in strict mode against an explorer for given addresses, using the batch balance and spendable-output methods so the node scans once for many addresses instead of about 14 seconds per address; produces the Comparison Report from [data-model.md](data-model.md)) (makes T044 pass)
- [ ] T046 [US5] Create `src/DigiByte.Web/Pages/DevNode.razor` at route `/dev/node`: shows the latest readiness snapshot and recent state changes, and an address input with a "Compare" button that renders the report; reads only, changes nothing for regular views
- [ ] T047 [US5] Gate the page: when `OwnNode:DevView` is false the route renders the normal not-found page and no navigation entry exists; add the entry to `src/DigiByte.Web/Layout/NavMenu.razor` only when the setting is on
- [ ] T048 [US5] (operator) When the real node reports `ok`: run quickstart step 6 with at least 20 sample addresses and record that balances, output counts and heights match (SC-008); this is the cut-over gate

**Checkpoint**: The team can prove the node is right before real users depend on it.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T049 [P] Add the privacy paragraph (FR-014a): when the wallet uses the team's own node, the team's servers rather than a third-party explorer see the public addresses the wallet looks up, and keys never leave the device; put it in `docs/SECURITY.md` and in the privacy section of `src/DigiByte.Web/Pages/Help.razor`
- [ ] T050 [P] Add a CHANGELOG.md entry: wallet can use the team's own node when it reports ready, automatic fallback, status indicator and detail, runtime settings, developer comparison page
- [ ] T051 [P] Update `docs/ARCHITECTURE.md` (the "Cascading Blockchain Service" section and the explorer table) with the new read/broadcast order and the readiness monitor
- [ ] T052 Run `dotnet build DigiByte.slnx` and `dotnet test` for `tests/DigiByte.Wallet.Tests`, `tests/DigiByte.Crypto.Tests`, `tests/DigiByte.Api.Tests` and `tests/DigiByte.NodeApi.Tests` from the repo root; fix regressions
- [ ] T053 Security review of the diff against the constitution (CSP origin, runtime config handling, nothing secret in `config.json`, no key or signing path touches the node), and flag the CSP change in the PR description
- [ ] T054 Run the full quickstart.md end to end on a staging deployment and tick off SC-001 to SC-008, recording the results

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none
- **Foundational (Phase 2)**: needs T001; blocks all stories
- **US1 (Phase 3)**: needs Phase 2; this plus US2 is the MVP
- **US2 (Phase 4)**: needs US1 (same file, `FallbackBlockchainService.cs`, edited in order)
- **US3 (Phase 5)**: needs US1 and US2; T026 also edits `FallbackBlockchainService.cs`, so after T020
- **US4 (Phase 6)**: independent of US2 and US3 in code (it only supplies configuration); T041 needs T040; T040 needs spec 004 task T019
- **US5 (Phase 7)**: needs Phase 2 and the node running; T048 needs the real node to report `ok`
- **Polish (Phase 8)**: needs everything above

### Task-Level Notes

- T057 and T058 sit in Phase 2 and must be done before T015 and T019 (added after analysis; IDs are not in execution order).
- T055 and T056 follow T032 (added after analysis; IDs are not in execution order).
- Tests before code: T002 -> T003, T004 -> T005, T006 -> T007, T008 -> T009, T010 -> T011, T013 -> T014, T018 -> T019, T023 -> T025, T024 -> T026, T034 (before its own changes), T044 -> T045.
- `src/DigiByte.Wallet/Services/FallbackBlockchainService.cs` is edited by T014, T019, T020, T026: do them in that order, never in parallel.
- `src/DigiByte.Web/Program.cs` is edited by T015, T016, T027, T035: in that order.
- T009 depends on T001, T003, T005; T015 depends on T003, T007, T009, T011.

### Parallel Opportunities

- Phase 2: T002, T004, T006, T008, T010 (all new test files)
- Phase 5: T023 and T024; then T028, T029, T030
- Phase 6: T034, T036, T038
- Phase 8: T049, T050, T051

## Implementation Strategy

### MVP First (User Stories 1 + 2)

1. Phase 1 and Phase 2 (decision logic, all tested)
2. Phase 3: ship the wallet with the feature available but off, then turn it on while the node syncs (explorer-only behaviour proves nothing breaks)
3. Phase 4: automatic switching, tested with the stub
4. **Stop and validate** with quickstart steps 1 to 4

### Incremental Delivery

1. Foundation, US1 (safe while syncing), US2 (switches itself)
2. US4 (deployment control and rollback) before the first production turn-on
3. US3 (visibility), then US5 (cut-over proof)
4. Cut over: when T048 passes, nothing to deploy; the wallet switches by itself after the stable period

## Notes

- [P] tasks = different files, no dependencies
- Work on branch `feature/wallet-own-node-source`; commit each task or logical group with Conventional Commits
- No secrets anywhere: `config.json` is public to the browser
- The own node is never used while the feature is on and the node is not ready, even when all explorers fail (research R3)
- Out of scope here, recommended as a later feature: an address index on the node so address balances and spendable outputs can also be answered quickly by the node (today about 14 seconds per lookup); after that the address route class can move to node first
