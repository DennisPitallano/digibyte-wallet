# Implementation Plan: Digi-ID Address Visibility & Login History Details

**Branch**: `003-digiid-address-history-details` | **Date**: 2026-10-27 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/003-digiid-address-history-details/spec.md`

## Summary

On the Identity page, show the site-specific signing address (with copy) on the confirmation card before approval and on the success card, and make each Login History row open a details modal with everything recorded about the sign-in (callback URL, address, challenge, signature, time, security flag, outcome, each with copy). The address is derived locally from the existing per-site key path without signing, through one shared derivation so the pre-approval address always equals the signed one. History entries gain optional fields (backwards-compatible JSON) in the existing `digiid_login_history` storage. No network, server or key-handling changes.

## Technical Context

**Language/Version**: C# / .NET 10 (Blazor WebAssembly), Tailwind CSS

**Primary Dependencies**: Existing only (NBitcoin via `DigiByte.Crypto`, Blazor WASM, `IJSRuntime` clipboard as already used in `TransactionDetailModal`).

**Storage**: Existing `Storage` key `digiid_login_history` (JSON list, max 50 entries). New fields are optional so old entries still deserialize.

**Testing**: xUnit. `tests/DigiByte.Crypto.Tests` for address derivation parity with `Sign`; `tests/DigiByte.Wallet.Tests` for `WalletService` address lookup (locked, watch-only, per-site stability) and history entry (de)serialization of legacy entries. UI verified via quickstart.

**Target Platform**: Installable PWA, mobile and desktop browsers.

**Project Type**: Web application (Blazor WASM PWA) in a layered monorepo.

**Performance Goals**: Address appears with the confirmation card with no perceptible delay (one local key derivation).

**Constraints**: Strict CSP (no inline/remote assets); works offline; details modal usable at 360px width; light and dark themes; reduced-motion respected.

**Scale/Scope**: 1 page edited (`Identity.razor`), 1 new modal component, 1 small method each in `DigiIdService` and `WalletService`, at most 50 history entries.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Self-custody | Pass. Only the public address, challenge and signature are shown or stored; no seed, password or private key is displayed or persisted. |
| II. In-browser cryptography | Pass. Derivation happens in the WASM client; no server involved. |
| III. Test-first for Crypto & Wallet logic | Pass, required. Changes to `DigiByte.Crypto` and `DigiByte.Wallet` ship with tests written first, including negative paths (locked wallet, watch-only wallet). |
| IV. Boundary discipline | Pass. Derivation logic lives in Crypto/Wallet; UI only consumes it. |
| V. Resilience | Pass. Clipboard failure and legacy entries degrade gracefully (spec edge cases). |

Post-design re-check: unchanged, pass.

## Project Structure

### Documentation (this feature)

```text
specs/003-digiid-address-history-details/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── ui-contract.md
└── tasks.md             # created by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── DigiByte.Crypto/DigiId/DigiIdService.cs        # shared address-from-key helper used by Sign()
├── DigiByte.Wallet/
│   ├── Services/WalletService.cs                  # GetDigiIdAddress(domain) without signing
│   └── Models/DigiIdLoginRecord.cs                # history record (moved from Identity.razor), optional new fields
└── DigiByte.Web/
    ├── Pages/Identity.razor                       # address on confirm/success cards; tappable history rows; richer entry
    └── Components/Shared/DigiIdLoginDetailModal.razor   # new details modal (copy per field)

tests/
├── DigiByte.Crypto.Tests/DigiIdServiceTests.cs    # address helper == Sign().Address
└── DigiByte.Wallet.Tests/DigiIdAddressTests.cs    # new: wallet address lookup, legacy history JSON
```

**Structure Decision**: Existing layered layout; one shared derivation in Crypto (consumed by both `Sign` and the new wallet method) guarantees FR-004. UI-only pieces stay in `DigiByte.Web`.

## Complexity Tracking

No constitution violations; nothing to justify.
