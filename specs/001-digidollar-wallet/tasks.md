---

description: "Task list for DigiDollar in the DGB Wallet"
---

# Tasks: DigiDollar in the DGB Wallet

**Input**: Design documents from `/specs/001-digidollar-wallet/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: INCLUDED. Constitution Principle III (Test-First for Crypto and Wallet logic, non-negotiable) requires tests in `tests/DigiByte.Crypto.Tests/` and `tests/DigiByte.Wallet.Tests/` for every change to `DigiByte.Crypto/` and `DigiByte.Wallet/`, including negative-path tests. Write each test first and confirm it FAILS before implementing.

**Organization**: Grouped by user story (US1 hold and receive, US2 send, US3 network health and USD context) so each can be built and tested independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1, US2, US3 (user-story phases only)
- All paths are relative to the repository root `C:\GitHub\digibyte-wallet`

## Path Conventions

Monorepo, no new projects (plan.md Structure Decision):
`src/DigiByte.Crypto/` (protocol), `src/DigiByte.Wallet/` (orchestration), `src/DigiByte.Web/` (Blazor UI), `src/DigiByte.NodeApi/` (chain relay), `tests/<Project>.Tests/`.

---

## Phase 1: Setup

**Purpose**: Verify the protocol assumptions the whole plan rests on, and clear the custodial work-in-progress that conflicts with the spec.

- [X] T001 Check whether the v9.26.5 node already ships a non-wallet RPC that returns DigiDollar outputs for an arbitrary address (research R3). **Done 2026-10-19**: none found in the `feature/digidollar-v1` source; the NodeApi keeps a DD output index (now simpler: it decodes zero-value P2TR outputs plus the single DD OP_RETURN, research R3). Recorded in research R3.
- [ ] T002 Capture testnet26 vectors per research R8 and save them as JSON under `tests/DigiByte.Crypto.Tests/Vectors/digidollar-testnet26.json`: the first receive and change DD addresses for the test mnemonic (via the `tr()` descriptor import in quickstart step 2), one real DD transfer transaction hex with its prevouts (to prove a transaction built per research R1 is accepted by a v9.26.5 node and to confirm the sighash type), and a captured DD block for the indexer test. **Blocked on a testnet26 node**: none is available in the current dev environment; the BIP86 official test vectors (T009) cover the tweak math meanwhile.
- [X] T003 Resolve the five blocking Send verification items in research R1. **Done 2026-10-19** from the DigiByte-Core source: amounts are `CScriptNum` cents in one `OP_RETURN "DD" <type=2> <amounts…>`; no leaf and no script tree; the metadata OP_RETURN is required; DGB fee inputs may be P2WPKH; minimum fee 0.1 DGB builder policy; `nVersion = 0x02000770`; unconfirmed DD inputs are rejected. Recorded in research R1. Remaining confirmation (acceptance by a real node and the sighash type) is part of T002 and quickstart step 4.
- [X] T004 [P] Remove the custodial wallet surface (FR-026, FR-028, research R7): delete `src/DigiByte.Web/Pages/DigiDollarMint.razor`, `src/DigiByte.Web/Pages/DigiDollarRedeem.razor`, `src/DigiByte.Web/Pages/DigiDollarPositions.razor`, and `src/DigiByte.Web/Services/DigiDollarService.cs`; remove the `DigiDollarService` and `WriteKey` registration at `src/DigiByte.Web/Program.cs` lines ~114-125 and drop `DigiDollar.WriteKey` from `src/DigiByte.Web/wwwroot/appsettings.json`. Keep the routes `/digidollar`, `/digidollar/receive`, `/digidollar/send` but replace each of `DigiDollar.razor`, `DigiDollarReceive.razor`, `DigiDollarSend.razor` with a minimal placeholder (an `@page` plus a single "DigiDollar is being rebuilt" paragraph, no `@inject` of the removed service) so the build stays green until the Phase 3 and Phase 4 page reworks; also remove any NavMenu or Home links that reference the deleted pages.
- [X] T005 Restrict the NodeApi wallet-scoped DigiDollar endpoints (`/address/new`, `/balance`, `/send`, `/send-many`, `/mint`, `/redeem`, `/positions`) to the DigiPay caller. The existing `src/DigiByte.NodeApi/Middleware/WriteKeyAuthMiddleware.cs` only guards POST and only "when configured", so: (a) write the failing tests first in `tests/DigiByte.NodeApi.Tests/DigiDollarReadEndpointsTests.cs` (an unkeyed call to each endpoint returns 401, including the GET endpoints `/balance` and `/positions`, and with no key configured the group fails closed, never open); (b) change the middleware in `src/DigiByte.NodeApi/Middleware/WriteKeyAuthMiddleware.cs` so the whole wallet-scoped group requires `X-NodeApi-Key`, and the group returns 503 when DigiDollar is enabled but no key is configured; (c) add a CORS policy in `src/DigiByte.NodeApi/Program.cs` so the wallet origin cannot call that group (contracts/nodeapi-digidollar-read.md, research R7). Depends on T006 for the test file.
- [X] T006 [P] Add the new test files as empty classes so the filters in quickstart step 1 resolve: `tests/DigiByte.Crypto.Tests/DigiDollarAddressTests.cs`, `DigiDollarKeyDerivationTests.cs`, `DigiDollarTransferBuilderTests.cs`; `tests/DigiByte.Wallet.Tests/DigiDollarServiceTests.cs`; `tests/DigiByte.NodeApi.Tests/DigiDollarReadEndpointsTests.cs`.

---

## Phase 2: Foundational (blocks all user stories)

**Purpose**: DD address and key primitives, encrypted storage, the NodeApi read surface, feature flags, and shared models. No user story can start until these are done.

**CRITICAL**: Test tasks come first in each group; confirm they fail before implementing.

### Crypto primitives (Constitution III)

- [X] T007 [P] Write failing tests in `tests/DigiByte.Crypto.Tests/DigiDollarAddressTests.cs`: encode then decode round trip on mainnet and testnet; reject a DGB bech32 address, a wrong-network DD address, a bad checksum, wrong length, surrounding whitespace, and an empty string (research R8, FR-006).
- [X] T008 Add `Encode(XOnlyPubKey, network)` to `src/DigiByte.Crypto/Addresses/DigiDollarAddressValidator.cs` (existing file) using the two-byte DD version bytes the validator already defines; make T007 pass.
- [X] T009 [P] Write failing tests in `tests/DigiByte.Crypto.Tests/DigiDollarKeyDerivationTests.cs`: the official BIP86 test vectors (mnemonic "abandon … about", path `m/86'/0'/0'/0/0`, expected output key `a60869f0dbcf1dc659c9cecbaf8050135ea9e8cdc487053f1dc6880949dc684c`, and the `/0/1` and `/1/0` vectors) pass through the tweak code; once T002 is available, the testnet26 DD addresses at `/0/0`, `/0/1`, `/1/0` match; the DD address encodes the tweaked OUTPUT key (research R2); mainnet uses coin type 20 and the coin type is an explicit parameter; negative: empty mnemonic, wrong network.
- [X] T010 Implement `src/DigiByte.Crypto/KeyGeneration/DigiDollarKeyDerivation.cs`: derive BIP86 Taproot keys on `m/86'/{coin}'/0'/{chain}/{index}` (receive chain 0, change chain 1; coin type is an explicit parameter, default 20 mainnet and 1 testnet per Core descriptor convention, research R2) and return the internal key, the BIP341 key-path-tweaked OUTPUT key (no script tree), and the tweaked private key for signing; make T009 pass. Keys exist only in memory (Constitution I/II).

