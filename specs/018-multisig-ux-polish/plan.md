# Implementation Plan: Multisig Journey & Signing Experience Polish

**Branch**: `018-multisig-ux-polish` | **Date**: 2026-11-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/018-multisig-ux-polish/spec.md`

## Summary

A presentation-only pass over the multisig creation and signing screens. Each co-signer becomes a signer card
(avatar, "You" marker, online/offline, waiting/signed/declined). Each room gets one viewer-centric headline and one
primary button. One shared payment summary and one shared state vocabulary (labels, icons, primary actions) replace
the per-screen wording in the rooms, the Pending list, Activity, wallet cards, and the offline sign/import screens.
Creation gets a step indicator and a plain-language scheme explanation; creation-room cards show Joined or Ready (no Offline state, since the hub drops disconnected co-signers). All decisions are made by small pure
functions in `DigiByte.Wallet` (unit-tested first), rendered by new reusable Blazor components in `DigiByte.Web`.
No server, wire-contract, storage, or signing/verification change: every input the UI needs (`Online`, `Action`,
`SignedKeys`, `Unreachable`, `IsReady`, saved pending items) is already available.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`), Blazor WebAssembly

**Primary Dependencies**: Existing only (Blazor, Tailwind 3.4 via the existing build, SignalR client already wired in `SigningRoomClient`). No new packages.

**Storage**: None new. Attention counts are computed from the already-saved pending items in the existing encrypted store; nothing is added or migrated.

**Testing**: xUnit in `tests/DigiByte.Wallet.Tests` for all decision logic (state classifier, headline, presence debounce, attention counts, initials/fingerprint, scheme text). UI verified by the quickstart walkthrough (phone width, light/dark, reduced motion, screen reader). There is no component-test project today; none is introduced.

**Target Platform**: Browser PWA (phone-first), light and dark themes

**Project Type**: Web application (Blazor WASM client). `DigiByte.Api` is not touched.

**Performance Goals**: Signed/declined changes visible within 5 s (already met by the hub); Offline shown within about 10 s via a client-side hold; no extra network calls for badges.

**Constraints**: Presentation only (FR-018); no private data on the wire; offline path unchanged; status never by color alone; reduced-motion respected; touch targets at least 44 px (matches existing `min-h-[44px]`).

**Scale/Scope**: About 8 new components, edits to roughly 9 existing pages/components, up to 7 co-signers per room.

## Constitution Check

*GATE: passes before research; re-checked after design (still passes).*

| Principle | Status | Note |
|---|---|---|
| I. Self-custody | Pass | No key, seed or password handling is touched. |
| II. In-browser crypto / relay limits | Pass | No server change; relay remains public-data only. No new data shared (FR-018, clarification 1). |
| III. Test-first for Wallet logic | Pass, applies | New logic in `DigiByte.Wallet` gets tests written first (failing), in the same PR. |
| IV. Boundary discipline | Pass | Decision logic lives in `DigiByte.Wallet`; rendering in `DigiByte.Web`; no new reference direction. |
| V. Resilience | Pass | Connection loss shows a calm banner and disables only connection-dependent actions; offline flow unchanged and shares the new vocabulary. |

No violations; Complexity Tracking not needed.

## Project Structure

### Documentation (this feature)

```text
specs/018-multisig-ux-polish/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── ui-contracts.md
├── checklists/requirements.md
└── tasks.md            # created by /speckit-tasks
```

### Source Code (repository root)

```text
src/DigiByte.Wallet/
├── Models/
│   └── MultisigUxModels.cs            # enums + small records (SignerCardModel, RoomHeadline, ...)
└── Services/
    ├── MultisigStateVocabulary.cs     # shared label/icon/primary-action per state
    ├── PendingStateClassifier.cs      # pending item + viewer + live-room flag -> state
    ├── SigningRoomHeadline.cs         # room view + viewer -> headline + primary action
    ├── SignerCards.cs                 # room/config -> card models (initials, fingerprint, disambiguation)
    ├── PresenceDebouncer.cs           # ~10 s offline hold, injectable clock
    ├── AttentionCounter.cs            # pending items -> per-wallet and total counts
    └── SchemeExplainer.cs             # "Any 2 of 3 co-signers must approve each payment"

src/DigiByte.Web/
├── Components/Multisig/
│   ├── StatusBadge.razor              # icon + label from the vocabulary
│   ├── SignerCard.razor / SignerList.razor
│   ├── RoomHeadlineCard.razor             # headline + single primary button
│   ├── PaymentSummary.razor           # shared summary + collapsed Details
│   ├── StepIndicator.razor
│   ├── AttentionBadge.razor
│   ├── EmptyState.razor
│   └── ConnectionBanner.razor
├── Components/Multisig/SignerProgress.razor, CoSignerRow.razor   # reuse new pieces
└── Pages/ MultisigSigningRoom, MultisigRoomPage, MultisigCreate, MultisigImport,
          MultisigPending, MultisigActivity, MultisigWallets, MultisigDetail, PsbtSign   # adopt components

tests/DigiByte.Wallet.Tests/
└── MultisigUx*Tests.cs                # one file per new service
```

**Structure Decision**: Pure decisions in `DigiByte.Wallet` (testable, per Principle III), reusable components in
`DigiByte.Web/Components/Multisig`, pages only compose them. `DigiByte.Api`, `DigiByte.P2P.Shared`, and `DigiByte.Crypto` are unchanged.
Tailwind output is regenerated once at the end, as in 017.
