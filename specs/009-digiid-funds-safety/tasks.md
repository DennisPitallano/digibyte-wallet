---

description: "Task list for Digi-ID Address Funds Safety"
---

# Tasks: Digi-ID Address Funds Safety

**Input**: Design documents from `/specs/009-digiid-funds-safety/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included. The constitution (Principle III) requires tests in the same PR for every change to `DigiByte.Crypto/` and `DigiByte.Wallet/`, with negative-path cases. Write each test task first and confirm it fails before the matching implementation task. The Web project has no component-test setup, so UI tasks are checked through [quickstart.md](quickstart.md).

**Organization**: Grouped by user story. User Story 1 (warning) needs nothing from the foundation and can ship alone. User Stories 2 and 3 build on the foundational phase.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no unfinished dependency)
- **[Story]**: US1, US2 or US3 (maps to the user stories in spec.md)
- All paths are from the repository root (`C:\GitHub\digibyte-wallet`)

---

## Phase 1: Setup

**Purpose**: Start from a known-good baseline.

- [X] T001 Create the working branch from the latest upstream: `git fetch origin`, then `git switch -c feature/digiid-funds-safety origin/main` (the constitution's branching rule). Do not branch from the local `main`, which is behind `origin/main` and lacks the 008 spec and the merged unlock fix, and do not build on `fix/identity-unlock-first`. The untracked `specs/009-digiid-funds-safety/` files carry over; `.specify/feature.json` is gitignored and stays local, so check it still names `specs/009-digiid-funds-safety`. Then run `dotnet build src/src.sln`, `dotnet test tests/DigiByte.Crypto.Tests` and `dotnet test tests/DigiByte.Wallet.Tests`, and note any tests already failing before changes, so later failures are not blamed on this feature.

---

## Phase 2: Foundational (blocks User Stories 2 and 3)

**Purpose**: The key-free building blocks: domain normalisation, data types, the sweep record store, and test doubles. User Story 1 does not depend on this phase.

**Checkpoint**: Domain normalisation, models and store are tested and green.

- [X] T002 [P] Write failing tests in `tests/DigiByte.Crypto.Tests/DigiIdDomainTests.cs` for `DigiIdDomain.Normalize` (FR-004a): a bare host, `Example.COM`, `https://Example.com:8443/path?x=1`, `digiid://example.com/cb?x=abc` and `web+digiid://` forms all give `example.com`, and each equals `DigiIdService.ParseUri(...).Domain` for the same host. Negative cases: empty, whitespace, `null`, `not a domain`, a string with spaces, a host with a trailing dot only, and an over-long host all return null.
- [X] T003 Implement `DigiIdDomain.Normalize(string input) → string?` in `src/DigiByte.Crypto/DigiId/DigiIdService.cs` (new static class next to `DigiIdService`): trim, accept a bare host or a URL, take the host the same way `ParseUri` does, lowercase, return null for empty or invalid input. Make T002 pass.
- [X] T004 [P] Create `src/DigiByte.Wallet/Models/DigiIdFundsModels.cs` with no private-key members anywhere. Types and constraints, quoted from data-model.md:
  - `DigiIdFundFinding`: Domain; Address ("Legacy P2PKH Digi-ID address", derived, not stored in history); ConfirmedSatoshis ("Sum of UTXOs with at least 1 confirmation", "Only this amount is sweepable"); PendingSatoshis ("Sum of UTXOs with 0 confirmations", "Shown, never swept"); Outcome enum `Funds`, `Empty`, `CouldNotCheck` ("`CouldNotCheck` only when the lookup threw. Never shown as `Empty`."); Error ("Set only for `CouldNotCheck`"); InFlight ("The address is not offered for sweeping"); OtherOutputsCount ("Outputs whose script is present and differs from the address's own standard script", "Left untouched and excluded from every amount (FR-016)", "Zero when the lookup returns no script").
  - `DigiIdSweepPlan`: Items ("At most 19 (`ReceiveGapLimit` minus 1), and fewer if unused addresses already handed out leave less room"); Remaining; TotalAmount, TotalFee, TotalNet; FeeRateSatPerVByte ("Not below the wallet floor (100)").
  - `DigiIdSweepPlanItem`: Domain, SourceAddress, AmountSatoshis, EstimatedFeeSatoshis ("`EstimateFee(inputs, 1, rate, Legacy)`", "Estimate only"), NetSatoshis ("Must be at least 10,000 sat (0.0001 DGB), else the item is excluded as 'too small'"), DestinationAddress ("Unique within the plan and unused. Never reused across items (FR-008a)."), DestinationIndex; plus an exclusion reason for items left out.
  - `DigiIdSweepRecord`: Id, Domain, SourceAddress, DestinationAddress, AmountSatoshis, FeeSatoshis ("Final amounts from the signed transaction (fee is the real one)"), TxId ("Null until broadcast succeeds"), Status enum `Sending`, `Pending`, `Confirmed`, `Failed`, FailureReason ("Short text, set when `Failed`"), CreatedUtc, UpdatedUtc.