### Shared models and storage

- [X] T011 [P] Create `src/DigiByte.Wallet/Models/DigiDollarModels.cs` with the records from data-model.md: `DigiDollarAccount` (adds `ScanComplete`, `LastScanAt`), `DigiDollarAddress` (Chain enum {Receive, Change}; label max 64 chars; unique on WalletId, Chain, Index), `DigiDollarBalance`, `DigiDollarUtxo`, `DigiDollarTransaction` (Direction {Received, Sent, SelfTransfer}, Status {Pending, Confirmed, Failed}), `DigiDollarCache`, `PendingDdSend` (with `SpentOutpoints`, `FirstMissingAt`, `Rebroadcasted`), `NetworkHealth` (with `DeploymentActive`, `OraclePriceAvailable`; `IsDegraded = ErrActive ∨ VolatilityFreeze ∨ IsOracleStale ∨ ¬OraclePriceAvailable`). Amounts: DD in cents (`long`), DGB in satoshis (`long`).
- [X] T012 Write failing tests then implement the encrypted DD store in `src/DigiByte.Wallet/Storage/DigiDollarStore.cs` (new) with tests in `tests/DigiByte.Wallet.Tests/DigiDollarStoreTests.cs`: persist `DigiDollarAccount`, addresses, `DigiDollarCache`, and `PendingDdSend` as one JSON blob encrypted via `ICryptoService` under a store secret derived from the unlocked seed with HKDF-SHA256 in `src/DigiByte.Crypto/Hashing/Hkdf.cs` (the PIN is not retained after unlock; FR-008, research R11); negative tests: wrong secret returns nothing, stored blob contains no key or seed material, ciphertext contains no address text. Also keep the single plaintext boolean `dd.used` per wallet (set on first DD open) through `ISecureStorage`.
- [X] T013 Rework `src/DigiByte.Web/Services/DigiDollarFeatureFlag.cs` into separate View and Send flags read from `DigiDollar:ViewEnabled` and `DigiDollar:SendEnabled` (FR-022), with `ddVisible = ViewEnabled && network ∈ {mainnet, testnet} && walletHasSeed` and `ddSendVisible = ddVisible && SendEnabled && backendReachable && deploymentActive` (contracts/wallet-ui-contract.md, research R9). Regtest always hides DD (FR-029); `backendReachable` and `deploymentActive` are inputs supplied by the health client (T018), so the flag service only combines them; non-seed wallet types resolve to a `NotSupportedForWalletType` result (FR-030). Update `src/DigiByte.Web/wwwroot/appsettings.json` to `"DigiDollar": { "ViewEnabled": false, "SendEnabled": false }`.

