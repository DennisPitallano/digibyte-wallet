# Implementation Plan: Sign an Unsigned Transaction (PSBT)

**Branch**: `feature/psbt-signing` (not yet created; the spec directory is `007-psbt-signing`) | **Date**: 2026-11-05 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/007-psbt-signing/spec.md`

## Summary

Let a regular single-signature wallet load a PSBT from outside, review what it really does, sign only its own inputs, and export or broadcast the result. A PSBT is untrusted input, so every fact shown or relied on is checked against the wallet's own keys and the chain, and the wallet refuses rather than signs when something doesn't match.

NBitcoin supplies the format and the per-input signing, but its convenience methods can't be relied on for safety: `SignWithKeys` signs multisig inputs containing the wallet's key and ignores requested signature types. The feature therefore adds a pure analysis and signing layer in `DigiByte.Crypto`, an orchestration service in `DigiByte.Wallet`, and one Advanced mode page in `DigiByte.Web`. No backend, explorer, or CSP change is needed. Details are in [research.md](research.md).

The first release is Stories 1, 2, 3 and 5. Story 4 follows it, and until then a PSBT with another participant's input signs only this wallet's inputs and always shows an unverified fee.

## Technical Context

**Language/Version**: C# on .NET 10

**Primary Dependencies**: NBitcoin 9.0.5 (existing), Blazor WebAssembly (existing). No new packages.

**Storage**: None for PSBTs, which are held in page memory only (FR-028). History uses the existing `TransactionTracker`.

**Testing**: xUnit, in `tests/DigiByte.Crypto.Tests` and `tests/DigiByte.Wallet.Tests`. The wallet test project also references `DigiByte.Web`. The DigiByte Core release check runs on regtest with the node in `docker-compose.yml`.

**Target Platform**: Browser (WebAssembly PWA), desktop and mobile

**Project Type**: Web application with client-side wallet libraries

**Performance Goals**: Review appears within 5 seconds for a typical PSBT on a working connection (SC-009). The user completes the flow in under 2 minutes (SC-001).

**Constraints**: All signing on the device. Fails closed when chain data is unavailable. 1 MB input limit. Core wallet flows are unaffected by this feature's failures.

**Scale/Scope**: One page, one service, three pure classes, and two small page helpers (`PsbtConfirmationFlow`, `PinAttemptThrottle`). Key search covers the wallet's scanned addresses: at least 60 on an HD wallet (20 receiving and 10 change, each in two forms), growing in native SegWit as addresses are used, or 2 on a WIF wallet. Lookups are limited to the PSBT's inputs, plus the wallet's unspent coins for its own addresses.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle or rule | Status | How this feature complies |
|---|---|---|
| I. Self-custody | Pass | Keys and seed stay on the device. Keys stay inside `WalletService`. |
| II. In-browser cryptography | Pass | Signing runs in WebAssembly. Servers only relay chain data and the signed transaction, and are never part of signing. |
| III. Test-first (non-negotiable) | Pass | Tests are written first for all `DigiByte.Crypto` and `DigiByte.Wallet` code, with negative paths for every refusal. See [quickstart.md](quickstart.md). |
| IV. Boundary discipline | Pass | `DigiByte.Crypto` has no dependency on higher layers. `DigiByte.Wallet` depends only on `DigiByte.Crypto`. Throttling, biometrics, spending limits, and warnings stay in `DigiByte.Web`, where those services already are. |
| V. Resilience | Pass | The spec defines the unavailable state: review labeled unverified, signing blocked, retry offered. Balance, receive, send, and history do not depend on this feature. |
| CSP | Pass | No change. |
| Secret hygiene | Pass | No logging of PINs, keys, or PSBT contents. Decrypted seed bytes used for PIN verification are cleared. |
| Address handling | Pass | Output addresses are derived from output scripts for the active network, not taken from user input. |
| Workflow | Pass | Conventional Commits. Spec, clarify, and plan were followed. PR flags this as security-relevant, including the move of the unlock delay rule. `docs/` is updated in the same PR. |

No violations, so Complexity Tracking is not needed.

**Post-design re-check**: Pass, re-checked on 2026-11-06 against merged `main`. The design adds no new project, dependency direction, backend dependency, or stored data. `GetVerifiedUtxosAsync` adds a chain read for this feature only, so its failures stay out of balance, send, and history (Principle V). The move of the unlock delay rule keeps one copy (Principle IV) and is flagged as security-relevant.

## Project Structure

### Documentation (this feature)

```text
specs/007-psbt-signing/
├── spec.md
├── plan.md                         # This file
├── research.md                     # Phase 0 decisions and evidence
├── data-model.md                   # Entities, states, refusal reasons
├── quickstart.md                   # How to validate the feature
├── contracts/
│   └── psbt-signing.md             # Service and page contracts
├── checklists/requirements.md
└── tasks.md                        # Created by /speckit-tasks
```

### Source Code (repository root)

```text
src/DigiByte.Crypto/Psbt/
├── PsbtDocument.cs                 # Decode text or bytes, size limit, structure checks
├── PsbtAnalyzer.cs                 # Pure classification, totals, and refusals
├── PsbtSigner.cs                   # Signs chosen inputs one at a time, tests completeness
└── PsbtModels.cs                   # Assessment records and refusal reasons