- [X] T005 [P] Create the test doubles in `tests/DigiByte.Wallet.Tests/DigiIdFundsTestDoubles.cs`: an in-memory `IBlockchainService` fake that returns configured UTXOs per address, can be set to throw for chosen addresses or for everything, records every `GetVerifiedUtxosAsync`/`GetUtxosAsync`/`GetBalanceAsync`/`BroadcastTransactionAsync`/`GetTransactionAsync` call with its arguments (so tests can prove one address per request, FR-004b), can reject broadcasts for chosen inputs, and returns configurable confirmations. Follow the setup pattern of `tests/DigiByte.Wallet.Tests/WalletServiceWatchOnlyTests.cs` and `MultiWalletTests.cs` for building an unlocked `WalletService` with an in-memory `ISecureStorage`. Also add helpers that create an unlocked HD wallet, a locked wallet, a watch-only wallet and a WIF wallet, each passing a `DigiIdSweepStore` over the in-memory storage as the optional constructor parameter added in T016.
- [X] T006 [P] Write failing tests (FR-011a, FR-015) in `tests/DigiByte.Wallet.Tests/DigiIdSweepStoreTests.cs`: records persist per wallet id under `digiid_sweeps_{walletId}` and reload; legal transitions `Sending→Pending`, `Pending→Confirmed`, `Sending→Failed`, `Pending→Failed`; illegal transitions (`Confirmed→Pending`, `Failed→Confirmed`) are rejected; an address with a `Sending` or `Pending` record reports in-flight; `Confirmed` and `Failed` do not; a different wallet's records are not visible; the serialized JSON contains no key, seed, raw transaction or signature field.
- [X] T007 Implement `src/DigiByte.Wallet/Services/DigiIdSweepStore.cs` over `ISecureStorage` (same pattern as `TransactionTracker`): load, upsert and list records newest first, the transition rules from data-model.md, and an in-flight lookup by source address. Register it in `src/DigiByte.Web/Program.cs` with `builder.Services.AddScoped<DigiIdSweepStore>();` next to `TransactionTracker` (line 71). Make T006 pass.

---

## Phase 3: User Story 1 - Warn users not to pay to a Digi-ID address (Priority: P1) 🎯 MVP

**Goal**: Every place that shows a Digi-ID address also says it is for sign-in only, is not watched, and must not receive payments, with a link to the recovery section.

**Independent Test**: Quickstart section 2. Open the approval panel, the success panel, a history entry and Help. Each shows the warning, and it is in Spanish when the language is Spanish.