### NodeApi read surface (needed by every story)

- [X] T014 [P] Write failing tests in `tests/DigiByte.NodeApi.Tests/DigiDollarReadEndpointsTests.cs`: every endpoint returns `503 {"error":"digidollar_disabled"}` when disabled; `400 invalid_address` for non-DD strings; batch endpoints return `413` above 200 addresses and `400` listing invalid addresses; `GET /tx/{txid}/status` returns `mempool` / `confirmed` / `unknown` and `400 invalid_txid`; the indexer decodes the captured block from T002 and marks spends (contracts/nodeapi-digidollar-read.md). Add a log-capture test: after calling the batch endpoints with known addresses, no captured log line (including the framework request log) contains any of those addresses, and no wildcard single-address route such as `/address/{address}/utxos` is registered. If T001 finds a node-level RPC instead of an indexer, replace the indexer-decoding test with a test of the RPC wrapper.
- [X] T015 Implement the DD output index in `src/DigiByte.NodeApi/Services/DdOutputIndexer.cs` (new `BackgroundService`, SQLite tables `DdOutput`, `DdTxSummary`, `IndexerState` per data-model.md Server-side): walk blocks from DD activation (mainnet 23,869,440), select txs where `(nVersion & 0xFFFF) == 0x0770`, pair each zero-value canonical P2TR output (`OP_1 <32-byte key>`) with its amount from the single DD `OP_RETURN` (`"DD" <type> <cents>…`, recipient order then change), store the output key, amount cents, outpoint and creating txid, mark spends, track the mempool, and roll back to the fork point on a hash mismatch. T001 found no node-level RPC, so the indexer is required.
- [X] T016 Add the address-scoped read group to `src/DigiByte.NodeApi/Endpoints/DigiDollarEndpoints.cs`: extend `GET /health` (adds `deploymentActive`, `oracle` which is `null` with no usable price, `protection`, `indexer.lagBlocks`), `POST /addresses/utxos`, `POST /addresses/history` (cursor paging, `count` default 50), `GET /tx/{txid}/status`, and reuse the existing `POST /tx/broadcast`. Per-IP rate limiting with `429` and `Retry-After`; CORS allows the wallet origin for this group only; error envelope `{ "error": "<snake_case>", "reason": "<optional>" }` with no stack traces. DigiDollar addresses are accepted only in POST bodies, never in URL paths or query strings, so no single-address GET exists (FR-023, research R12 privacy note); request logging for this group MUST NOT record request bodies, and the group must log no client IP together with any address. Make T014 pass.
- [X] T017 [P] Write failing tests for the DD read client in `tests/DigiByte.Wallet.Tests/DigiDollarNodeApiClientTests.cs` (stub the `HttpMessageHandler`): `503`, timeout, and network failure map to `Available = false`; `enabled:false` health maps to unavailable; `400 invalid_address` and `413` surface as typed errors; `oracle: null` maps to `OraclePriceAvailable = false`; a recorded-request assertion that the client never calls a wallet-scoped endpoint (`/address/new`, `/balance`, `/send`, `/send-many`, `/mint`, `/redeem`, `/positions`) (Constitution Principle III, FR-003, research R14).
- [X] T018 [P] Make T017 pass: add the DD read client with the shared models in `src/DigiByte.Wallet/Services/IDigiDollarService.cs` and `src/DigiByte.Wallet/Services/DigiDollarNodeApiClient.cs` (new): methods for health, batch UTXOs, batch history, tx status, and broadcast; map `503`, timeout, or network failure to an `Available = false` result; no wallet-scoped calls.

