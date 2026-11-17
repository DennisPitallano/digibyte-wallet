# Implementation Plan: Online Multisig Signing

**Branch**: `feature/multisig-online-signing` | **Date**: 2026-11-17 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/017-multisig-online-signing/spec.md`

## Summary

Add live "signing rooms" so co-signers can sign and broadcast a pending multisig proposal together, the way they
already create a multisig together in a creation room. A new SignalR hub (`MultisigSigningHub`) in `DigiByte.Api`
sits next to the existing `MultisigRoomHub` and relays only public data: the partially signed transaction (PSBT),
signatures, display names, and participant status. Joining needs a challenge–response proof that the joiner holds a
co-signer key, checked by the hub against the room's co-signer key list. Every wallet signs locally (WebAssembly),
checks every incoming signature with the same rules as today's offline import, and merges it into its own stored
proposal, so a room ending never loses work. The hub hands out one broadcast claim at a time, so a payment is
broadcast at most once. Rooms are in memory only, last 15/30/60/120 minutes (operator cap, default 120), and are
rate-limited and logged with anonymous counts only. The offline share/import flow is unchanged.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`), Blazor WebAssembly for the wallet

**Primary Dependencies**: ASP.NET Core SignalR (server hub and `Microsoft.AspNetCore.SignalR.Client` in the wallet, both already in use), NBitcoin (PSBT, compact signatures), existing `MultisigWalletService` / `MultisigService`

**Storage**: Server: in-memory only (static concurrent dictionaries, as `MultisigRoomHub`), nothing persisted. Wallet: existing encrypted IndexedDB store via `ISecureStorage` (pending proposals under `MultisigPendingPrefix`)

**Testing**: xUnit; `tests/DigiByte.Wallet.Tests` (proposal adoption, signature merge, proof message), `tests/DigiByte.Crypto.Tests` (proof signing/verification), `tests/DigiByte.Api.Tests` (hub room logic via an extracted, hub-independent `SigningRoomRegistry`)

**Target Platform**: Wallet: browser PWA (WASM). Hub: `DigiByte.Api` on Linux containers

**Project Type**: Web application (static Blazor WASM client + ASP.NET Core API)

**Performance Goals**: Status/signature fan-out to all room participants in < 5 s p95 (SC-002); reconnect within 60 s restores state (SC-006)

**Constraints**: No private key, seed, or password on the wire (Principle I/II); room data in memory only and dropped at room end (FR-024); logs contain no keys, addresses, amounts, PSBTs, codes, or names (FR-028); per-connection limits 5 failed joins/min and 10 rooms/hour (FR-027); offline flow and core wallet flows never depend on the hub (FR-021, Principle V)

**Scale/Scope**: Up to 7 participants per room; one active room per proposal; expected low hundreds of concurrent rooms per API instance. Single-instance hub (same assumption as the existing creation rooms)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | How this plan complies |
|---|---|---|
| I. Self-Custody | PASS | Keys never leave the device. The hub receives only public keys, PSBTs (which carry no private keys), partial signatures, and a compact signature over a one-time challenge that can't be reused to spend. |
| II. In-Browser Cryptography | PASS | All signing (PSBT and join proof) runs in the WASM wallet through existing key access. The hub is a **co-signer coordination relay** (constitution v1.2.0): it carries public data only, keeps it in memory, logs no identifying data, is rate-limited, and is never required, since every proposal can still be signed, shared, imported, and broadcast offline (FR-020/021). |
| III. Test-First (Crypto & Wallet) | PASS | New Wallet methods (adopt proposal, merge room signatures, verify proposal matches multisig) and the Crypto proof helper get unit tests with negative paths first: wrong network, foreign key, tampered outputs, wrong txid, bad or replayed proof. |
| IV. Boundary Discipline | PASS | Proof message format and verification live in `DigiByte.Crypto` (no higher-layer deps). Proposal logic stays in `DigiByte.Wallet`. Wire contracts go in `DigiByte.P2P.Shared` (already shared by Api and Web). The hub lives in `DigiByte.Api`; UI lives in `DigiByte.Web`. No new reference directions. |
| V. Resilience | PASS | The hub is optional. If it's unreachable, "Sign together online" shows as unavailable and every offline action works (FR-021). Automatic reconnect resumes state; a room ending leaves all signatures in local storage. |
| Security: CSP | PASS | Uses the existing API origin already allowed in `connect-src` for `/hubs/multisig-room`; no new hosts. |
| Security: Secret hygiene | PASS | FR-028 logging rules; the hub never logs payloads. |
| Security: Address handling | PASS | Destination and change are derived from the PSBT and checked against the local multisig (FR-005) before display. |
| Security: Auth boundary | PASS (constitution 1.2.0) | A connection sees or changes room data only after a single-use proof of co-signer key possession, bound to that connection and purpose; the proof authorises that connection for that room until it disconnects (Rejoin needs a new proof), under the co-signer coordination relay exception added in constitution v1.2.0. Failed joins are rate-limited without revealing whether a room exists. Flag as security-relevant in the PR. |
| Workflow | PASS | Spec + clarify done. Branch to be `feature/multisig-online-signing`. Docs: update `docs/PROCESS_FLOWS.md` (multisig signing) and Help/Guide pages. |

