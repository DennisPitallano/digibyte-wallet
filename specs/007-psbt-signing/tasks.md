---

description: "Task list for PSBT signing in regular wallets"
---

# Tasks: Sign an Unsigned Transaction (PSBT)

**Input**: Design documents from `specs/007-psbt-signing/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/psbt-signing.md](contracts/psbt-signing.md), [quickstart.md](quickstart.md)

**Tests**: Required. Constitution Principle III makes tests mandatory for `DigiByte.Crypto` and `DigiByte.Wallet`, including negative paths. Write each test task first and confirm it fails before implementing.

**Organization**: Tasks are grouped by user story. User Stories 1 and 2 are both P1 and **must ship together**: signing without the safety checks puts funds at risk. Story 1 alone is a development checkpoint, not a releasable state. The first release is Phases 1 to 7, which are Setup, Foundational, Stories 1, 2, 3 and 5, and the release checks. Story 4 is Phase 8, after the first release.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete tasks)
- **[Story]**: The user story this task belongs to (US1 to US5)

## Rules that apply to every task

- Never call `PSBT.SignWithKeys` or `MultisigService.SignPSBT` in this feature (research R1).
- Never use `TransactionBuilder.Verify` to decide validity (research R3).
- Never log PSBT contents, PINs, or key material (FR-028).
- Never change multisig code (FR-029).
- Commit with Conventional Commits. Do not push until asked.

---

## Phase 1: Setup

**Purpose**: Branch and shared test fixtures

- [X] T001 Create branch `feature/psbt-signing` from `main`, and commit the `specs/007-psbt-signing/` documents with `docs:` as the first commit
- [X] T002 [P] Create PSBT test builders in tests/DigiByte.Crypto.Tests/PsbtTestFixtures.cs that fund and build unsigned PSBTs on `DigiByteNetwork.Mainnet` for P2WPKH, P2PKH with previous transaction, P2SH-P2WPKH, P2TR, 2-of-2 P2WSH, and mixed-owner inputs, and link that file into tests/DigiByte.Wallet.Tests/DigiByte.Wallet.Tests.csproj as a compile include so both test projects use the same builders
- [X] T003 [P] Create `FakePsbtBlockchain : IBlockchainService` in tests/DigiByte.Wallet.Tests/PsbtTestDoubles.cs, with settable transactions, unspent lists, and failure modes (throw, not found), following the fakes in tests/DigiByte.Wallet.Tests/BatchSendTests.cs

---

## Phase 2: Foundational

**Purpose**: Types, decoding, and key access that every story uses

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T004 Create the data model records and enums in src/DigiByte.Crypto/Psbt/PsbtModels.cs: `ChainInputFact`, `InputAssessment`, `OutputAssessment`, `ReviewSummary`, `SigningResult`, `BroadcastResult`, and `PsbtRefusalReason` with the 13 reasons in the order listed in specs/007-psbt-signing/data-model.md. Amounts are in satoshis
- [X] T005 [P] Write decoding tests in tests/DigiByte.Crypto.Tests/PsbtDocumentTests.cs: valid base64, surrounding whitespace, valid binary, hex refused, random text refused, base64 that is not a PSBT refused, input above 1 MB refused before processing, duplicate input refused, no inputs refused, no outputs refused
- [X] T006 Implement `PsbtDocument` in src/DigiByte.Crypto/Psbt/PsbtDocument.cs per research R11: "At most 1 MB", decode text with `Convert.FromBase64String` then `PSBT.Load`, catch `FormatException` and `InvalidOperationException`, and return `TooLarge`, `NotAPsbt`, or `InvalidStructure` rather than throwing
- [X] T007 Create `IPsbtKeyProvider` in src/DigiByte.Wallet/Services/IPsbtKeyProvider.cs with `CanSign`, `Network`, `GetOwnedScriptsAsync`, and `SignInputs` as defined in specs/007-psbt-signing/contracts/psbt-signing.md, and add `FakePsbtKeyProvider` to tests/DigiByte.Wallet.Tests/PsbtTestDoubles.cs
- [X] T008 [P] Write signer tests in tests/DigiByte.Crypto.Tests/PsbtSignerTests.cs: signs only listed inputs when both are owned, signs P2WPKH and P2PKH to a valid finalized transaction, signing twice leaves one identical signature, original PSBT is unchanged, `TryComplete` leaves the original not finalized, `TryComplete` rejects a tampered partial signature
- [X] T009 Implement `PsbtSigner` in src/DigiByte.Crypto/Psbt/PsbtSigner.cs with `SignInputs` using `PSBTInput.Sign` one input at a time on a clone, and `TryComplete` using `TryFinalize` and `ExtractTransaction` on a clone (research R1, R3)
- [X] T010 [P] Write key provider tests in tests/DigiByte.Wallet.Tests/WalletServicePsbtTests.cs: new HD wallet owned scripts cover receiving 0-19 and change 0-9 in native SegWit and legacy (60 scripts), a wallet with used addresses past that also owns their native SegWit scripts, a coin found by discovery is owned when scripts are read straight after unlock, slow discovery doesn't block the read, WIF wallet gives 2, watch-only gives scripts with `CanSign` false, locked gives `CanSign` false, `SignInputs` throws when `CanSign` is false, `SignInputs` skips an index whose script is not owned, a 2-of-2 multisig input containing the wallet's key is not signed, wrapped SegWit on the wallet's key is not signed
- [X] T011 Implement `IPsbtKeyProvider` on `WalletService` in src/DigiByte.Wallet/Services/WalletService.cs. `GetOwnedScriptsAsync` first calls `WaitForAddressDiscoveryAsync(AddressDiscoverySendWait)`, then uses the public-key address derivation in `GetAllAddresses`, so it works on watch-only wallets. `SignInputs` uses `GetAddressKeyMap` and the WIF branch logic of `SendAsync`, with private keys staying inside the class
- [X] T012 Write PIN tests in tests/DigiByte.Wallet.Tests/WalletServicePsbtTests.cs: correct PIN true, wrong PIN false, missing wallet false, and session and active wallet unchanged in every case
- [X] T013 Implement `WalletService.VerifyPinAsync(walletId, pin)` in src/DigiByte.Wallet/Services/WalletService.cs: decrypt the seed, clear the decrypted bytes, return the result, and do not call `UnlockWalletAsync` (research R7)
- [X] T014 [P] Write throttle tests in tests/DigiByte.Wallet.Tests/PinAttemptThrottleTests.cs: no delay for 2 failures, 15 seconds at 3, doubling each failure, capped at 5 doublings, success resets
- [X] T015 Implement `PinAttemptThrottle` in src/DigiByte.Web/Services/PinAttemptThrottle.cs, moving the rule out of `UnlockAsync` in src/DigiByte.Web/Pages/Unlock.razor and changing that page to use the class, with its messages and delays unchanged. Each page creates its own instance, so no service registration is needed. Compare the unlock behavior against `main` before and after the change
- [X] T016 [P] Write lookup tests in tests/DigiByte.Wallet.Tests/VerifiedUtxoTests.cs: `BlockchainApiService`, `BlockbookApiService` and `NodeApiBlockchainService` (strict and lenient) each return outputs on success, return an empty list for an address with no coins, and throw on a transport error, HTTP 429, HTTP 502 and an HTML reply. `FallbackBlockchainService` asks the next explorer after a failure, uses the node on regtest and when it is active, throws when no source answers, doesn't set a cooldown or the outage state, and doesn't cache
- [X] T017 Add `GetVerifiedUtxosAsync` as a default method in src/DigiByte.Wallet/Services/IBlockchainService.cs, with overrides in src/DigiByte.Wallet/Services/BlockchainApiService.cs, src/DigiByte.Wallet/Services/NodeApiBlockchainService.cs and src/DigiByte.Wallet/Services/FallbackBlockchainService.cs. Don't change `GetUtxosAsync`

**Checkpoint**: Decoding, signing, key access, and PIN checks pass their tests. The multisig tests are still green.

---

## Phase 3: User Story 1 - Review and sign a PSBT, then export it (Priority: P1) 🎯 MVP

**Goal**: Load a PSBT, see what it does, sign the wallet's inputs after PIN or biometrics, and export the result.

**Independent Test**: Load a PSBT spending one of the wallet's coins, review it, sign it, export it, and confirm with a separate tool that the input holds a valid signature.

### Tests for User Story 1 ⚠️

- [X] T018 [P] [US1] Write analyzer tests in tests/DigiByte.Crypto.Tests/PsbtAnalyzerTests.cs: wallet input classification by script only, PSBT key paths ignored for inputs and for change (FR-005), output labels for address, data with readable text, and non-standard, net amount exact to the satoshi including the wallet-gains case (SC-003), fee and `HighFee` above 10% of the amount sent to outputs that are not the wallet's, `HighFee` compared with total inputs when every output is the wallet's, lock time and unconfirmed warnings, `CanSign` and `CanBroadcastSigned` derivation, and a PSBT with another participant's input gives an `Unverified` fee, a required acknowledgment and `SharedOutputsNotChecked`, and the review counts inputs to sign and inputs belonging to others
- [X] T019 [P] [US1] Write service tests in tests/DigiByte.Wallet.Tests/PsbtSigningServiceTests.cs: `LoadAsync` from text and bytes gives the same review, `SignAsync` signs only `InputsToSign`, `SignAsync` refuses when a required acknowledgment is missing, the service holds no PSBT between calls, and balance and history are unchanged after signing without broadcast
- [X] T020 [P] [US1] Write confirmation flow tests in tests/DigiByte.Wallet.Tests/PsbtConfirmationFlowTests.cs: steps run in the order acknowledgment, spending limit, warnings, PIN or biometrics; the first failure stops later steps and reports its reason; a declined warning stops the flow; a spending limit check that throws does not block; a failed or cancelled PIN or biometric check gives no go-ahead; the already signed path follows the same order, and a PSBT over the spending limit is blocked (FR-034)

### Implementation for User Story 1

- [X] T021 [US1] Implement `PsbtAnalyzer.Analyze` in src/DigiByte.Crypto/Psbt/PsbtAnalyzer.cs for classification, output labels using the active network, totals, fee status, warnings, and `NeedsAcknowledgment`, as a pure function with no I/O. Until Story 4, the fee status is `Unverified` whenever any input is someone else's, and `SharedOutputsNotChecked` is set
- [X] T022 [US1] Implement `PsbtSigningService.LoadAsync` and `SignAsync` in src/DigiByte.Wallet/Services/PsbtSigningService.cs: `GetVerifiedUtxosAsync` to prove wallet inputs that have PSBT data, and `GetTransactionAsync` only for inputs with no PSBT data (using `ScriptHex`, or the script derived from `Address` when empty), then the analyzer and signer (research R4)
- [X] T023 [US1] Register `PsbtSigningService` and `IPsbtKeyProvider` as scoped services in src/DigiByte.Web/Program.cs, following the `IDdSessionProvider` registration
- [X] T024 [US1] Add a `downloadBinaryFile(base64, filename)` function beside `downloadTextFile` in src/DigiByte.Web/wwwroot/js/download.js
- [X] T025 [US1] Create src/DigiByte.Web/Pages/PsbtSign.razor at `/settings/psbt-sign` inside `AuthGuard`: paste box, file picker with `InputFile` limited to 1 MB as in src/DigiByte.Web/Pages/MultisigImport.razor, and the state flow in specs/007-psbt-signing/data-model.md, with the PSBT held only in the component
- [X] T026 [US1] Add the review to src/DigiByte.Web/Pages/PsbtSign.razor (FR-014 to FR-018): net amount as the primary figure, shown as received when negative, every output with full address and amount, marked when it returns to the wallet, fee with its status, the number of inputs to sign and the number belonging to others, the "outputs were not checked" note, and warnings with the required acknowledgment
- [X] T027 [US1] Implement `PsbtConfirmationFlow` in src/DigiByte.Web/Services/PsbtConfirmationFlow.cs, which takes each check as a delegate, runs them in the fixed order in specs/007-psbt-signing/contracts/psbt-signing.md, and stops at the first failure
- [X] T028 [US1] Add the sign steps to src/DigiByte.Web/Pages/PsbtSign.razor by running `PsbtConfirmationFlow` with: acknowledgment, `SpendingLimitService.CheckLimitAsync` with `NetAmount`, or zero when the wallet gains overall (FR-033), `SendRiskEvaluator.EvaluateBatch` with `BakuService.ConfirmSendRiskAsync` when all inputs are the wallet's (FR-031), and biometrics or PIN through `VerifyPinAsync` and `PinAttemptThrottle` (FR-019), then calling `SignAsync` only if the flow passes
- [X] T029 [US1] Add the result to src/DigiByte.Web/Pages/PsbtSign.razor: complete or incomplete status, remaining signature count (FR-022), copy as base64, and download through `downloadBinaryFile` as a `.psbt` file (FR-021)
- [X] T030 [US1] Add the Advanced mode entry near Recovery Tools in src/DigiByte.Web/Pages/Settings.razor, shown only when `AppState.IsSimpleMode` is false (FR-030), and show only the Advanced mode explanation in src/DigiByte.Web/Pages/PsbtSign.razor in Simple mode

**Checkpoint**: Story 1 works end to end on PSBTs that are honest. **Do not release yet**: Story 2 is required.

---

## Phase 4: User Story 2 - Refuse PSBTs that are unsafe or misleading (Priority: P1)

**Goal**: Every unsafe or misleading PSBT is refused with a specific reason before any signature exists.

**Independent Test**: Load each PSBT in the bad-case set in specs/007-psbt-signing/quickstart.md and confirm each gives its named refusal and no signature.

### Tests for User Story 2 ⚠️

- [X] T031 [P] [US2] Add refusal tests to tests/DigiByte.Crypto.Tests/PsbtAnalyzerTests.cs for the cases in specs/007-psbt-signing/quickstart.md: DigiDollar by version, DigiDollar by data output under a plain version, `NONE`, `SINGLE`, and `ANYONECANPAY` on a wallet input, `SINGLE` on another input, input with no data whose lookup failed refused as `InputNotIdentified` (FR-035), Taproot, legacy wallet input with no previous transaction, mismatched previous transaction, false amount and false script on a wallet input, spent input, outputs greater than inputs, nothing to sign, plus a test that the first applicable reason wins, in the order in specs/007-psbt-signing/data-model.md
- [X] T032 [P] [US2] Add service tests to tests/DigiByte.Wallet.Tests/PsbtSigningServiceTests.cs: coin spent between load and sign refused at signing (FR-008), chain sources all throwing gives `Unavailable` and no signature (FR-013), a real `BlockchainApiService` receiving HTTP 502 gives `Unavailable`, not `InputSpentOrUnknown`, coin only on the other network refused as `InputSpentOrUnknown`, wrong PIN path produces no signature, wallet input with PSBT data is accepted when the transaction lookup finds nothing but the unspent list has it, and `GetTransactionAsync` isn't called for it

### Implementation for User Story 2

- [X] T033 [US2] Add the refusal checks to src/DigiByte.Crypto/Psbt/PsbtAnalyzer.cs in the order in specs/007-psbt-signing/data-model.md, using `DigiDollarScripts.IsDigiDollarVersion`, `TryParseOpReturn`, and `TryParseMintOpReturn` (research R10), `SighashType` (research R2), and `IsScriptType(ScriptType.Taproot)`, and refuse a negative fee in the analyzer itself
- [X] T034 [US2] Make `SignAsync` in src/DigiByte.Wallet/Services/PsbtSigningService.cs repeat every chain check before signing, and treat a thrown chain call on this wallet's inputs as `Unavailable`, following the failure table in research R4
- [X] T035 [US2] Show refusals in src/DigiByte.Web/Pages/PsbtSign.razor with a plain message for each reason, naming the input where relevant, the "nothing to sign" message without a sign action (FR-012), and the unverified review with Sign unavailable and a retry action when chain data is unavailable (FR-013)
- [X] T036 [US2] Add the explanations in src/DigiByte.Web/Pages/PsbtSign.razor for watch-only wallets (FR-003) and the search range note, shown whenever any input is treated as someone else's (`OutOfRangeNote`), and discard the PSBT when the wallet locks during review

**Checkpoint**: Stories 1 and 2 work together. Continue with Story 3 and Story 5, which complete the first release's features.

---

## Phase 5: User Story 3 - Finalize and broadcast a complete PSBT (Priority: P2)

**Goal**: Send a fully signed transaction directly from the wallet.

**Independent Test**: Sign a PSBT whose inputs are all the wallet's, broadcast it on regtest, and confirm the node accepts it and it appears in history.

### Tests for User Story 3 ⚠️

- [X] T037 [P] [US3] Add broadcast tests to tests/DigiByte.Wallet.Tests/PsbtSigningServiceTests.cs: complete with a wallet input broadcasts and returns the ID, one history entry per research R9, wallet-gains case skips recording, tampered signature not broadcast, fully signed with no wallet input refused, `PrepareSignedAsync` lets an already fully signed PSBT with a wallet input broadcast without a new signature, repeats the chain checks, and refuses when a required acknowledgment is missing, failed broadcast returns the reason and keeps the signed PSBT (FR-027), watch-only wallet refused

### Implementation for User Story 3

- [X] T038 [US3] Implement `PsbtSigningService.PrepareSignedAsync` and `BroadcastAsync` in src/DigiByte.Wallet/Services/PsbtSigningService.cs, with both requiring `IPsbtKeyProvider.CanSign`, `BroadcastAsync` also requiring `IsComplete` and at least one wallet input (FR-024), and recording history through `TransactionTracker.RecordSendAsync` per research R9
- [X] T039 [US3] Add broadcast to src/DigiByte.Web/Pages/PsbtSign.razor: a separate confirmation showing the same outputs and fee (FR-025), transaction ID on success, reason and export on failure, the already-signed path from the review, which first runs `PsbtConfirmationFlow` (FR-034) and then `PrepareSignedAsync`, and the same cache clearing as `ConfirmSend` in src/DigiByte.Web/Pages/Send.razor

**Checkpoint**: A single-signer PSBT can go from load to network inside the wallet.

---

## Phase 6: User Story 5 - Sign a PSBT that lacks input data (Priority: P3)

**Goal**: Fill in missing data for SegWit inputs on the wallet's own addresses.

**Independent Test**: Load a PSBT without input data that spends the wallet's SegWit coin, and confirm the review shows the right amounts and signing succeeds.

### Tests for User Story 5 ⚠️

- [X] T040 [P] [US5] Add tests to tests/DigiByte.Wallet.Tests/PsbtSigningServiceTests.cs: data filled for a wallet SegWit input from the chain, the data is present in the exported PSBT (FR-023), the signature is valid, a legacy wallet input with no previous transaction is still refused with an explanation, a missing-data input of another participant is not filled, and a missing-data input whose lookup fails blocks signing with a retry (FR-035)

### Implementation for User Story 5

- [X] T041 [US5] Fill `WitnessUtxo` for wallet native SegWit inputs from the chain facts gathered by `LoadAsync`, in `SignAsync` in src/DigiByte.Wallet/Services/PsbtSigningService.cs, on the clone being signed

**Checkpoint**: The first release's features are complete. Story 4 follows after the first release.

---

## Phase 7: Polish & Release Checks (first release)

**Purpose**: Documentation, validation, and the release gate

- [X] T042 [P] Update docs/ARCHITECTURE.md with the new classes and service, and add the flow to docs/PROCESS_FLOWS.md
- [X] T043 [P] Add the PSBT trust model and checks to docs/SECURITY.md, update the PSBT section of docs/COMPLIANCE.md, and update docs/ROADMAP.md
- [X] T044 [P] Add this feature to CHANGELOG.md, and mention the Advanced mode entry in src/DigiByte.Web/Pages/Help.razor
- [X] T045 Run `dotnet build` and `dotnet test` across the affected test projects, and confirm all multisig tests pass unchanged (SC-008)
- [ ] T046 Run the manual checks in section 2 of specs/007-psbt-signing/quickstart.md in the running wallet, including that the review appears within 5 seconds (SC-009) and the browser console shows no PSBT contents (FR-028)
- [ ] T047 Run a security self-review with `/security-review`, and flag the PR as security-relevant, as the constitution requires
- [X] T048 **Release gate**: Run section 3 of specs/007-psbt-signing/quickstart.md against a local DigiByte Core regtest node, and record the transaction IDs in the PR (SC-005). This can't be replaced by automated tests
- [ ] T049 Run the usability check for SC-006 with 10 test users, time how long each takes from having a PSBT to exporting the result (SC-001), and record both results in the PR

---

## Phase 8: User Story 4 - Sign your part of a shared transaction (Priority: P2, after the first release)

**Goal**: Sign only the wallet's inputs, return an incomplete PSBT, and verify other participants' amounts where possible.

**Independent Test**: Load a PSBT with one input from this wallet and one from another key, sign it, and confirm only this wallet's input is signed and broadcast is not offered.

### Tests for User Story 4 ⚠️

- [ ] T050 [P] [US4] Add shared-transaction tests to tests/DigiByte.Crypto.Tests/PsbtAnalyzerTests.cs: false amount on another input refused (FR-032), fee verified when all amounts match, unverified when the lookup of an input with a stated amount fails, `SharedOutputsNotChecked` warning when any input is someone else's. Update T018's test, which expects an unverified fee for every shared PSBT, to match these rules
- [ ] T051 [P] [US4] Add shared-transaction tests to tests/DigiByte.Wallet.Tests/PsbtSigningServiceTests.cs: lookups run for other participants' inputs, a failed lookup of an input with a stated amount does not block signing, zero signatures added to other inputs (SC-004), result incomplete, broadcast refused, no unspent query for other participants' addresses, and a thrown lookup of another participant's input with a stated amount leaves `ChainStatus` as `Verified`

### Implementation for User Story 4

- [ ] T052 [US4] Add `GetTransactionAsync` lookups of other participants' inputs with PSBT data to `LoadAsync` and `SignAsync` in src/DigiByte.Wallet/Services/PsbtSigningService.cs, following the failure table in research R4
- [ ] T053 [US4] Replace the interim rule that marks every shared PSBT's fee as unverified with the mismatch refusal and fee status rules for other inputs in src/DigiByte.Crypto/Psbt/PsbtAnalyzer.cs (FR-016, FR-032)
- [ ] T054 [US4] Show each input as this wallet's or someone else's, and the signing count, in src/DigiByte.Web/Pages/PsbtSign.razor. The review's unverified-fee acknowledgment and "outputs were not checked" note already exist from T026

**Checkpoint**: Shared transactions work, and export is the only way forward for an incomplete PSBT.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies
- **Foundational (Phase 2)**: Depends on Setup. Blocks all stories
- **User Stories (Phases 3-6 and 8)**: All depend on Foundational
- **Polish and release checks (Phase 7)**: Depend on Stories 1, 2, 3 and 5
- **Story 4 (Phase 8)**: Follows the first release

### User Story Dependencies

| Story | Depends on | Notes |
|---|---|---|
| US1 (P1) | Foundational | Not releasable without US2 |
| US2 (P1) | US1 analyzer, service, and page | Must ship with US1 |
| US3 (P2) | US1 and US2 | Adds broadcast |
| US4 (P2) | US1 and US2 | After the first release. Replaces the interim unverified fee rule | 
| US5 (P3) | US1 and US2 | In the first release. Independent of US3 and US4 |

### Within Each Story

- Write tests first and confirm they fail
- Analyzer before service, service before page
- Tasks touching the same file run in order: src/DigiByte.Web/Pages/PsbtSign.razor, src/DigiByte.Crypto/Psbt/PsbtAnalyzer.cs, and src/DigiByte.Wallet/Services/PsbtSigningService.cs are each edited across stories

### Parallel Opportunities

- **Setup**: T002 and T003
- **Foundational**: T005, T008, T010, T014, and T016 are in separate files
- **Each story**: the test tasks marked [P] are in separate files
- **Polish**: T042, T043, and T044
- **Stories**: US3 and US5 can proceed in parallel after US2, but they share the service and page files, so run them in sequence unless you can coordinate merges

---

## Parallel Example: User Story 1

```text
Task: "Write analyzer tests in tests/DigiByte.Crypto.Tests/PsbtAnalyzerTests.cs" (T018)
Task: "Write service tests in tests/DigiByte.Wallet.Tests/PsbtSigningServiceTests.cs" (T019)
Task: "Write confirmation flow tests in tests/DigiByte.Wallet.Tests/PsbtConfirmationFlowTests.cs" (T020)
```

---

## Implementation Strategy

### First release: Stories 1, 2, 3 and 5

1. Complete Setup and Foundational
2. Complete Story 1, then Story 2
3. Validate with the bad-case set
4. Complete Story 3, then Story 5
5. Run the polish tasks and the release gate before shipping

### Incremental Delivery

1. Stories 1 and 2: load, review, refuse, sign, export
2. Story 3: broadcast from the wallet
3. Story 5: minimal PSBTs, completing the first release
4. Story 4: shared transaction checks and display, after the first release

### Notes

- Stop at any checkpoint to validate
- Scope changes during implementation go back into spec.md, plan.md, and this file, as the constitution requires