**Checkpoint**: Foundation ready. Address and key primitives, encrypted store, flags, and read endpoints exist, so user-story work can begin.

---

## Phase 3: User Story 1 - Hold and receive DigiDollar (Priority: P1) 🎯 MVP

**Goal**: A seed-based wallet user with DigiDollar enabled sees a DD balance on Home, generates and shares receive addresses, sees incoming payments in history, and gets their funds back after restoring on a new device.

**Independent Test**: With `ViewEnabled=true` on testnet, restore the test mnemonic, open Receive, and pay the address from an external wallet; the balance, a "Received" history entry, and the confirmation count appear within one minute (quickstart scenarios 1-5, 16, 17, 19, 20).

### Tests for User Story 1 (write first, must fail)

- [X] T019 [P] [US1] In `tests/DigiByte.Wallet.Tests/DigiDollarServiceTests.cs` add failing tests against a mocked NodeApi: balance aggregation across multiple addresses (confirmed versus pending shown separately, per-address map); new receive address advances `NextReceiveIndex` and persists; restore scan with gap limit 20 over both chains stops after 20 consecutive unused addresses and sets the next indexes to `highestActive + 1` (research R12); scan resumes from the last checked index and does not persist a partial result as final when the backend fails mid-scan; backend unavailable returns `UnavailableCached` with the cached snapshot.
- [X] T020 [P] [US1] In the same test file add failing history tests: Direction classification (Received, Sent, SelfTransfer when every DD output is own), counterparty derivation, net amount, and Failed/Pending/Confirmed status mapping.

### Implementation for User Story 1