src/DigiByte.Wallet/Services/
├── IBlockchainService.cs           # Adds GetVerifiedUtxosAsync, which throws when a source can't answer
├── BlockchainApiService.cs, NodeApiBlockchainService.cs, FallbackBlockchainService.cs  # Its overrides
├── IPsbtKeyProvider.cs             # Owned scripts and signing, without exposing keys
├── PsbtSigningService.cs           # Chain lookups, signing flow, broadcast, history
└── WalletService.cs                # Implements IPsbtKeyProvider; adds VerifyPinAsync

src/DigiByte.Web/
├── Pages/PsbtSign.razor            # Load, review, confirm, result
├── Pages/Settings.razor            # Advanced mode entry near Recovery Tools
├── Pages/Unlock.razor              # Uses PinAttemptThrottle; behavior unchanged
├── Services/PinAttemptThrottle.cs  # The delay rule, shared by the unlock and signing pages
├── Services/PsbtConfirmationFlow.cs # Fixed order of checks before signing or broadcast
├── wwwroot/js/download.js          # Adds a binary download function
└── Program.cs                      # Registers the new service

tests/DigiByte.Crypto.Tests/
├── PsbtDocumentTests.cs
├── PsbtAnalyzerTests.cs
└── PsbtSignerTests.cs

tests/DigiByte.Wallet.Tests/
├── PsbtSigningServiceTests.cs
├── WalletServicePsbtTests.cs
├── PsbtConfirmationFlowTests.cs
├── VerifiedUtxoTests.cs
└── PinAttemptThrottleTests.cs

docs/                               # ARCHITECTURE, PROCESS_FLOWS, SECURITY, COMPLIANCE, ROADMAP
```

**Structure Decision**: Extends the existing layered projects, with no new project. The pure classes in `DigiByte.Crypto` take chain facts as input, so every refusal can be tested without a network. Multisig code is not touched (FR-029).

## Delivery order

| Slice | Stories | Why this order |
|---|---|---|
| 1 | Story 1, then Story 2 | They must ship together, because signing without the checks puts funds at risk. Story 1 alone is a development checkpoint |
| 2 | Story 3 | Adds finalize, broadcast, and history. Part of the first release |
| 3 | Story 5 | Adds filling in missing SegWit input data, so an input with no data can't produce an empty signing result. Part of the first release |
| 4 | Release checks | DigiByte Core regtest round trip (SC-005) and docs. Completes the first release |
| 5 | Story 4 | After the first release. Adds checks of other participants' amounts and the shared-transaction display |

## Risks to watch

| Risk | Mitigation |
|---|---|
| A later change swaps in `SignWithKeys` | A test signs a PSBT containing a multisig input with the wallet's key and asserts no signature is added |
| A compromised chain source lies about both the amount and the unspent state | Accepted in the spec's trust model. It is the same exposure as ordinary sends |
| A DigiByte Core PSBT behaves differently from the test PSBTs | SC-005 is a release gate, and not yet verified |
| Own coins beyond the search range appear as someone else's | The review explains this. Address discovery makes it rare, and ownership waits a limited time for discovery before using the saved range |