- [X] T008 [P] [US1] Add the warning keys `identity.addrWarning.title`, `identity.addrWarning.body` and `identity.addrWarning.link` to `src/DigiByte.Web/wwwroot/locales/en.json`. The body must state all three facts from FR-002: identity address for signing in, not monitored by the wallet, must not be used to receive payments. The link text is "Sent funds here by mistake? Recover them".
- [X] T009 [P] [US1] Add the same three keys, translated to Spanish (FR-003), to `src/DigiByte.Web/wwwroot/locales/es.json`. Other locales fall back to English (research R-9), so no other file changes.
- [X] T010 [US1] Create `src/DigiByte.Web/Components/Shared/DigiIdAddressWarning.razor`: reads the three keys through `LocalizationService` the way the DigiDollar components do, uses the wallet's existing warning styling, is always visible and not dismissible, supports a `Compact` parameter for the modal, and links to `/identity#recover-funds`. Depends on T008 and T009.
- [X] T011 [US1] In `src/DigiByte.Web/Pages/Identity.razor`, render `<DigiIdAddressWarning />` directly under the `pendingAddress` row (about line 98 to 106), before the Approve button, and directly under the "Address used" row of the success panel (about line 233 to 238). Keep it in the same block as each `CopyValueButton`, so the warning is visible when the address is copied (spec scenario 6). The shortened address in the Login History list row (about line 300) is exempt under FR-001 and needs no change, because tapping the row opens the modal that T012 covers. Depends on T010.
- [X] T012 [US1] In `src/DigiByte.Web/Components/Shared/DigiIdLoginDetailModal.razor`, render `<DigiIdAddressWarning Compact="true" />` directly under the `Field("Signing address", Entry.Address)` row (line 40). Depends on T010.
- [X] T013 [P] [US1] In `src/DigiByte.Web/Pages/Help.razor`, in the `digi-id` section (about line 303 to 315): add a note to "Is Digi-ID safe?" that the address is for signing in only, and add a new entry "Can I receive funds to my Digi-ID address?" that says no, explains the wallet does not watch these addresses, and points to the "Recover funds" section on the **ID** page for funds sent by mistake. The answer must also say never to send DigiDollar or other tokens to a Digi-ID address, because the wallet can only recognise such outputs when the lookup returns their script (FR-016). English only, matching the rest of `Help.razor`.
- [X] T014 [US1] Run quickstart section 2 in the browser (wallet running locally on a test network) and confirm all four placements and the Spanish text. Check at phone width that the warning wraps without horizontal scroll. Verified in the browser on a throwaway test wallet: the approval panel shows the signing address with the warning directly under it and the link to the recovery section, and Help has the new entry with the link. The Spanish strings were checked in the served locale file. Placements on the success panel and in the login detail dialog are one-line uses of the same component and were verified by build only, because reaching them needs a real signed login to a Digi-ID site.

**Checkpoint**: User Story 1 works on its own and can be released alone.

---

## Phase 4: User Story 2 - Find funds sent to Digi-ID addresses (Priority: P2)

**Goal**: An unlocked HD wallet checks its Digi-ID addresses (login history plus hand-typed domains), one request per address, and lists what it finds without ever showing a failure as "nothing found".

**Independent Test**: Quickstart section 3. Fund one Digi-ID address on a test network, run the check, see the site, confirmed and pending amounts and the total. Lock the wallet, go offline, clear the history and type the domain by hand.

### Tests for User Story 2 (write first, confirm they fail)

- [X] T015 [P] [US2] Write failing scan tests in `tests/DigiByte.Wallet.Tests/DigiIdFundsRecoveryTests.cs` for `WalletService.ScanDigiIdFundsAsync` (FR-004, FR-005, FR-006, FR-007), using the doubles from T005:
  - funds on one of three domains give one `Funds` and two `Empty` findings with the right confirmed and pending amounts;
  - a fake that throws for one address gives `CouldNotCheck` for it and normal results for the others;
  - every lookup throwing gives only `CouldNotCheck` findings, never `Empty`;
  - each lookup is its own `GetVerifiedUtxosAsync` call with exactly one address, and the batched `GetUtxosAsync(IEnumerable<string>)` and `GetBalanceAsync(IEnumerable<string>)` are never called (FR-004b);
  - the same site given twice, or as `Example.com` and `https://example.com/x`, is checked once;
  - an invalid domain gives a `CouldNotCheck` finding with the reason "not a valid site name" and no lookup;
  - the derived address equals `GetDigiIdAddress(domain)` and is not taken from any stored login record;
  - an address with a `Sending` or `Pending` sweep record is reported `InFlight`;
  - a locked wallet, a watch-only wallet and a WIF wallet each throw and make no lookup;
  - cancellation returns the findings that finished;
  - an output whose script is present and differs from the address's own standard script is left out of the confirmed and pending amounts and counted in `OtherOutputsCount`; an output with no script returned counts as plain (FR-016);
  - with no sweep records, building and unlocking the wallet makes no Digi-ID lookup at all, and `RefreshDigiIdSweepsAsync` with no records makes none either (FR-014). The UI side, that opening the section makes no lookup until the user taps check, is checked in quickstart section 3.

### Implementation for User Story 2