- [X] T021 [US1] Implement receive and read logic in `src/DigiByte.Wallet/Services/DigiDollarService.cs` (new; the old copy was removed in T004): `GetBalanceAsync`, `GetHistoryAsync`, `NewReceiveAddressAsync` (derives locally via T010, validates via T008/FR-006, persists via T012), `ScanAsync` per research R12, cached-snapshot fallback per research R14, and the `dd.used` marker set on first open. All amounts in cents; display conversion stays in the UI.
- [X] T022 [US1] Register `IDigiDollarService` and its dependencies in `src/DigiByte.Web/Program.cs` and wire the 15 s foreground poll (paused when the tab is hidden) with a manual refresh that also re-runs the scan (FR-010, research R6).
- [X] T023 [P] [US1] Create `src/DigiByte.Web/Components/DigiDollar/DigiDollarHomeCard.razor` and mount it in `src/DigiByte.Web/Pages/Home.razor`: confirmed and pending USD balance, tap-through to `/digidollar`, hidden unless `ddVisible`, never blocks DGB rendering (FR-001, FR-002, FR-004).
- [X] T024 [P] [US1] Create `src/DigiByte.Web/Components/DigiDollar/DigiDollarUnavailableLine.razor` and mount it in `src/DigiByte.Web/Pages/Home.razor`: the single neutral line `dd.flagOff.line` shown only when `ddUnavailableLine` (view switch off, seed wallet, `dd.used` set); no balance and no navigation (FR-002).
- [X] T025 [US1] Rework `src/DigiByte.Web/Pages/DigiDollar.razor`: balance (confirmed and pending), recent history (direction, counterparty, USD amount, confirmations, timestamp, explorer link per research R10), Receive and Send actions, and the UI states `Loading`, `Scanning` (progress instead of `$0.00`), `Ready`, `Empty`, `UnavailableCached` (cached label "as of …"), `Unavailable`, `NotActive` (`dd.notActive`, no receive or send actions, rechecked on refresh, FR-003), and `NotSupportedForWalletType` (FR-030). Redirect to `/` with the `dd.unavailable.toast` toast when the route condition turns false mid-session. Explorer links use a per-network base URL read from config (`DigiDollar:ExplorerBaseUrl` for mainnet and testnet in `src/DigiByte.Web/wwwroot/appsettings.json`); confirm the testnet explorer URL when wiring this.
- [X] T026 [US1] Rework `src/DigiByte.Web/Pages/DigiDollarReceive.razor`: derive the address locally (no `/address/new` call), show address plus QR, list own receive addresses with per-address balance, and hide change addresses (FR-005, FR-007); receive keeps working in `UnavailableCached`, but is hidden in `NotActive`.
- [X] T027 [P] [US1] Add `dd.*` strings for US1 states to `src/DigiByte.Web/wwwroot/locales/en.json` and `src/DigiByte.Web/wwwroot/locales/es.json` (the only locales in the repo; others fall back to English per FR-025): `dd.unavailable.toast`, `dd.flagOff.line`, `dd.notActive`, scanning and cached labels, empty and unsupported-wallet states. Use locale `NumberFormatInfo` and `$` for amounts.
- [X] T028 [US1] (Done: the entry point is the Home tile, not a sixth bottom-bar tab; the bar already has five items.) Confirm every interactive element has an accessible name and banners use `role="status"` (`role="alert"` for unavailable) (FR-024).

**Checkpoint**: US1 works end to end on testnet26. Shipping only this phase gives a hold-and-receive wallet.

---

## Phase 4: User Story 2 - Send DigiDollar (Priority: P2)

**Goal**: A user sends DigiDollar to a valid same-network address, signed on-device, with a pending entry that resolves to confirmed or failed.

**Independent Test**: With at least $5 of DD and enough DGB for the fee, send $2.00 to the external wallet; the confirm dialog shows the full address, the amount, the fee and the custody line; history shows Pending then Confirmed (quickstart scenarios 6-10, 18, 21-23).

**Gate**: T003 (R1 verification items) must be closed before T032.

### Tests for User Story 2 (write first, must fail)

- [X] T029 [P] [US2] After T003, write failing tests in `tests/DigiByte.Crypto.Tests/DigiDollarTransferBuilderTests.cs`: the transaction has `nVersion = 0x02000770`, DD outputs are `OP_1 <32-byte key>` with value 0, exactly one DD `OP_RETURN` (`"DD"`, `CScriptNum(2)`, cents per DD output, recipients then change) whose amount count matches the DD output count; a golden-vector transaction from T002 matches byte for byte before signing once T002 exists; DD inputs below 1 confirmation are rejected (`dd-input-amounts-unknown`); sighash check; conservation violation throws; amount below 100 cents throws; above 10,000,000 cents throws; DD change greater than 0 and below 100 cents throws; "send all" builds with no DD change output; insufficient DGB for the fee throws; DD change goes to a fresh `/1/i` key; the automatic fee is `max(policyMinDdFee, feeRate × vsize)` where `policyMinDdFee` is the value confirmed in T003 (0.1 DGB per current repo notes; update this test if T003 finds a different protocol minimum) and is never below it (FR-012, FR-014, research R1, R5, R15).
- [X] T030 [P] [US2] In `tests/DigiByte.Wallet.Tests/DigiDollarServiceTests.cs` add failing send tests: rate limiter allows 5 per rolling 60 s and blocks the sixth, resets on lock or unlock; available DD excludes outpoints reserved by non-Failed pending sends; validation order from data-model.md (address, bounds, change minimum, available balance, DGB fee, rate limit, backend reachable); pending lifecycle per research R13: stays Pending while `mempool` or `confirmed`, `unknown` starts `FirstMissingAt`, becomes Failed after 30 consecutive minutes absent with funds released, the timer does not advance while the backend is unreachable, one automatic rebroadcast on first `unknown`; record removed after 6 confirmations or after the Failed notification.

