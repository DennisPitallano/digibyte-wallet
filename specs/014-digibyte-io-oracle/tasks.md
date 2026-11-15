# Tasks: Oracle price and DigiDollar status from digibyte.io

**Input**: Design documents from `/specs/014-digibyte-io-oracle/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/oracle-api.md, contracts/digibyte-io-upstream.md, quickstart.md

**Tests**: Included. The project constitution (Principle III) requires tests with every change to `DigiByte.Wallet`, written first and failing before the code they cover. The relay gets contract tests because it is a new public route.

**Organization**: Grouped by user story. The relay route and the wallet client are shared by every story, so they are in the Foundational phase.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: US1 to US4 (see spec.md); Setup, Foundational and Polish tasks have no story label
- Paths are from the repository root

## Phase 1: Setup

- [X] T001 Create branch `feature/digibyte-io-oracle` from an up-to-date `main`, and record the baseline: build `src/DigiByte.Api` and `src/DigiByte.Web`, and run `tests/DigiByte.Api.Tests` and `tests/DigiByte.Wallet.Tests` (note any tests that already fail, such as the timing-sensitive ones and the live explorer benchmarks)

---

## Phase 2: Foundational (blocks every user story)

**Purpose**: The relay route, the wallet's client, the health-model changes and the wrapper that merges them. No screen changes yet.

### Tests first (write them, watch them fail)

- [X] T002 [P] Write relay contract tests in `tests/DigiByte.Api.Tests/OracleEndpointsTests.cs` using an in-process host with a stub for digibyte.io: success shape (contracts/oracle-api.md); two calls within 30 s cause one upstream request per kind; concurrent calls cause one; the last good answer is served until 5 minutes after `fetchedAt` and then the route answers 503; each upstream failure (timeout, non-200, bad JSON, missing or mistyped field, price 0 or negative, `status` other than "active", body over 64 KB, redirect) gives 503; `Oracle:Enabled=false` gives 404; the 31st request in a minute from one visitor gives 429; nothing from the caller (query, headers, cookies) reaches the stub; the stub is only ever asked for the two fixed paths (FR-015); during an upstream outage no new attempt is made within 30 seconds of the previous attempt, successful or not, however many wallets call (SC-012); an update time more than 10 minutes in the future is rejected; the route's log output contains no visitor address (FR-007); an `Oracle:BaseUrl` that is not https (or http on a loopback address) makes the route answer 404
- [X] T003 [P] Write client tests in `tests/DigiByte.Wallet.Tests/OracleDataServiceClientTests.cs`: parses the contract's success answer; rejects each unusable shape (missing field, wrong type, price 0 or negative, unknown extra fields ignored); a fetch is limited to 5 seconds; 404, 429, 503 and network errors leave no reading; `RequestRefresh` starts at most one fetch per 30 seconds, counted from the previous attempt whether it succeeded or failed, and none while one is in flight (so a failed fetch is not retried at once even though `Changed` fires and the screens re-read the health); `Current` is none once the answer's `fetchedAt` is more than 5 minutes old; `IsLoading` is true only while the first fetch is in flight with no current reading; `Changed` fires when a fetch completes; the request has no query string, body or custom headers (SC-005); uses an injected `TimeProvider`; rejects an update time more than 10 minutes in the future; makes no request when the configured address is not https (or http on a loopback address)
- [X] T004 [P] Write freshness tests in `tests/DigiByte.Wallet.Tests/OracleFreshnessTests.cs` for the stale rule in data-model.md: reported stale; more than 20 blocks behind a known height; exactly 20 is not stale; a missing height (`NodeHeight` 0) in an otherwise available answer falls back to the update time (5 minutes); an update block ahead of the height is not stale; `IsDegraded` for each combination
- [X] T005 [P] Write wrapper tests in `tests/DigiByte.Wallet.Tests/OracleEnrichedDigiDollarApiTests.cs`: merges the reading into the inner health on mainnet; leaves the inner health untouched when the reading is missing; an unavailable inner health stays unavailable and carries no oracle fields even when the reading is present; no request on testnet; every other `IDigiDollarNodeApi` call passes straight through; a hanging oracle never delays the inner health call: it returns at once, with `OracleLoading` true on the first load and with the last reading (or none) afterwards

### Implementation

- [X] T006 [P] Add `src/DigiByte.Api/Services/OracleOptions.cs` (`Oracle:Enabled` default true, `Oracle:BaseUrl` default `https://digibyte.io/api`; a base address that is not https, or http on a loopback address, turns the route off, which the route tests in T002 cover)
- [X] T007 Add `src/DigiByte.Api/Services/OracleRelay.cs`: parallel fetch of `getoracleprice` and `getdigidollarstats` (3-second limit each, no redirects, 64 KB cap), strict validation and normalisation per contracts/digibyte-io-upstream.md, one shared snapshot with single-flight refresh, 30-second freshness, serve-old until 5 minutes after `fetchedAt`, no new attempt for a kind within 30 seconds of its previous attempt, successful or not, so an outage is not hammered (research R5), injected `TimeProvider` (depends on T006)
- [X] T008 Add `src/DigiByte.Api/Endpoints/OracleEndpoints.cs`: `GET /api/oracle` answering 200, 503 `{"reason":"unavailable"}` or 404 as in contracts/oracle-api.md, `Cache-Control: no-store`, no input read, no visitor address logged (depends on T007)
- [X] T009 Edit `src/DigiByte.Api/Program.cs`: named client `DigiByteIoOracle` (user agent `DigiByte-Wallet/1.0`, 3-second timeout, redirects off), bind `OracleOptions`, register `TimeProvider` and `OracleRelay`, add a rate-limit policy of 30 requests per minute per visitor using the existing visitor key with a 429 `Retry-After: 60` rejection for `/api/oracle`, and map the route group (depends on T008)
- [X] T010 [P] Add `src/DigiByte.Wallet/Models/OracleReading.cs`: the validated reading (fields in data-model.md) with the shared validity rules and a `TryParse` that never throws
- [X] T011 [P] Add `src/DigiByte.Wallet/Services/IOracleFeed.cs`: the oracle feed with `Current`, `IsLoading`, `RequestRefresh()` and a `Changed` event as in data-model.md; none of them throws or blocks
- [X] T012 Edit `src/DigiByte.Wallet/Models/DigiDollarModels.cs`: add `OracleUpdatedAt`, `OracleReportedStale`, `MintingRestricted`, `AllOperationsRestricted`, `OracleLoading` to `NetworkHealth`, and replace `IsOracleStale` with the three-step rule from data-model.md (depends on T004 being written)
- [X] T013 Add `src/DigiByte.Wallet/Services/OracleDataServiceClient.cs`: implements the oracle feed: `RequestRefresh()` starts a background fetch of the configured route (limited to 5 seconds) only when no fetch is in flight, at least 30 seconds have passed since the previous attempt started (successful or not), and the current answer, if any, is more than 30 seconds old, parses through `OracleReading.TryParse`, keeps one shared answer, exposes it as `Current` only while its `fetchedAt` is at most 5 minutes old, raises `Changed` when a fetch completes, successfully or not, injected `TimeProvider`; sends no query, body or custom headers (depends on T010, T011)
- [X] T014 Add `src/DigiByte.Wallet/Services/OracleEnrichedDigiDollarApi.cs`: wraps an `IDigiDollarNodeApi`; `GetHealthAsync` awaits only the inner call, merges the oracle feed's `Current` and `IsLoading` into the health without waiting for the network, and calls `RequestRefresh()`; it does this on mainnet only and only when the inner health is available (an unavailable health stays unavailable and shows no oracle); the merge is a public static `Merge(NetworkHealth, IOracleFeed, bool isMainnet)` that the view model reuses (T021) and that returns the health unchanged unless it is available and the network is mainnet, so no caller can put oracle data on a test network or on an unavailable health (FR-012); all other calls delegate unchanged (depends on T011, T012)
- [X] T015 Edit `src/DigiByte.Web/Program.cs` and `src/DigiByte.Web/wwwroot/appsettings.json`: add `DigiDollar:OracleApiUrl` (default `https://digibyte-api-production.up.railway.app/api/oracle`; not overridable from `config.json`), register a named client and `OracleDataServiceClient` as the single shared `IOracleFeed` that both the wrapper and the screens receive, validate the address the way the node and gateway addresses are validated (https, or http on a loopback address; otherwise the oracle stays off and no request is made), and wrap the explorer backend with `OracleEnrichedDigiDollarApi` only when the backend is `esplora` and `DigiDollar:OracleEnabled` is true (depends on T013, T014)