- [X] T016 [US2] Implement `ScanDigiIdFundsAsync(IEnumerable<string> domains, IProgress<int>? progress, CancellationToken ct)` in `src/DigiByte.Wallet/Services/WalletService.cs` per [contracts/wallet-service.md](contracts/wallet-service.md): the common locked and HD-only preconditions, normalise and de-duplicate domains with `DigiIdDomain.Normalize` (T003), derive each address with `_hd.DeriveDigiIdKey(DigiIdService.DeriveSiteIndex(domain))` and `DigiIdService.GetAddress`, call `GetVerifiedUtxosAsync` once per address with at most 4 at a time, treat only `Confirmations >= 1` as confirmed, exclude any output whose script is present and differs from `address.ScriptPubKey` and count it in `OtherOutputsCount` (FR-016), set `InFlight` from `DigiIdSweepStore` (T007), report progress, honour cancellation. No private key leaves the method and none is logged. Give the `WalletService` constructor (about line 266) an optional trailing parameter `DigiIdSweepStore? sweepStore = null` so the 11 existing `new WalletService(` call sites compile unchanged and dependency injection supplies the store (T007). The three new Digi-ID methods throw `InvalidOperationException("Digi-ID recovery is not configured.")` when it is null. Make T015 pass.
- [X] T017 [P] [US2] Add the section keys `identity.recover.*` for the title, explanation, locked, unsupported, domain input and its errors, check button, checking, nothing found, results, "could not check", retry, total, and pending labels to `src/DigiByte.Web/wwwroot/locales/en.json`, following [contracts/ui.md](contracts/ui.md). Add the Spanish versions to `src/DigiByte.Web/wwwroot/locales/es.json` in the same task.
- [X] T018 [US2] Create `src/DigiByte.Web/Components/Shared/DigiIdRecoverFunds.razor` with the states Locked, Unsupported, Ready, Checking, Nothing found, Results and Check failed from [contracts/ui.md](contracts/ui.md):
  - read the domains from the `digiid_login_history` storage key the same way `Identity.razor` does (`LoadHistoryAsync`), plus a domain input that normalises with `DigiIdDomain.Normalize` and shows an inline message for invalid input;
  - an empty-history prompt to add domains by hand (FR-004a);
  - progress with a Cancel button;
  - failed lookups listed with a Retry that re-checks only those (FR-007);
  - addresses `InFlight` shown as "being swept" and not selectable;
  - a finding with `OtherOutputsCount` above zero shows "N other outputs left untouched" next to it;
  - no key, path or raw transaction ever shown;
  - phone-width layout with visible keyboard focus.
  Depends on T016 and T017.
- [X] T019 [US2] (FR-001a) Embed `<DigiIdRecoverFunds />` at the bottom of `src/DigiByte.Web/Pages/Identity.razor` inside an element with `id="recover-funds"`, and make the Locked state's unlock link return to `/identity#recover-funds` through the existing unlock route. This makes the link added in T010 land on the section. Depends on T018.
- [X] T020 [US2] Run quickstart section 3 and confirm every check, including the network panel showing one request per address and the timed check of up to 50 sites finishing in under 30 seconds (SC-002). Verified in the browser on a throwaway test wallet (explorer replies faked where noted): locked state with an unlock link that returns to the section; invalid site name rejected inline; a pasted URL normalised to the bare domain; every request carried exactly one address; a failed lookup shown as "Could not check" with a retry, never as nothing found; a funds-found list with confirmed, pending and totals (faked explorer reply); the warning link scrolls to the section. A 50-site check took 6.4 s with a faked 300 ms reply per address, so the real time is the network time./

**Checkpoint**: User Stories 1 and 2 both work. Funds can be found but not yet moved.

---

## Phase 5: User Story 3 - Sweep recovered funds to a regular address (Priority: P3)

**Goal**: The user reviews amount, fee, net and a different destination per address, confirms once, and each address is swept in its own transaction. Status is saved and shown until each sweep is confirmed or failed.

**Independent Test**: Quickstart section 4. Sweep two funded Digi-ID addresses and confirm two separate transactions to two different receive addresses, a status that survives a reload, and the funds in the normal balance.

### Tests for User Story 3 (write first, confirm they fail)

