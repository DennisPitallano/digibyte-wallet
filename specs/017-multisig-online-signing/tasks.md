---

description: "Task list for Online Multisig Signing"
---

# Tasks: Online Multisig Signing

**Input**: Design documents from `specs/017-multisig-online-signing/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/signing-room-hub.md](contracts/signing-room-hub.md), [quickstart.md](quickstart.md)

**Tests**: Included. Constitution Principle III makes tests mandatory for `DigiByte.Crypto` and `DigiByte.Wallet`
changes (written first, failing, with negative paths); the hub's room logic is tested through `SigningRoomRegistry`.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US5)

## Path Conventions

The monorepo layout from plan.md: `src/DigiByte.Crypto`, `src/DigiByte.Wallet`, `src/DigiByte.P2P.Shared`,
`src/DigiByte.Api`, `src/DigiByte.Web`, and tests under `tests/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Branch, configuration, and empty files in the right places

- [ ] T001 Work on branch `feature/multisig-online-signing` (already created, on top of `docs/constitution-1.2.0`). **Prerequisite:** the creation-room key-proof change (`refs/heads/claude/gifted-wozniak-3f626b`, commit `e759296`, which adds `src/DigiByte.Crypto/Multisig/CoSignerProof.cs`) must be merged to `main` and then into this branch before T004/T008; confirm `dotnet build src/src.sln` passes before any change
- [ ] T002 [P] Add the `MultisigSigning` section to `src/DigiByte.Api/appsettings.json` with `"MaxLifetimeMinutes": 120`, `"DefaultLifetimeMinutes": 30`, `"AllowedLifetimesMinutes": [15, 30, 60, 120]`, `"FailedJoinsPerMinute": 5`, `"RoomsOpenedPerHour": 10` (contracts/signing-room-hub.md § Configuration)
- [ ] T003 [P] Create `src/DigiByte.Api/Multisig/SigningRoomOptions.cs` (bound from `MultisigSigning`; a static `SectionName`; validation: default ∈ allowed, max ≥ smallest allowed) and register it with `builder.Services.Configure<SigningRoomOptions>(…)` in `src/DigiByte.Api/Program.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Wire contracts, the join proof, the proposal reader, the room registry core, rate limiting, the hub
shell, and the wallet's hub client. Every story depends on these.

**⚠️ CRITICAL**: No user-story work can begin until this phase is complete.

### Tests first (must fail before implementation)

- [ ] T004 [P] Extend `tests/DigiByte.Crypto.Tests/CoSignerProofTests.cs` for the signing-room domain (`CoSignerProof.SigningRoomDomain`): round-trip `Sign`/`TryVerify` for purposes `open`/`join`/`rejoin`; `BuildMessage` gives exactly `"DigiByte multisig signing room\n{purpose}\n{target}\n{nonceHex}\n{connectionId}"`; **domain separation**: a proof made with `CreationRoomDomain` never verifies under `SigningRoomDomain` and vice versa; verify fails for wrong key, changed nonce, changed connection id, changed purpose, changed target. (Wrong length, non-base64 and line-break cases are already covered by the existing tests; do not duplicate them)
- [ ] T005 [P] Write `tests/DigiByte.Wallet.Tests/MultisigProposalReaderTests.cs`: from a 2-of-3 PSBT built with `MultisigWalletService.CreateSpendingPSBTAsync`, the reader gets UnsignedTxId, destination, amount, change and fee (= Σinputs − Σoutputs > 0). Negative paths: a foreign input script, missing witness UTXO value, two external outputs, a destination for the wrong network, fee ≤ 0. Each yields a plain-language `Problems` entry
- [ ] T006 [P] Write `tests/DigiByte.Api.Tests/SigningRoomRegistryTests.cs` (core): open creates a room with a unique 6-char code from the alphabet `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`; **one live room per UnsignedTxId** (a second open by a key in that room's `CoSignerKeys` returns the existing code; a second open by any other key returns the same result as an unknown code, counts as a failed join attempt, and reveals neither the code nor the room); an open whose PSBT inputs do not all spend the multisig script rebuilt from `CoSignerKeys` and `RequiredSignatures` is rejected (test both a P2SH-P2WSH and a P2WSH multisig, and an input missing its witness UTXO); lifetimes outside `AllowedLifetimesMinutes` or above `MaxLifetimeMinutes` are rejected; the default of 30 applies when none is given; PSBT > 100 KB or with a txid ≠ claimed is rejected; Memo > 200 chars is rejected; DisplayName must be 1–40 chars after trimming; the creator key must be in `CoSignerKeys` (2–7 valid compressed keys); expiry via a fake `TimeProvider` removes the room and reports reason `expired`
- [ ] T007 [P] Write `tests/DigiByte.Api.Tests/SigningRoomRateLimiterTests.cs`: the 6th failed join within 60 s is limited and passes again after the window; the 11th room open within 3600 s is limited; windows are per connection and are dropped on disconnect

### Implementation

- [ ] T008 [P] Reuse `src/DigiByte.Crypto/Multisig/CoSignerProof.cs` (from T001's prerequisite) with `domain = CoSignerProof.SigningRoomDomain` for every signing-room proof (research R2); **do not add a second proof helper**. Change `CoSignerProof` only if T004 shows a gap, keeping the creation-room hub's behaviour and tests unchanged. Makes T004 pass
- [ ] T009 [P] Create the wire DTOs in `src/DigiByte.P2P.Shared/Models/SigningRoom.cs`: `SigningRoomView` (RoomId, InviteCode, ExpiresAt UTC, Network `main|test`, UnsignedTxId, RequiredSignatures, CoSignerKeys[], PsbtBase64, Memo?, SignedPsbts[{PublicKeyHex, PsbtBase64}], Participants[], Status `Open|Complete|Broadcast`, Unreachable, BroadcastClaimHolder?, BroadcastTxId?, IncludesPayload), `SigningParticipantView` (PublicKeyHex, DisplayName, Online, Action `None|Signed|Declined`, IsCreator; **no ConnectionId**), `SigningRoomOptionsView`, `OpenSigningRoomRequest` (includes `Network` `main|test`), `JoinSigningRoomRequest`, `RejoinSigningRoomRequest`
- [ ] T010 [P] Create `src/DigiByte.P2P.Shared/Contracts/IMultisigSigningHub.cs` with `IMultisigSigningHub` (GetOptions, RequestChallenge, OpenRoom, JoinRoom, Rejoin, SubmitSignature, Decline, ClaimBroadcast, ReportBroadcast, CloseRoom, LeaveRoom) and `IMultisigSigningClient` (Options, Challenge, RoomState, ExistingRoom, SignatureAdded, BroadcastGranted, BroadcastClaimed, Broadcasted, BroadcastFailed, RoomEnded, Superseded, Error(code, message)), exactly as in contracts/signing-room-hub.md
- [ ] T011 Create `src/DigiByte.Wallet/Services/MultisigProposalReader.cs` returning the `ProposalFacts` value object (UnsignedTxId, Inputs, DestinationAddress, AmountSat, ChangeSat, FeeSat, Problems) from a PSBT and a `MultisigWalletConfig` (data-model.md § ProposalFacts). Makes T005 pass
- [ ] T012 Create `src/DigiByte.Api/Multisig/SigningRoomRegistry.cs` (no SignalR dependency; takes `TimeProvider` and `IOptions<SigningRoomOptions>`): the room store (each room stores its `Network`), invite-code index, UnsignedTxId index, challenge store (60 s, single use, one per connection), open/join lookup, participants keyed by public key, `Sweep()` for expiry, and data dropped on removal (FR-024). Makes T006 pass
- [ ] T013 [P] Create `src/DigiByte.Api/Multisig/SigningRoomRateLimiter.cs`: sliding windows per connection id, with `FailedJoinsPerMinute` (5/60 s) and `RoomsOpenedPerHour` (10/3600 s) from options, and `Forget(connectionId)`. Makes T007 pass
- [ ] T014 Create `src/DigiByte.Api/Hubs/MultisigSigningHub.cs`, a thin adapter over the registry and rate limiter implementing GetOptions, RequestChallenge, OpenRoom (proof purpose `open`, target = UnsignedTxId; rebuild the sorted multisig script from `CoSignerKeys` and `RequiredSignatures` and require every PSBT input to spend it (an input passes when its witness UTXO's script is either the P2WSH or the P2SH-P2WSH form of that script (the app's default address type is `p2sh-p2wsh`); an input without a witness UTXO is refused), else `Error("bad_psbt", …)`; on duplicate → `ExistingRoom(code)` **only when the proven key is in the existing room's `CoSignerKeys`**, otherwise `Error("invalid_code", "Invalid or expired code.")` counted as one failed join attempt, per constitution 1.2.0 Auth boundary), JoinRoom (rate limit first; an unknown code, a bad proof, or a non-member key all → `Error("invalid_code", "Invalid or expired code.")` and count one failure; over the limit → `Error("rate_limited", "Too many attempts, try again shortly.")`), and group membership by RoomId. Parse PSBTs with NBitcoin for the `Network` in `OpenSigningRoomRequest`, store it on the room, and use the room's network for every later `SubmitSignature` check; reject an unknown network with `Error("bad_psbt", …)`
- [ ] T015 Register in `src/DigiByte.Api/Program.cs`: `SigningRoomRegistry` and `SigningRoomRateLimiter` as singletons, `TimeProvider.System`, a hosted service that calls `registry.Sweep()` every 15 s and sends `RoomEnded` for ended rooms, `app.MapHub<MultisigSigningHub>("/hubs/multisig-signing")`, and a fixed-window policy of 30 negotiates/min per client address (reuse `SponsorEndpoints.ClientKey`) on that hub (research R10)
- [ ] T016 [P] Add anonymous metrics and logs in `src/DigiByte.Api/Multisig/SigningRoomTelemetry.cs` (`signing_room.opened` with lifetime, `.ended` with reason/participant count/signature count, `.broadcast` with outcome, `.rate_limited` with kind, `.error` with kind; exception *type* only) and call it from the hub and the sweep. **Never** log room id, code, keys, addresses, amounts, PSBTs, or names (FR-028)
- [ ] T017 Create `src/DigiByte.Web/Services/SigningRoomClient.cs` (scoped): a lazy `HubConnection` to `{apiBase}/hubs/multisig-signing` (apiBase found the same way as `MultisigRoomPage.EnsureHubConnected`), `WithAutomaticReconnect(0, 2, 5, 10, 20, 30 s)`, `CheckAvailabilityAsync()` with a 5 s timeout cached per session (research R12), typed events for every `IMultisigSigningClient` callback, a challenge helper that signs with `CoSignerProof.Sign(key, CoSignerProof.SigningRoomDomain, …)` using `WalletService.GetPrivateKeyForMultisig()` (the key is never kept in a field), and registration in `src/DigiByte.Web/Program.cs`

**Checkpoint**: Proof, reader, registry, limiter, hub shell and client exist, and all Phase 2 tests pass.

---

## Phase 3: User Story 1 - Collect signatures in a live signing room (Priority: P1) 🎯 MVP

**Goal**: From a pending proposal, open a room; co-signers join by code, see the checked payment details, sign
locally, and everyone sees signatures live and gets "Ready to broadcast".

**Independent Test**: Quickstart scenarios 1, 3, 5. A creates a 2-of-3 proposal and opens a room; B joins by code,
signs; both show 2/2 with no export or import.

### Tests for User Story 1

- [ ] T018 [P] [US1] Write `tests/DigiByte.Wallet.Tests/MultisigOnlineSigningTests.cs` (lookup + adopt + merge): `FindByRedeemScriptAsync` matches a local multisig from its co-signer keys and M given in any order, and returns no match for a wrong M, a foreign key, or a config owned by another wallet. `AdoptProposalAsync` saves a proposal whose destination/amount/fee come from `MultisigProposalReader`, not from room metadata, and refuses a PSBT with `Problems`, a different multisig, or a cancelled local copy. `MergeRoomSignaturesAsync` merges a valid co-signer signature, **discards a partial signature that fails sighash checking**, refuses another txid or a foreign key, counts each key once, and sets `ReadyToFinalize` at the threshold. `CheckProposalMatches` refuses a room PSBT whose outputs differ from the local proposal with the same id. After adopt and merge, the local multisig's co-signer labels (`CoSigner.Name`) are unchanged (FR-025)
- [ ] T019 [P] [US1] Extend `tests/DigiByte.Api.Tests/SigningRoomRegistryTests.cs`: a join with a member key adds the participant; a join with a non-member key and a join with an unknown code give the same result; `SubmitSignature` is accepted only when the txid matches and every new partial signature belongs to the caller's key; an accepted submission marks the participant Signed and replaces that key's earlier entry; `Decline` is refused after Signed; `Complete` is set when the distinct signed keys ≥ RequiredSignatures; `Unreachable` is set when the declines leave fewer than M possible signers (FR-022)

### Implementation for User Story 1

- [ ] T020 [US1] Add `FindByRedeemScriptAsync(coSignerKeys, requiredSignatures, ownerWalletId)` to `src/DigiByte.Wallet/Services/MultisigWalletService.cs`, rebuilding the sorted multisig redeem script and matching it to a local `MultisigWalletConfig.RedeemScriptHex` (research R4)
- [ ] T021 [US1] Add `AdoptProposalAsync(config, psbtBase64, memo)` and `CheckProposalMatches(pending, psbtBase64)` to `src/DigiByte.Wallet/Services/MultisigWalletService.cs` using `MultisigProposalReader`. Reuse an existing pending proposal with the same `UnsignedTxId` instead of creating a duplicate; refuse when the local copy is `Cancelled`
- [ ] T022 [US1] Add `MergeRoomSignaturesAsync(pending, psbtBase64)` to `src/DigiByte.Wallet/Services/MultisigWalletService.cs`: the `ImportSignedPSBTAsync` rules plus a check of each partial signature against its input's sighash (research R6), returning which keys were accepted and which were rejected. Make T018 pass
- [ ] T023 [US1] Implement `SubmitSignature` and `Decline` in `src/DigiByte.Api/Hubs/MultisigSigningHub.cs` and `src/DigiByte.Api/Multisig/SigningRoomRegistry.cs` (relay `SignatureAdded(key, psbt)` + `RoomState`; `Error("bad_signature", …)` to the caller on rejection; never relay rejected data). Make T019 pass
- [ ] T024 [US1] Add a **Sign together online** action to `src/DigiByte.Web/Pages/MultisigPending.razor` for proposals with `Status` AwaitingSignatures/ReadyToFinalize, shown only when this wallet holds a co-signer key. A sheet lets the user choose the lifetime (choices from `GetOptions`, default 30) and a display name (1–40 chars, prefilled with the local label), then calls OpenRoom with a fresh proof and goes to the room. Disable the open button while the request is in flight so a double tap opens one room (FR-026). When `ExistingRoom(code)` comes back, offer to join it
- [ ] T025 [US1] Create `src/DigiByte.Web/Pages/MultisigSigningRoom.razor` at `@page "/multisig/sign-room"` with a code entry and a display-name entry. On join it first refuses a room whose `Network` differs from the active wallet's network ("This room is for a different network"), then calls `FindByRedeemScriptAsync` → `CheckProposalMatches` or `AdoptProposalAsync` → shows recipient (full address), amount, fee, change, memo (labelled "note from co-signer, not verified"), and the per-co-signer checklist (Not joined / Joined / Offline / Signed / Declined) with x of M. The room header shows the amount, a shortened recipient and the code, so rooms for different payments are easy to tell apart. Room display names are shown only in the room and are **never** written to the local `CoSigner.Name` labels (FR-025). Each refusal gets a clear reason (FR-005, FR-007, FR-009)
- [ ] T026 [US1] In `src/DigiByte.Web/Pages/MultisigSigningRoom.razor`, add room info (6-char code, a copyable link `{origin}/multisig/sign-room?code=XXXXXX`, a QR code using the existing QR component, and a live countdown to `ExpiresAt`) plus **Sign**, shown only while the local proposal's `SignedBy` does not contain this wallet's key (otherwise show "You signed"), (unlock if needed → `SignPendingTransactionAsync` → `SubmitSignature`) and **Decline** with a confirmation. Guard against double taps (FR-026)
- [ ] T027 [US1] In `src/DigiByte.Web/Pages/MultisigSigningRoom.razor`, handle `SignatureAdded` with `MergeRoomSignaturesAsync`, show a room warning naming the participant when a signature is rejected, update the checklist, and show "Ready to broadcast" when the local merged copy reaches M (FR-010, FR-011)
- [ ] T028 [US1] Show the "payment can't be completed" banner in `src/DigiByte.Web/Pages/MultisigSigningRoom.razor` when `Unreachable` is set (declines) or when the inputs are found spent (check through `GetSpendableUtxosAsync` on load, every 60 s while the room is open, and before broadcast; skip a check while the previous one is still running), suggesting a local cancel (FR-022)

**Checkpoint**: US1 works end to end except for broadcasting from the room. The offline Broadcast on the Pending page already works with the merged copy.

---

## Phase 4: User Story 2 - Broadcast once the threshold is reached (Priority: P1)

**Goal**: One participant broadcasts; everyone sees the txid; each local proposal moves to Activity as Sent, once.

**Independent Test**: Quickstart scenario 4. A and B tap Broadcast together; there is one broadcast and the same
txid on both, and Activity lists it once as Sent.

### Tests for User Story 2

- [ ] T029 [P] [US2] Extend `tests/DigiByte.Api.Tests/SigningRoomRegistryTests.cs`: only one claim is held at a time; a second claimer gets the holder's key; the claim is released on a failed report, after 60 s (fake time), or when the holder disconnects; only the holder can report; a success sets `Broadcast` + txid and the room is removed after a 60 s grace period; a failure reason is cut to 200 chars
- [ ] T030 [P] [US2] Extend `tests/DigiByte.Wallet.Tests/MultisigOnlineSigningTests.cs`: `MarkBroadcastAsync(pending, txId)` sets `Broadcast` + `BroadcastTxId`, is a no-op when already broadcast with the same id, and refuses a cancelled proposal. `FinalizeAndBroadcastAsync` treats a node reply of "already in mempool / already known" as success and returns the computed txid

### Implementation for User Story 2

- [ ] T031 [US2] Add `MarkBroadcastAsync` and the "already known counts as success" handling to `src/DigiByte.Wallet/Services/MultisigWalletService.cs` (research R7). Make T030 pass
- [ ] T032 [US2] Implement `ClaimBroadcast` and `ReportBroadcast` in `src/DigiByte.Api/Multisig/SigningRoomRegistry.cs` and `src/DigiByte.Api/Hubs/MultisigSigningHub.cs` (`BroadcastGranted` to the caller, `BroadcastClaimed(holder)` to the group, `Broadcasted(txId)` / `BroadcastFailed(reason)`, `RoomEnded("broadcast")` after the grace period). Make T029 pass
- [ ] T033 [US2] In `src/DigiByte.Web/Pages/MultisigSigningRoom.razor`, add a **Broadcast** button (shown when the local merged copy reaches M). Claim → when granted, run `FinalizeAndBroadcastAsync` → `ReportBroadcast`. While another participant holds the claim, show "Broadcasting by {name}…". On `Broadcasted`, call `MarkBroadcastAsync` and show the txid with copy. On `BroadcastFailed`, show the reason and allow **Retry** for any participant (FR-014–FR-016). Guard against double taps

**Checkpoint**: The MVP (US1 + US2) is complete: sign together and broadcast without a file or QR exchange.

---

## Phase 5: User Story 3 - Join a signing room as an invited co-signer (Priority: P2)

**Goal**: Joining is effortless and safe: deep link with unlock, the right wallet, the proposal saved locally, and
signatures held on the device shared.

**Independent Test**: Quickstart scenarios 2 and 12. B opens the link while locked, unlocks, lands in the right room,
and keeps the proposal after leaving; a watch-only device is refused.

### Tests for User Story 3

- [ ] T034 [P] [US3] Extend `tests/DigiByte.Wallet.Tests/MultisigOnlineSigningTests.cs`: joining with a local copy that already has an offline-imported signature results in both sides' signatures after the merge (FR-012); the join lookup refuses a watch-only config (`OwnKeyIndex == -1`) as a signing wallet; `ListWalletsAsync`-based lookup finds a matching multisig owned by another wallet on the device (for the switch offer in T037)

### Implementation for User Story 3

- [ ] T035 [US3] Support `?code=` in `src/DigiByte.Web/Pages/MultisigSigningRoom.razor`: if the wallet is locked, send the user to unlock with a return URL and come back with the code prefilled (FR-003, follow how `MultisigRoomPage` handles `?code=`)
- [ ] T036 [US3] On join in `src/DigiByte.Web/Pages/MultisigSigningRoom.razor`, submit every signature already held in the local proposal (`SubmitSignature` with the local PSBT, if it has any partial signatures) so the room and the device combine them (FR-012, US3 scenario 4)
- [ ] T037 [US3] Handle the wallet choice in `src/DigiByte.Web/Pages/MultisigSigningRoom.razor`: if the active wallet isn't a co-signer but another wallet on the device owns a matching multisig, show "Switch to {wallet name} to join" with a one-tap switch that resumes the join. If no wallet matches, refuse with "None of your wallets is a co-signer of this multisig" (research R13)
- [ ] T038 [US3] Refuse watch-only devices in `src/DigiByte.Web/Pages/MultisigPending.razor` and `src/DigiByte.Web/Pages/MultisigSigningRoom.razor`: show online signing as unavailable with "Watch-only copies can't join signing rooms; use Share/Import" and never send a proof (spec edge case, clarification Q4)

**Checkpoint**: Invitees can join from a link on any device setup and keep their proposal.

---

## Phase 6: User Story 4 - Recover from disconnection and room end (Priority: P2)

**Goal**: Dropped connections resume automatically; a room ending loses nothing; the hub being down never blocks
the offline flow.

**Independent Test**: Quickstart scenarios 7, 8, 10, 11.

### Tests for User Story 4

- [ ] T039 [P] [US4] Extend `tests/DigiByte.Api.Tests/SigningRoomRegistryTests.cs`: a disconnect marks the participant offline (not removed) and keeps their Signed state; **the creator disconnecting doesn't end the room**; `Rejoin` with a valid `rejoin` proof moves the participant to the new connection and the old one gets `Superseded`; a second live connection for the same key replaces the first; the rate-limiter state for a disconnected connection is forgotten

### Implementation for User Story 4

- [ ] T040 [US4] Implement `OnDisconnectedAsync`, `Rejoin`, and `LeaveRoom` in `src/DigiByte.Api/Hubs/MultisigSigningHub.cs` and `src/DigiByte.Api/Multisig/SigningRoomRegistry.cs` (mark offline, release a broadcast claim the leaver holds, `Superseded` for replaced connections, a full `RoomState` with payload on rejoin). Make T039 pass
- [ ] T041 [US4] In `src/DigiByte.Web/Services/SigningRoomClient.cs`, on `Reconnected` request a new challenge and call `Rejoin(roomId)`. While reconnecting, show "Reconnecting…" in the room. If reconnection gives up or `RoomEnded` arrives, end the session but keep the local proposal
- [ ] T042 [US4] Add a room-ended view to `src/DigiByte.Web/Pages/MultisigSigningRoom.razor` with the reason (expired / closed / broadcast / connection lost), the collected signature count, and buttons to **Share** / **Import** / **Broadcast** on the Pending page (FR-020)
- [ ] T043 [US4] Add the unavailable state to `src/DigiByte.Web/Pages/MultisigPending.razor`: when `CheckAvailabilityAsync` fails, the action shows "Online signing is unavailable right now. You can still share and import signatures." No other page connects to the hub (FR-021, Principle V)

**Checkpoint**: Flaky networks and outages cost no signatures.

---

## Phase 7: User Story 5 - Room creator controls the room (Priority: P3)

**Goal**: The creator can close the room; anyone can reopen one for the proposal; there's one active room per payment.

**Independent Test**: Quickstart scenarios 9 and 10.

### Tests for User Story 5

- [ ] T044 [P] [US5] Extend `tests/DigiByte.Api.Tests/SigningRoomRegistryTests.cs`: only the creator can `CloseRoom`; closing frees the UnsignedTxId so a new room (new code) can be opened; the old code then gives `invalid_code`

### Implementation for User Story 5

- [ ] T045 [US5] Implement `CloseRoom` in `src/DigiByte.Api/Hubs/MultisigSigningHub.cs` and `src/DigiByte.Api/Multisig/SigningRoomRegistry.cs` (creator only → `Error("not_allowed", …)` otherwise; `RoomEnded("closed")`; data dropped). Make T044 pass
- [ ] T046 [US5] Add a creator-only **Close room** action with a confirmation ("Closing the room does not cancel the payment") in `src/DigiByte.Web/Pages/MultisigSigningRoom.razor`, and make **Sign together online** on `src/DigiByte.Web/Pages/MultisigPending.razor` open a fresh room once the previous one has ended

**Checkpoint**: All five stories work on their own.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T047 [P] Add online-signing help to `src/DigiByte.Web/Pages/MultisigGuide.razor` and `src/DigiByte.Web/Pages/Help.razor` (how to open or join, lifetimes, co-signers only, offline fallback)
- [ ] T048 [P] Document the signing-room flow (sequence: open → join proof → sign → merge → claim → broadcast) in `docs/PROCESS_FLOWS.md` and the new hub in `docs/ARCHITECTURE.md`, including the deployment constraint: rooms live in one API instance's memory, so run a single instance or use sticky routing for `/hubs/multisig-signing`
- [ ] T049 [P] Add a "Real-Time Multisig Signing Rooms" entry to `src/DigiByte.Web/Pages/Roadmap.razor`
- [ ] T050 Review every new log/metric call in `src/DigiByte.Api/Hubs/MultisigSigningHub.cs` and `src/DigiByte.Api/Multisig/` against FR-028, and check that `SigningParticipantView` never carries `ConnectionId`
- [ ] T051 Run `dotnet build src/src.sln` and `dotnet test` for `tests/DigiByte.Crypto.Tests`, `tests/DigiByte.Wallet.Tests`, `tests/DigiByte.Api.Tests`; then run quickstart.md manual scenarios 1–14 with two browser profiles
- [ ] T052 Note in the PR description that the change is **security-relevant** (hub calls authorised by proof of key possession) and cite constitution v1.2.0 (co-signer coordination relay and its auth exception). If the amendment ships in its own PR, merge it first (plan.md Constitution Check)
- [ ] T053 [P] Write `tests/DigiByte.Api.Tests/SigningRoomLatencyTests.cs` using `WebApplicationFactory<Program>` and three in-process SignalR clients (add `Microsoft.AspNetCore.SignalR.Client` to `tests/DigiByte.Api.Tests/DigiByte.Api.Tests.csproj`): over 20 runs, a `SubmitSignature` reaches the other two clients with p95 < 5 s (SC-002); after a client's connection is dropped and restarted, `Rejoin` restores `RoomState` within 60 s (SC-006). Record the numbers in the PR

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)** → **Foundational (Phase 2)** → user stories.
- **US1 (P1)** depends only on Phase 2.
- **US2 (P1)** depends on US1 (it needs a room with merged signatures).
- **US3 (P2)** depends on US1 (it builds on the join page). It can run in parallel with US2.
- **US4 (P2)** depends on US1. It can run in parallel with US2/US3 apart from shared files (see below).
- **US5 (P3)** depends on US1.
- **Polish** after the chosen stories are done.

### Shared-file ordering

`MultisigSigningRoom.razor`, `MultisigSigningHub.cs`, `SigningRoomRegistry.cs` and `MultisigWalletService.cs` are
touched by several stories. Tasks on the same file run one after another in ID order. Tests in different test
files are `[P]`.

### Within each story

Tests (failing) → Wallet/Crypto logic → hub/registry → UI.

## Parallel Examples

```text
Phase 2 tests together:       T004, T005, T006, T007
Phase 2 independent impl:     T008, T009, T010, T013, T016
US1 tests together:           T018, T019
After US1, in parallel:       US2 (T029–T033) and US3 (T034–T038), different owners per file
Polish docs together:         T047, T048, T049
```

## Implementation Strategy

### MVP first (US1 + US2)

1. Phases 1–2.
2. US1 → check with quickstart scenarios 1, 3, 5.
3. US2 → check with scenario 4. **Stop and demo**: co-signers sign and broadcast together without passing files.

### Incremental delivery

4. US3 (deep link, wallet switch, signatures held on the device) → scenarios 2, 12.
5. US4 (resilience) → scenarios 7, 8, 10, 11.
6. US5 (close and reopen) → scenario 9.
7. Polish → scenario 13 (logs), docs, PR flags.