**Checkpoint**: T002 to T005 pass; `GET /api/oracle` works locally; the wallet's health carries the oracle fields when the switch is on. No screen shows anything new yet.

---

## Phase 3: User Story 1 - See the oracle price on DigiDollar screens (Priority: P1) MVP

**Goal**: On mainnet with viewing and the oracle switch on, DigiDollar screens show the price with at least six decimals, the update block and its age, and the source note.

**Independent Test**: quickstart.md section 2.

- [X] T016 [P] [US1] Add the text keys in `src/DigiByte.Web/wwwroot/locales/en.json` and `src/DigiByte.Web/wwwroot/locales/es.json`: source note ("Price from digibyte.io. Not verified by the wallet."), loading placeholder, update line with age in blocks
- [X] T017 [US1] Edit `src/DigiByte.Web/Components/DigiDollar/OracleStatusPanel.razor`: show the update block and age in blocks, the source note, and the loading placeholder while `OracleLoading` is true (not the "unavailable" message); the placeholder is announced politely to screen readers and the source note is readable by them (FR-017); the panel's existing `Loading` parameter means the whole page is loading, so `OracleLoading` is a separate input
- [X] T018 [P] [US1] Edit `src/DigiByte.Web/Components/DigiDollar/DigiDollarPriceLine.razor`: add the source note and the loading state, readable by screen readers (FR-017)
- [X] T019 [P] [US1] Check `DigiDollarFormat.DgbPrice` in `src/DigiByte.Wallet/Services/DigiDollarFormat.cs` shows at least six decimals for a sub-cent price (for example $0.002725), and add the test to `tests/DigiByte.Wallet.Tests/` next to the existing formatting tests (FR-002)
- [X] T020 [US1] Check every screen that shows the oracle price (`DigiDollarHomeCard.razor`, `Pages/Analytics.razor`, the DigiDollar pages) carries the source note, and confirm that the main balance card in `src/DigiByte.Web/Pages/Home.razor` (value = total DGB times the price from `GetDgbPriceAsync(AppState.FiatCurrency)`) never reads the DigiDollar health, with an automated test in `tests/DigiByte.Wallet.Tests/` that `FallbackBlockchainService.GetDgbPriceAsync` returns the same market price with the oracle feed on, off and unavailable (change `Home.razor` only if it turns out to read the oracle) (FR-003, FR-018, SC-011)
- [X] T021 [US1] In `src/DigiByte.Web/Services/DigiDollarViewModel.cs`, subscribe to the oracle feed's `Changed` event and, when it fires, re-merge the feed's current reading into `Dashboard.Health` with `OracleEnrichedDigiDollarApi.Merge` (passing whether the current network is mainnet) and raise the view model's `Changed` (unsubscribe in `Dispose`), so the price appears as soon as it arrives and not at the next 15-second poll (SC-001), with a test in `tests/DigiByte.Wallet.Tests/OracleEnrichedDigiDollarApiTests.cs` that the re-merge updates the health and raises `Changed`, and that a re-merge on a test network, or on an unavailable health, leaves the health unchanged (FR-012, SC-007); and check the view model's poll (`PollInterval`) reads the health through the shared 30-second cache, so a screen left open makes at most one oracle request per 30 seconds (SC-006), and add three tests in `tests/DigiByte.Wallet.Tests/OracleDataServiceClientTests.cs`: requests are counted over a simulated 10 minutes and stay at or below 20; a changed answer is picked up by the next refresh without a reload (scenario US1.3); a failed fetch is not retried before 30 seconds have passed, however often the screens re-read the health