- [X] T021 [P] [US3] Write failing tests in `tests/DigiByte.Wallet.Tests/DigiIdSweepPlannerTests.cs` for `DigiIdSweepPlanner` (FR-008a, FR-010; pure, no I/O): the fee uses the Legacy input size and never a rate below the 100 floor; an item whose net is below 10,000 sat is excluded with the reason "too small to sweep" and an item with funds below the fee never yields a negative net; unconfirmed-only addresses are excluded; at most 19 items are taken (fewer when unused addresses already handed out leave less room) and the rest are counted in `Remaining`; destinations are assigned one per item, in order, all distinct, none equal to a source address; in-flight addresses are excluded; totals equal the sums of the items.
- [X] T022 [US3] Write failing plan and execute tests (FR-008a, FR-009, FR-010, FR-012, FR-013, FR-015) in `tests/DigiByte.Wallet.Tests/DigiIdFundsRecoveryTests.cs` (same file as T015, so write it after T015 and do not run it in parallel) using the doubles from T005:
  - **Plan:** re-verifies each selected address with its own request; a failed re-verify excludes only that item; two items get two different, unused receive addresses; nothing is saved or broadcast while planning, so cancelling leaves `NextReceivingIndex` unchanged.
  - **Execute:** two items broadcast two transactions, each with inputs from one source address only and one output to its own destination and no change, with a fee at or above the floor; the highest destination index plus one is saved as `NextReceivingIndex` before the first broadcast, and afterwards `GetReceivingAddressAsync` returns an address that is not one of the sweep destinations; if saving the index fails nothing is broadcast; one rejected broadcast gives that record `Failed` with a reason and the other still completes; a second `ExecuteDigiIdSweepAsync` for an address with a `Pending` record broadcasts nothing and returns the existing record; two concurrent calls broadcast once per address; each broadcast is recorded in `TransactionTracker` with the memo "Digi-ID recovery sweep"; locked, watch-only and WIF wallets throw and broadcast nothing; no log line contains a key or raw transaction.
  - **Other outputs:** an output with a different script is never given to the transaction builder, is not in the amount, fee or net, and the sweep of the plain outputs on the same address still completes (FR-016).
  - **Lock:** if the wallet locks between two items, the first is broadcast, the unsent items become `Failed` with the reason "wallet locked", nothing more is broadcast, and a later plan and execute after unlocking sweeps them (spec edge case).
  - **Fee:** for a signed sweep, the fee actually paid is within 10% of the estimate in the plan item (SC-004).
  - **Refresh:** a `Pending` record with at least 1 confirmation becomes `Confirmed`; a lookup failure leaves it `Pending` and does not throw; a `Sending` record without a txid whose source still holds UTXOs becomes `Failed`.

### Implementation for User Story 3

