# Implementation Plan: Receive Multi-Sig Proposals by QR Scan

**Branch**: `022-multisig-proposal-scan-import` (spec directory; the git branch is the user's choice) | **Date**: 2026-11-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/022-multisig-proposal-scan-import/spec.md`

## Summary

A co-signer's device cannot receive a brand-new proposal today: "Import signature" only merges into a proposal the device already holds. This feature adds a **Receive proposal** entry on the multi-sig pending list that accepts a proposal PSBT by camera scan, paste or file, verifies it against the open wallet, and either adds it, merges signatures into the copy already held, or refuses with a reason.

Most of the machinery already exists from online signing (spec 017): `MultisigProposalReader` reads recipient/amount/fee from the PSBT itself, `AdoptProposalAsync` saves a new proposal and merges verified signatures, `MergeRoomSignaturesAsync` strips unverifiable signatures, and `QrScannerModal` + `MultisigSendHelper.ClassifyScan` already scan and classify PSBT text. The plan adds a thin, testable result-returning service method over these, plus the pieces the clarifications require: the cancelled-restore path, a wrong-wallet lookup with a one-shot hand-off, a "received" flag for the card label, and a coins-already-spent marker on the pending list.

## Technical Context

**Language/Version**: C# / .NET 10, Blazor WebAssembly (PWA)

**Primary Dependencies**: NBitcoin (PSBT), existing `QrScannerModal` (jsQR), existing `Clipboard` and `InputFile` helpers; no new packages

**Storage**: Existing multi-sig pending-transaction store (browser IndexedDB via `MultisigWalletService`); one new boolean on `PendingMultisigTransaction` (absent in old records → false)

**Testing**: xUnit in `tests/DigiByte.Wallet.Tests` using the existing `MultisigFakeStorage` / `MultisigFakeChain` / `MultisigFakeCrypto` doubles; manual two-device and preview verification for the UI

**Target Platform**: Installed PWA and mobile/desktop browsers (camera available on most phones)

**Project Type**: Web app (Blazor WASM) with a separate wallet-logic library

**Performance Goals**: SC-001 — scan to reviewable proposal in under 30 s; the add itself is a local operation (one optional unspent-coin lookup)

**Constraints**: Fully client-side; no server call is made to receive a proposal (FR-014); the coin check degrades silently when chain data is unavailable (constitution V); proposals above ~2,200 chars stay QR-less (multi-part QR out of scope)

**Scale/Scope**: One pending page, one service method pair, one small model change, ~1 new UI panel

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 — still passes. No amendment needed.*

| Principle | Assessment |
|-----------|------------|
| I. Self-custody | No key or seed material is touched; only public PSBT data is read. Pass. |
| II. In-browser cryptography | Parsing, signature verification and merging run in the browser; no server is involved, so no relay role is used or changed. Pass. |
| III. Test-first (wallet logic) | New logic lives in `DigiByte.Wallet`; tests are written first in `DigiByte.Wallet.Tests`, including negative paths (wrong wallet, foreign signer, malformed, closed, cancelled, spent coins). Pass — enforced in tasks. |
| IV. Boundary discipline | Receive logic and coin check go in `DigiByte.Wallet` (lowest owning layer); `DigiByte.Web` only renders and wires. No new reference direction. The wrong-wallet hand-off is Web-only UI state. Pass. |
| V. Resilience | Coin lookup failure leaves the proposal signable and unmarked (no guess); offline paste/file path is first-class. No new backend dependency. Pass. |
| Security & custody | No CSP change; destination addresses come from the PSBT and are validated through the existing reader (network-specific); no secrets logged. Pass. |

## Project Structure

### Documentation (this feature)

```text
specs/022-multisig-proposal-scan-import/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── receive-proposal.md
├── checklists/requirements.md
└── tasks.md             # created by /speckit-tasks
```

### Source Code (repository root)

```text
src/DigiByte.Wallet/
├── Models/MultisigModels.cs                 # + ReceivedFromCoSigner on PendingMultisigTransaction
└── Services/
    ├── MultisigWalletService.cs             # + ReceiveProposalAsync, RestoreCancelledAsync,
    │                                        #   FindWalletForProposalAsync, GetSpentInputIdsAsync
    └── MultisigProposalReceive.cs           # + result types (new file)

src/DigiByte.Web/
├── Pages/MultisigPending.razor              # + Receive proposal panel, label, spent marker, scroll/highlight
├── Pages/MultisigDetail.razor               # + "Scan proposal" shortcut to /multisig/{id}/pending?scan=1
└── Services/ProposalHandoff.cs              # + scoped one-shot hand-off for the wrong-wallet offer (new file)

tests/DigiByte.Wallet.Tests/
└── MultisigProposalReceiveTests.cs          # new
```

**Structure Decision**: Reuse the existing library/Web split. All decisions (add vs merge vs refuse, restore, which wallet owns a PSBT, spent coins) are pure wallet logic and unit-testable; the Razor page only maps outcomes to messages and focus.

## Complexity Tracking

No constitution violations to justify.