**Checkpoint**: The price, its freshness and its source show on DigiDollar screens with the oracle switch on.

---

## Phase 4: User Story 2 - Never trust a missing or old price (Priority: P1)

**Goal**: Every failure shows "unavailable"; nothing old is shown as current; no other screen or action is affected.

**Independent Test**: quickstart.md section 3.

- [X] T022 [US2] Add the remaining failure cases to `tests/DigiByte.Wallet.Tests/OracleEnrichedDigiDollarApiTests.cs`: client timeout at 5 seconds; 404, 429, 503 and network errors; an answer older than 5 minutes after a successful one (simulated clock); each case leaves the oracle fields empty so the existing "unavailable" state shows; a hung data service leaves the health available at once, and the oracle area moves from loading to unavailable within 6 seconds (SC-003)
- [X] T023 [US2] Verify in `src/DigiByte.Wallet/Services/OracleEnrichedDigiDollarApi.cs` that a failing or hanging oracle never delays or fails the explorer's health call (the merge is non-blocking by design, T014), and that DGB and DigiDollar balance, history, receive and send do not depend on it (FR-010, SC-003)
- [X] T024 [US2] Add a send-check test in `tests/DigiByte.Wallet.Tests/` that, with the oracle unavailable, the send is still allowed and the existing "price unavailable" warning appears (spec 001 FR-019, scenario US2.5)
- [X] T025 [US2] After T017 and T018, edit `src/DigiByte.Web/Components/DigiDollar/OracleStatusPanel.razor` and `DigiDollarPriceLine.razor` so "loading" (`OracleLoading`) and "unavailable" are two different states (a normal load never flashes the error)