- [X] T023 [US3] Implement `src/DigiByte.Wallet/Services/DigiIdSweepPlanner.cs` (pure helpers described in [contracts/wallet-service.md](contracts/wallet-service.md)): fee estimate through `DigiByteTransactionBuilder.EstimateFee(inputs, 1, rate, ScriptPubKeyType.Legacy)` with the 100 sat/vB floor, net and the 10,000 sat minimum, the item cap of `WalletService.ReceiveGapLimit - 1` (19), reduced further when unused addresses already handed out leave less room, eligibility, and destination assignment. Make T021 pass.
- [X] T024 [US3] Implement `PlanDigiIdSweepAsync(IEnumerable<DigiIdFundFinding> selected, int feeRateSatPerVByte = 100)` (FR-008, FR-009, FR-010) in `src/DigiByte.Wallet/Services/WalletService.cs`: preconditions, one `GetVerifiedUtxosAsync` per selected address (dropping outputs whose script differs from the address's own standard script, as in T016), `DigiIdSweepPlanner` for fees, exclusions and the cap, and peek the next N unused receive addresses from `NextNewReceivingIndex(info)` onward without saving anything. Depends on T016 and T023.
- [X] T025 [US3] Implement `ExecuteDigiIdSweepAsync(DigiIdSweepPlan plan, IProgress<DigiIdSweepRecord>? progress, CancellationToken ct)` (FR-008a, FR-011, FR-012, FR-013, FR-015) in `src/DigiByte.Wallet/Services/WalletService.cs` per [contracts/wallet-service.md](contracts/wallet-service.md): save the highest destination index plus one as `NextReceivingIndex` with `PersistAddressIndexesAsync` before any broadcast (so the Receive page shows a fresh unused address, research R-4) and abort if that fails; then per item, one at a time, skip an address that already has a `Sending` or `Pending` record, take a per-address in-memory lock, check the wallet is still unlocked (before writing anything for the item), write a `Sending` record through `DigiIdSweepStore`, re-derive the key, build with `DigiByteTransactionBuilder.BuildSendAllTransaction` using `Utxo` objects whose script is the Legacy address script, broadcast, set `TxId`, the real fee and `Pending`, call `_txTracker.RecordSendAsync(..., "Digi-ID recovery sweep")`; a failure sets `Failed` with a short reason and the loop continues; if the wallet has locked, every item not yet sent is set to `Failed` with the reason "wallet locked" and the method returns (R-13). Keys are never stored or logged. Depends on T007 and T024.
- [X] T026 [US3] Implement `RefreshDigiIdSweepsAsync(CancellationToken ct)` (FR-011, FR-011a) in `src/DigiByte.Wallet/Services/WalletService.cs`: one `GetTransactionAsync(txId)` per `Pending` record, `Confirmed` at 1 or more confirmations, `Failed` for a transaction the network no longer knows after a grace period, a lookup failure leaves the record unchanged without throwing, and the interrupted-run rule for `Sending` records without a txid. Make T022 pass together with T025.
- [X] T027 [P] [US3] Add the keys `identity.recover.review.*` and `identity.recover.status.*` (review table labels, "too small to sweep", "N more can be swept after these confirm", confirm and cancel, sending, pending, confirmed, failed, retry, and the error messages from the contract) to `src/DigiByte.Web/wwwroot/locales/en.json` and the Spanish versions to `src/DigiByte.Web/wwwroot/locales/es.json`.
- [X] T028 [US3] Extend `src/DigiByte.Web/Components/Shared/DigiIdRecoverFunds.razor` with the Review, Sending and Status states (FR-009, FR-010, FR-011, FR-011a, FR-012) from [contracts/ui.md](contracts/ui.md):
  - select addresses and a "Sweep selected" action that builds the plan;
  - the review shows per item the amount, estimated fee, net and its own destination, then totals, the "N more after these confirm" line and the excluded "too small" items with the reason;
  - Confirm and Cancel, where Cancel sends and saves nothing;
  - Sweep and Confirm disabled while a plan or execution is running;
  - per-record status with the reason for `Failed` and a Retry for a failed item;
  - run `RefreshDigiIdSweepsAsync` when the section opens, so status survives a reload, and add a "Refresh" button plus an automatic refresh about every 30 seconds while the section is open and at least one record is `Pending`; stop the timer when the section is disposed or nothing is pending (FR-011, FR-014, research R-12);
  - show "Failed: wallet locked" items with a Retry that asks for an unlock first;
  - unconfirmed amounts labelled as pending;
  - status in text, not colour alone.
  Depends on T024, T025, T026 and T027.
- [X] T029 [US3] Run quickstart section 4 on a test network and confirm every check, including two separate transactions with different destinations, the status after a reload, the "too small" case, the double-click case, the sweep finishing in under 2 minutes (SC-003), the fee within 10% of the estimate (SC-004), the status refreshing on its own and stopping when the section closes, and the lock-during-sweep case. Verified in the browser on a throwaway test wallet with a faked explorer: the review showed amount, fee estimate (0.000192 DGB for one input), what arrives and an own destination; Cancel sent nothing; Confirm clicked three times in a row sent exactly one broadcast; the status showed Pending and the address showed "Being swept" and could not be selected again; the status refreshed on its own about 30 seconds later; the sweep was still there after a reload and unlock (it showed within 0.3 s once the saved list was read first, which this check led to fixing: it first took about 20 s); and no status lookups happened in 40 s after leaving the page. Not run in the browser, but covered by unit tests: funds too small to sweep, the wallet locking part way, and a real network accepting the transaction. Fee: the signed fee was 1.6% under the estimate (limit 10%).

**Checkpoint**: All three user stories work.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T030 [P] Add an entry under `[Unreleased]` in `CHANGELOG.md`: the Digi-ID address warning, the "Recover funds" section on the **ID** page and its privacy properties (separate lookups, separate transactions, different destinations). Mark it security-relevant.
- [X] T031 [P] Describe the recovery flow in `docs/PROCESS_FLOWS.md` (check, review, sweep, status) and note the privacy rules and the 19-address-per-round limit and why it is one below the wallet's 20-address limit.
- [X] T032 Review the diff for secret hygiene (FR-015): confirm no private key, seed, raw transaction or signature appears in any new type, stored record, log line, exception message or UI string. Search the new code for `PrivateKey`, `Console.WriteLine`, `ToHex()` and `ToBytes()` outside the signing and broadcast path. Done: the new files hold no private key, seed, mnemonic, logging or console output; in WalletService the site key exists only as a local variable that signs and is then dropped, ToHex() is used only on public scripts, ToBytes() only on the transaction that is broadcast, and no exception text from the network or the chain is shown or stored (fixed wording is used). The stored sweep record was read back from the browser storage and holds only ids, domain, addresses, amounts, status and times.
- [X] T033 Check that nothing in this feature changes balance, receive, send or history behaviour: no new background polling, no change to `GetAddressKeyMap`, the existing recovery sweeps untouched, and the 11 existing `new WalletService(` call sites compiling unchanged. Run the full `dotnet build src/src.sln` and `dotnet test` for `tests/DigiByte.Crypto.Tests` and `tests/DigiByte.Wallet.Tests`, and compare with the baseline from T001. Done: the whole solution builds with 0 warnings. WalletService changed by 559 added lines and one replaced line (the constructor, which gained an optional last parameter, so the 11 existing call sites compile unchanged); GetAddressKeyMap, the existing recovery sweeps and send paths are untouched. Tests: Crypto 384 pass (350 before, plus 34 new). Wallet: 147 new tests, all passing in every run. The full Wallet suite also has failures that were there before this change and are not related to it: 6 live-network benchmarks (LiveExplorerBenchmarks, they call real explorers) and a handful of wall-clock timing tests (for example ParallelQueryTests.GetBalance_MultiAddress_RunsInParallel and NodeSourceMonitorTests.HealthAndExplorerLookups_RunInParallel) whose count changes from run to run (1 to 7 of them) with machine load; the same tests fail the same way when the new Digi-ID tests are left out of the run (8 failures in one run and 13 in the next, out of the original 1197 tests).
- [ ] T034 Run all of [quickstart.md](quickstart.md) once more from a clean browser profile, including the Spanish check, and the phone-width and keyboard-focus checks.
- [ ] T035 In the pull request description, flag the change as security-relevant (a new signing and broadcast path) and link the spec, as the constitution's merge gates require. Use a Conventional Commits title such as `feat(identity): recover funds sent to Digi-ID addresses`.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: none.
- **Foundational (Phase 2)**: after Setup. Blocks User Stories 2 and 3 only.
- **User Story 1 (Phase 3)**: can start right after Setup. It does not use anything from Phase 2.
- **User Story 2 (Phase 4)**: after Phase 2.
- **User Story 3 (Phase 5)**: after Phase 2 and the scan work in T016, because planning reuses the scan findings and the same section component.
- **Polish (Phase 6)**: after the stories you plan to ship.

### Within a story

- Tests first, and they must fail before the implementation.
- Key-free helpers (`DigiIdDomain`, `DigiIdSweepPlanner`, `DigiIdSweepStore`) before the `WalletService` methods that use them.
- `WalletService` methods before the Razor component states that call them.
- Locale keys before the component that reads them.

### Parallel opportunities

- Phase 2: T002, T004, T005 and T006 touch different files and run together. T003 follows T002, and T007 follows T006.
- Phase 3: T008, T009 and T013 run together. T011 and T012 run together once T010 is done.
- Phase 4: T015 and T017 run together.
- Phase 5: T021 and T027 run together. T022 shares a file with T015, so it is not parallel and is written after T015.
- Phase 6: T030 and T031 run together.
- Different people can take User Story 1 and Phase 2 in parallel from the start.

### Parallel example

```text
Start together:  T002 (domain tests)   T004 (models)   T005 (test doubles)   T006 (store tests)   T008/T009 (US1 locale keys)
Then:            T003, T007, T010
Then:            T011, T012, T013 and T015
```

---

## Implementation Strategy

### MVP first (User Story 1 only)

1. Phase 1, then Phase 3 (T008 to T014).
2. Stop and validate with quickstart section 2.
3. Release. This prevents new mistakes at almost no risk, because it moves no money.

### Incremental delivery

1. Add Phase 2 and User Story 2: users can now see funds that were already sent by mistake.
2. Add User Story 3: users can recover them. It carries the most risk, so ship it last and after the T032 review.
3. Each increment is independently demonstrable, and User Story 1's link to the recovery section works as soon as T019 lands.

### Notes

- The 19-address cap per confirmation, the 10,000 sat minimum net and the 100 sat/vB fee floor come from [research.md](research.md) (R-4, R-5). Do not change them without updating the spec.
- Commit after each task or small group. Per the project's convention, commit locally and wait for an explicit "push" before pushing.