### Implementation for User Story 2

- [X] T031 [P] [US2] Create `src/DigiByte.Crypto/Transactions/DigiDollarScripts.cs`: the DD output script `OP_1 <32-byte output key>` (canonical P2TR, value 0), the DD `OP_RETURN` payload builder/parser (`"DD"`, `CScriptNum(2)`, `CScriptNum` cents per DD output, recipients then change), and `MakeDigiDollarVersion(type, flags) = (type << 24) | (flags << 16) | 0x0770` (TRANSFER = `0x02000770`), per research R1.
- [X] T032 [US2] Create `src/DigiByte.Crypto/Transactions/DigiDollarTransferBuilder.cs`: build the transaction manually, not through `TransactionBuilder` (research R4), with `nVersion = 0x02000770` (research R1); DD inputs (confirmed only, at least 1 confirmation) spent by key path with the BIP86-tweaked private key and signed `SigHash.Default`; DGB inputs signed through the existing P2WPKH path; recipient DD output, DD change output only when change is 0 or at least 100 cents, DGB change output (only if at least 1000 sats), and the single DD `OP_RETURN` appended last (built by T031); prevouts (scriptPubKey and value for DD and DGB UTXOs) feed the Taproot sighash. Coin selection: DD inputs largest-first, preferring a combination with zero change or change ≥ 100 (research R15); DGB fee from `UtxoSelector.SelectLargestFirst`. Make T029 pass.
- [X] T033 [US2] Add send logic to `src/DigiByte.Wallet/Services/DigiDollarService.cs`: `ValidateSendAsync` (all rules in data-model.md Draft → Confirming, including 2b and the reachability rule), `SendAsync` (build, sign in memory only, broadcast, persist `PendingDdSend` with `SpentOutpoints`), `SendAllAsync`, the 5 per 60 s limiter, tx status polling with the 30-minute absence timer and the one-time rebroadcast, and a failure notification when a send becomes Failed (FR-011..017, research R13). Map known reject reasons (for example `bad-dd-conservation`, `dd-output-below-minimum`) to user messages. Make T030 pass.
- [X] T034 [P] [US2] Create `src/DigiByte.Web/Components/DigiDollar/DigiDollarConfirmDialog.razor`: full recipient address (never truncated), USD amount, DGB fee, an own-address note, the plain-language custody line (FR-026), and a degraded-state warning line naming the condition when `IsDegraded` (stale oracle, ERR or volatility freeze, or "price unavailable", FR-027). Focus trap, closes on Esc, and the primary button is disabled for 1 s after opening.
- [X] T035 [US2] Rework `src/DigiByte.Web/Pages/DigiDollarSend.razor` with a new `src/DigiByte.Web/Components/DigiDollar/DigiDollarSendForm.razor`: recipient, USD amount, a "Send all" button, and the computed DGB fee shown read-only with no fee selector or custom input (FR-014). Inline validation with the keys `dd.send.err.notDdAddress`, `wrongNetwork`, `belowMin`, `aboveMax`, `changeBelowMin` (with the "Send all" action), `insufficientDd`, `insufficientDgbFee`, `slowDown`, `offline`. Route visible only when `ddSendVisible`. Show the pending entry immediately in history after a send (FR-017). Verify every control has an accessible name, the form is keyboard operable, and inline errors use `role="alert"` (FR-024).
- [X] T036 [P] [US2] Add the `dd.send.*` strings (errors above, `dd.send.failed`, custody line, own-address note, confirm warnings including `dd.confirm.warn.priceUnavailable`) to `src/DigiByte.Web/wwwroot/locales/en.json` and `es.json`.

**Checkpoint**: US1 and US2 both work independently. Do not enable `SendEnabled` anywhere until the manual testnet26 run in quickstart passes (research R8).

---

## Phase 5: User Story 3 - Network health and USD context (Priority: P3)

**Goal**: Users see the oracle USD price with freshness, banners for degraded states, and the USD equivalent of their DGB balance.

**Independent Test**: With a fresh oracle the price and age show; stubbing a stale oracle, a lagging node, no price, and an inactive network each produces its banner or state without blocking the rest of the wallet (quickstart scenarios 11, 12, 24, 25).

### Tests for User Story 3 (write first, must fail)