No violations, so Complexity Tracking is empty.

**Post-design re-check (after Phase 1)**: Still PASS. The contract confirms that only public data crosses the hub
(no field can carry key material), that the join proof is single-use and bound to a connection and purpose, that
rooms live in memory only with a sweep-based expiry, and that logs are limited to counts. Two new wallet-side checks
(`MultisigProposalReader`, sighash checking in merge) add Principle III test obligations, which are listed in
[quickstart.md](quickstart.md).

## Project Structure

### Documentation (this feature)

```text
specs/017-multisig-online-signing/
├── plan.md              # This file
├── research.md          # Phase 0 decisions
├── data-model.md        # Phase 1 entities and state machines
├── quickstart.md        # Phase 1 validation guide
├── contracts/
│   └── signing-room-hub.md   # SignalR hub contract + proof format
├── checklists/requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks, not created here)
```

### Source Code (repository root)

```text
src/
├── DigiByte.Crypto/
│   └── Multisig/SigningRoomProof.cs            # NEW: build proof message, sign (given Key), verify (pubkey + compact sig)
├── DigiByte.Wallet/
│   └── Services/
│       ├── MultisigWalletService.cs            # + FindByRedeemScriptAsync, AdoptProposalAsync, MergeRoomSignaturesAsync,
│       │                                       #   CheckProposalMatches (reuses ImportSignedPSBTAsync rules), MarkBroadcastAsync
│       └── MultisigProposalReader.cs           # NEW: derive destination/amount/fee/change from a PSBT + config (no trust in room metadata)
├── DigiByte.P2P.Shared/
│   ├── Contracts/IMultisigSigningHub.cs        # NEW: hub + client contracts
│   └── Models/SigningRoom*.cs                  # NEW: SigningRoomView, SigningParticipantView, enums, options DTO
├── DigiByte.Api/
│   ├── Hubs/MultisigSigningHub.cs              # NEW: thin SignalR adapter
│   ├── Multisig/SigningRoomRegistry.cs         # NEW: rooms, codes, proposal index, claims, expiry (testable, no SignalR)
│   ├── Multisig/SigningRoomRateLimiter.cs      # NEW: per-connection sliding windows (FR-027)
│   ├── Multisig/SigningRoomOptions.cs          # NEW: MaxLifetimeMinutes (default 120), limits
│   └── Program.cs                              # + options binding, MapHub("/hubs/multisig-signing"), negotiate rate-limit policy
└── DigiByte.Web/
    ├── Services/SigningRoomClient.cs           # NEW: hub connection, reconnect + re-proof, event stream
    ├── Pages/MultisigSigningRoom.razor         # NEW: /multisig/sign-room?code=… (join) and room view
    ├── Pages/MultisigPending.razor             # + "Sign together online" action, availability state
    └── Pages/MultisigGuide.razor, Help.razor   # + online signing help

tests/
├── DigiByte.Crypto.Tests/SigningRoomProofTests.cs
├── DigiByte.Wallet.Tests/MultisigOnlineSigningTests.cs
└── DigiByte.Api.Tests/SigningRoomRegistryTests.cs, SigningRoomRateLimiterTests.cs
```

**Structure Decision**: Existing monorepo layout. A separate hub (`/hubs/multisig-signing`) instead of extending
`MultisigRoomHub`, so the creation flow's contract and clients stay untouched (see research R1). Room logic sits in
a plain `SigningRoomRegistry` so it can be unit-tested without SignalR.

## Complexity Tracking

None.