**Checkpoint**: All failure drills in quickstart.md section 3 end in "unavailable" with every other screen working.

---

## Phase 5: User Story 3 - See when DigiDollar is under protection or the price is stale (Priority: P2)

**Goal**: Stale, volatility, emergency, minting-restricted and all-operations-restricted states each show their own notice, and none blocks a send.

**Independent Test**: quickstart.md section 4.

- [X] T026 [P] [US3] Add the mapping tests with synthetic non-normal answers to `tests/DigiByte.Api.Tests/OracleEndpointsTests.cs`: `protection_active`, `minting_restricted`, `all_operations_restricted`, `is_emergency`, and `burn_multiplier` above 1 each set only their own flag in the route's answer
- [X] T027 [P] [US3] Add `MintingRestricted` and `AllOperationsRestricted` to `DdDegradedCondition` in `src/DigiByte.Wallet/Services/IDigiDollarService.cs` and map them in `DegradedConditions` in `src/DigiByte.Wallet/Services/DigiDollarService.cs`, with tests in `tests/DigiByte.Wallet.Tests/` that each condition appears only in its own state (SC-009)
- [X] T028 [US3] Edit `src/DigiByte.Web/Components/DigiDollar/DigiDollarHealthBanner.razor` and `DigiDollarConfirmDialog.razor` to show the two new notices, each with its own wording, in the existing banner and warning list (and keep the existing stale, volatility and emergency notices), with the text in `locales/en.json` and `locales/es.json`, using the same roles and announcements as the existing banners so screen readers and keyboard users get them (FR-017)
- [X] T029 [US3] Add a send test in `tests/DigiByte.Wallet.Tests/` that a DigiDollar send can be completed in every state the source can report, including "all operations restricted" and "minting restricted" (SC-010, scenarios US3.7 and US3.8)

**Checkpoint**: Each notice appears in its own state and in no other; sends are never blocked by this feature.

---

## Phase 6: User Story 4 - Control over whether and when it is used (Priority: P3)

**Goal**: The oracle can be switched off at either end; it makes no request off mainnet, with the switch off, or on the node backend; the browser's content policy is unchanged.

**Independent Test**: quickstart.md sections 1, 3 (switch off) and 6.