- [X] T037 [P] [US3] In `tests/DigiByte.Wallet.Tests/DigiDollarServiceTests.cs` add failing tests for health mapping: `IsOracleStale` when `NodeHeight − OracleHeight > 20`; `IsNodeLagging` when `NetworkHeight − NodeHeight > 6`; `OraclePriceAvailable = false` when `oracle` is null or zero; `IsDegraded` is true for ERR, volatility freeze, stale oracle, or no price; `DeploymentActive = false` yields the NotActive state; `Available = false` on 503, timeout, or `enabled:false`.

### Implementation for User Story 3

- [X] T038 [US3] Add `GetHealthAsync` and the derived flags to `src/DigiByte.Wallet/Services/DigiDollarService.cs` and expose oracle price and freshness for the UI; make T037 pass (FR-018..020, research R6, R16).
- [X] T039 [P] [US3] Create `src/DigiByte.Web/Components/DigiDollar/DigiDollarHealthBanner.razor` with variants `unavailable` (FR-003), `oracleStale`, `err`, `volatility` (FR-019), `nodeLag` (FR-020), and `priceUnavailable` (FR-019); non-blocking, `role="status"` (`role="alert"` for `unavailable`).
- [X] T040 [US3] Show the oracle USD price with a "last updated" indicator (or `dd.price.unavailable` "Price unavailable") and the banners on every DD page, edit `src/DigiByte.Web/Pages/DigiDollar.razor`, `DigiDollarReceive.razor`, and `DigiDollarSend.razor` (FR-018). Verify banners are announced (`role="status"`, `role="alert"` for `unavailable`) and the price and freshness text has an accessible name (FR-024).
- [X] T041 [US3] Show the USD equivalent of the DGB balance on Home, computed from the oracle price and hidden when no price is available, edit `src/DigiByte.Web/Pages/Home.razor` and `src/DigiByte.Web/Components/DigiDollar/DigiDollarHomeCard.razor` (FR-021).
- [X] T042 [P] [US3] Add the banner and price strings (`dd.banner.*`, `dd.price.unavailable`, freshness labels) to `src/DigiByte.Web/wwwroot/locales/en.json` and `es.json`.

**Checkpoint**: All three stories are independently functional.

---

## Phase 6: Polish and Cross-Cutting

- [X] T043 Add a first-party, cookieless aggregate counter to the NodeApi in `src/DigiByte.NodeApi/Endpoints/MetricsEndpoints.cs` (new) with tests in `tests/DigiByte.NodeApi.Tests/MetricsEndpointsTests.cs`: `POST /api/metrics/event` accepts only an allowlisted event name (`dd_section_view`, `home_view`, `dd_send_attempted`) with an empty body, increments a per-day per-event count (SQLite or in-memory), and stores and logs nothing else: no IP address, user agent, cookie, or any identifier; `400` for any non-allowlisted name or any request body; `GET /api/metrics/summary` returns the daily counts for SC-005 and requires `X-NodeApi-Key` (operator only, fails closed when no key is configured). The POST route is rate-limited with the framework rate limiter keyed per client IP in memory only (the IP is never stored or logged; for example 60 events per minute per client, then `429` with `Retry-After`), and CORS allows only the wallet origin. Tests: a non-allowlisted name returns `400`; a request body returns `400`; the 61st request in a minute returns `429`; an unkeyed `GET /summary` returns `401`; a foreign origin is rejected by CORS. Failing tests first: assert no identifying data is persisted or logged (FR-023, SC-005, research R17).
- [X] T044 [P] Wire the aggregate counters from the wallet to T043: add `src/DigiByte.Web/Services/UsageCounter.cs` (new) that fire-and-forgets `POST /api/metrics/event` with an empty body for `home_view` (fired once per Home page load in `src/DigiByte.Web/Pages/Home.razor`), `dd_section_view` (fired once per load of `/digidollar` in `src/DigiByte.Web/Pages/DigiDollar.razor`), and `dd_send_attempted` (fired when a send is confirmed in `src/DigiByte.Web/Pages/DigiDollarSend.razor`), never blocks the UI, and is disabled when the NodeApi is unreachable. Do NOT use the existing Google Analytics bootstrap (`wwwroot/js/analytics.js`) for DigiDollar events, because it sets a per-device client ID (conflicts with SC-005 and FR-023). Verify no DD address, txid, or oracle price reaches any log or telemetry path (FR-023).
- [X] T045 [P] Set `SendEnabled=false` and `ViewEnabled=false` as the shipped defaults and document the ship-dark, beta, GA rollout switches (FR-022) in `specs/001-digidollar-wallet/quickstart.md`; add `appsettings.Development.json` overrides for local runs.
- [ ] T046 Run the full quickstart (`specs/001-digidollar-wallet/quickstart.md` steps 1-5, all 25 scenarios) on testnet26, including the interop check in step 2, a keyboard-only and screen-reader pass over all three DD pages and the confirm dialog (FR-024), and the security spot checks (no secrets in network requests, no `WriteKey` in `appsettings.json`, cross-origin write calls rejected).
- [X] T047 [P] Remove the now-unused custodial helpers and docs references: `src/DigiByte.Web/DEFI-SUMMARY.md` and `src/DigiByte.Web/ANNOUNCEMENT.md` wording that promises mint, redeem, or custodial send; confirm `/digidollar/mint`, `/redeem`, `/positions` return the not-found page (FR-028).
- [X] T048 Run `dotnet build` and the three DigiDollar test filters from quickstart step 1; confirm DGB balance, send, and history are unaffected when the NodeApi DD endpoints are stopped (SC-008).

---

## Dependencies and Execution Order

### Phase dependencies

- **Phase 1 (Setup)**: T001-T003 are verification and can start immediately; T004-T006 are independent edits. T003 blocks Phase 4 only; T001 decides the shape of T015.
- **Phase 2 (Foundational)**: starts after T002 (vectors) and T004 (build green). Blocks all user stories.
- **Phase 3 (US1)**, **Phase 4 (US2)**, **Phase 5 (US3)**: each depends only on Phase 2. In priority order they ship P1, P2, P3; US2 and US3 can run in parallel with each other once Phase 2 is done.
- **Phase 6 (Polish)**: after the stories you intend to ship.

### Within a story

- Tests first and failing, then models, then services, then UI.
- T029, T031, and T032 all depend on T003 (script serialization, tree shape, minimum fee) as well as T002; T032 also depends on T031. T033 depends on T032. T035 depends on T033 and T034.
- T025, T026, and T035 all edit the DD pages; T040 edits the same pages, so T040 runs after T025, T026, and T035.

### Parallel opportunities

- Phase 1: T004 and T006 together; T005 after T006 (it adds tests to the file T006 creates).
- Phase 2: T007 with T009 and T011; T014 with T018; Crypto, Wallet, and NodeApi tracks are independent until T021; T013 needs the health client (T018) for its inputs, so it runs after T018.
- US1: T019 with T020; T023, T024, T027 together.
- US2: T029 with T030; T031 and T034 and T036 together.
- US3: T037 with T039 and T042.

### Parallel example: Phase 2

```text
Track A (Crypto):   T007 → T008 ; T009 → T010
Track B (Wallet):   T011 → T012 ; T013 (after T018)
Track C (NodeApi):  T014 → T015 → T016 ; T017 → T018 (needs T011)
```

---

## Implementation Strategy

### MVP first (User Story 1 only)

1. Phase 1 and Phase 2 (verification, removal of the custodial code, primitives, read surface).
2. Phase 3 (US1). Stop and validate on testnet26: hold, receive, and restore.
3. Ship dark behind `ViewEnabled`, then beta, then GA (FR-022).

### Incremental delivery

1. Foundation, then US1 (hold and receive) as the first release.
2. Add US2 (send) behind `SendEnabled`, enabled only after the manual testnet26 run (quickstart) and the closed R1 items (T003).
3. Add US3 (health and USD context), which can also precede US2 since it does not depend on send.

### Notes

- The current uncommitted DigiDollar code in the working tree is the custodial version. T004 and T005 remove or restrict it, and DigiPay's use of the wallet-scoped NodeApi endpoints continues behind `X-NodeApi-Key` (out of scope for this feature, see plan.md Constitution Check).
- Locales: only `en.json` and `es.json` exist in the repo today. The plan's ten-locale list falls back to English until translations are added (FR-025).
- Commit after each task or logical group, and do not push without an explicit request.