- [X] T030 [US4] Add tests in `tests/DigiByte.Wallet.Tests/OracleEnrichedDigiDollarApiTests.cs` that the wallet makes no oracle request with `OracleEnabled` false, on testnet, and with the node backend (SC-007)
- [X] T031 [P] [US4] Add `tests/DigiByte.Wallet.Tests/OracleContentPolicyTests.cs` that reads `src/DigiByte.Web/wwwroot/index.html` and asserts it does not mention digibyte.io (FR-016) and still lists the data service origin
- [X] T032 [P] [US4] Update documentation: `docs/SECURITY.md` (data flows through the relay, nothing user-specific sent, content policy unchanged), `docs/ARCHITECTURE.md` (the relay and the wrapper), `railway.toml` and `docs/RAILWAY_NODE.md` (the service settings `Oracle__Enabled` and `Oracle__BaseUrl`, and the wallet's `WALLET_DD_ORACLE`)
- [X] T033 [US4] Amend `specs/001-digidollar-wallet/spec.md` (FR-019 and FR-021) and `specs/001-digidollar-wallet/quickstart.md` so they no longer say the main balance's dollar value comes from the oracle price (spec 014, FR-018), and note the explorer-backend oracle feed

**Checkpoint**: Switches and scope rules proven by tests; documents match the behaviour.

---

## Phase 7: Polish and rollout

- [X] T034 Resolve the known gap before the display is switched on: check the meaning of `err_tier.burn_multiplier`, `is_emergency` and the restriction flags in a non-normal state against DigiByte Core or its DigiDollar documentation; record the result in `specs/014-digibyte-io-oracle/research.md` (R3) and adjust the mapping and tests if the meaning differs
- [X] T035 [P] Add the opt-in live check `tests/DigiByte.Api.Tests/OracleLiveTests.cs` (runs only with `ORACLE_LIVE=1`): the route returns a valid reading from the real digibyte.io, and its price equals a direct read of the real site's `getoracleprice` made at the same moment, allowing for one update between the two reads (SC-002)
- [X] T036 Run all automated checks: `dotnet build` for `src/DigiByte.Api` and `src/DigiByte.Web`; `dotnet test` for `tests/DigiByte.Api.Tests` and `tests/DigiByte.Wallet.Tests` (excluding the live benchmarks); confirm only the baseline failures from T001 remain
- [ ] T037 Walk through quickstart.md sections 1 to 6 locally (including the privacy check and every failure drill), measure the time to the price over 20 loads on a throttled connection of about 10 Mbit/s with 100 ms delay (95% within 3 seconds, SC-001), do a keyboard and screen-reader pass over the new elements (FR-017), and note the results in the pull request
- [ ] T038 Open the pull request: describe the data flow (wallet to data service to digibyte.io), state that the content policy is unchanged, flag the new anonymous public route as security-relevant (constitution merge gates), link the amended spec 001 text, and list the rollout steps from quickstart.md section 7; deploy the service route first, then the wallet build, then set `WALLET_DD_ORACLE=true`

---

## Dependencies and execution order

- **Phase 1** first. **Phase 2** blocks everything else; inside it, tests T002 to T005 come first and run in parallel; T006 to T009 (service) and T010 to T015 (wallet) are two chains that can proceed in parallel after their own tests.
- **US1** (Phase 3) needs Phase 2. **US2** (Phase 4) needs Phase 2 and shares files with US1 (T025 follows T017 and T018). **US3** (Phase 5) needs Phase 2 and is otherwise independent. **US4** (Phase 6) needs Phase 2; its tests and documents are independent of US1 to US3.
- **Polish** last; T034 must finish before the display is switched on in production.

## Parallel examples

- Tests: T002, T003, T004 and T005 are separate files and run together.
- Service and wallet chains: T006 to T009 alongside T010 to T015.
- Phase 3: T016, T018 and T019 are separate files.
- Phase 5: T026 and T027 are separate files.
- Phase 6: T031 and T032 are separate files.

## Implementation strategy

- **MVP**: Phases 1 and 2, then US1 and US2 together (both are priority 1: a price that is safe to show), validated with quickstart sections 2 and 3. Deploy the route and the wallet build with the switch off; switch on after T034.
- **Then**: US3 (notices), then US4 (switch proofs, documents, spec 001 amendment), then Polish.
- **Rollout**: service route first, wallet build second, `WALLET_DD_ORACLE=true` last, with the kill switches described in research.md R10.

## Summary

| Phase | Tasks | Count |
|---|---|---|
| 1 Setup | T001 | 1 |
| 2 Foundational | T002 to T015 | 14 |
| 3 US1 | T016 to T021 | 6 |
| 4 US2 | T022 to T025 | 4 |
| 5 US3 | T026 to T029 | 4 |
| 6 US4 | T030 to T033 | 4 |
| 7 Polish | T034 to T038 | 5 |
| **Total** | | **38** |
