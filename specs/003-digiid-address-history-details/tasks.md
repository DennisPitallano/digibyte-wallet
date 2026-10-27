# Tasks: Digi-ID Address Visibility & Login History Details

**Input**: Design documents from `/specs/003-digiid-address-history-details/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ui-contract.md, quickstart.md

**Tests**: Included. The constitution (Principle III, test-first) requires unit tests for every change to `DigiByte.Crypto/` and `DigiByte.Wallet/`, written first and failing before the code change, with negative-path coverage. Razor UI is verified via quickstart.md.

**Organization**: Grouped by user story. US1 and US2 are both P1 and independent after the Foundational phase.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1, US2, US3

## Phase 1: Setup

- [x] T001 Confirm baseline: run `dotnet test tests/DigiByte.Crypto.Tests` and `dotnet test tests/DigiByte.Wallet.Tests`; note any pre-existing failures so they are not attributed to this feature

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: Shared address derivation, a reusable copy control, and the extended history record. Everything in US1/US2/US3 depends on these.

- [x] T002 [P] Write failing test in `tests/DigiByte.Crypto.Tests/DigiIdServiceTests.cs`: `DigiIdService.GetAddress(Key)` returns the same address string as `DigiIdService.Sign(request, key).Address` for the same key, and different keys give different addresses
- [x] T003 Add `public static string GetAddress(Key privateKey)` to `src/DigiByte.Crypto/DigiId/DigiIdService.cs` (Legacy P2PKH on `DigiByteNetwork.Mainnet`) and make `Sign()` call it instead of computing the address inline (makes T002 pass; FR-004)
- [x] T004 [P] Write failing tests in new `tests/DigiByte.Wallet.Tests/DigiIdAddressTests.cs`: `WalletService.GetDigiIdAddress(domain)` (a) returns the same address for the same domain, (b) differs across domains, (c) equals the address from `SignDigiIdAsync` for a request on that domain, (d) throws `InvalidOperationException` when the wallet is locked, (e) throws `InvalidOperationException` for a watch-only wallet. Reuse the existing fixtures (`InMemorySecureStorage`, `FakeCryptoService`, `FakeBlockchainService`, as set up in `WalletServiceWatchOnlyTests.cs`) and create the HD wallet through `WalletService` with a generated mnemonic. If building an unlocked HD wallet this way proves impractical, drop only case (c) (parity is already covered at Crypto level by T002) and keep (a), (b), (d), (e)
- [x] T005 Add `public string GetDigiIdAddress(string domain)` to `src/DigiByte.Wallet/Services/WalletService.cs` next to `SignDigiIdAsync`: `EnsureUnlocked()`, same HD/watch-only guard and messages as `SignDigiIdAsync`, then `DeriveSiteIndex` → `DeriveDigiIdKey` → `DigiIdService.GetAddress(key.PrivateKey)`; no signing, no network (makes T004 pass; FR-001, FR-003)
- [x] T006 [P] Write failing test in `tests/DigiByte.Wallet.Tests/DigiIdAddressTests.cs` (history record): using default `System.Text.Json` serializer options (as `Identity.razor` does), a legacy JSON entry containing only `Domain`, `Address`, `Timestamp` deserializes with all new fields null, and a full entry round-trips. Requires the history record to be accessible to tests: move `AuthHistoryEntry` from the private nested record in `src/DigiByte.Web/Pages/Identity.razor` to a public record `DigiIdLoginRecord` in `src/DigiByte.Wallet/Models/DigiIdLoginRecord.cs` (namespace `DigiByte.Wallet.Models`, next to the other wallet models, so `DigiByte.Wallet.Tests` can reference it directly)
- [x] T007 Define `DigiIdLoginRecord` in `src/DigiByte.Wallet/Models/DigiIdLoginRecord.cs` with fields per data-model.md: `Domain` (string, required), `Address` (string, required), `Timestamp` (DateTime UTC, required), and optional new `CallbackUrl` (string?), `Uri` (string?), `Nonce` (string?), `Signature` (string?), `IsUnsecure` (bool?); keep the property names identical to the old `AuthHistoryEntry` so already-stored JSON keeps loading; update `Identity.razor` to use it for `authHistory`, `LoadHistoryAsync`, and `SaveHistoryAsync` (makes T006 pass; FR-008, FR-009)
- [x] T008 [P] Create reusable copy button component `src/DigiByte.Web/Components/Shared/CopyValueButton.razor`: parameters `Value` (string), optional `Label`; copies the exact full `Value` via `navigator.clipboard.writeText` (as in `TransactionDetailModal.razor`), shows "Copied" for about 2 seconds, shows "Copy failed" if the JS call throws; has an accessible name ("Copy {Label}"); light and dark theme styling matching the existing Tailwind classes (FR-002, FR-007)

**Checkpoint**: Address derivation, tested wallet API, history record and copy control ready.

---

## Phase 3: User Story 1 - See and copy the signing address before approving (Priority: P1) 🎯 MVP

**Goal**: The confirmation card shows the full per-site signing address with a copy button before the user approves.

**Independent Test**: Quickstart manual steps 1 and 4: paste a Digi-ID URI, see the full address, copy it, and compare; a second site shows a different address; Cancel signs nothing and adds no history.

- [x] T009 [US1] In `src/DigiByte.Web/Pages/Identity.razor`, add a `pendingAddress` string field; when `AuthenticateAsync` sets `pendingRequest`, compute it with `WalletService.GetDigiIdAddress(request.Domain)` inside try/catch (`InvalidOperationException` → leave null so the block is hidden); clear it in `CancelAuth` and `ResetState`
- [x] T010 [US1] In the confirmation card of `src/DigiByte.Web/Pages/Identity.razor` (after the domain/callback block), render a "Signing address" label, the full `pendingAddress` in a wrapped monospace block (`break-all`, selectable) and `<CopyValueButton Value="@pendingAddress" Label="signing address" />`; render nothing when `pendingAddress` is null (FR-001, FR-002)
- [x] T011 [US1] Verify per quickstart steps 1 and 4 in the running app (copy/paste equals displayed character for character (SC-003), two sites differ, Cancel leaves no history, locked/watch-only shows no address block)

**Checkpoint**: US1 is independently deliverable as the MVP.

---

## Phase 4: User Story 2 - Open a detailed view of any past login (Priority: P1)

**Goal**: Each Login History row opens a modal showing all recorded details, each long value copyable.

**Independent Test**: Quickstart steps 3, 5 and 6: tap an entry, see all fields, copy values, close; an old-format entry shows "Not recorded".

- [x] T012 [US2] In `ConfirmAuthAsync` of `src/DigiByte.Web/Pages/Identity.razor`, populate the new `DigiIdLoginRecord` fields when inserting the history entry: `CallbackUrl = pendingRequest.CallbackUrl`, `Uri = pendingRequest.OriginalUri`, `Nonce = pendingRequest.Nonce`, `Signature = response.Signature`, `IsUnsecure = pendingRequest.IsUnsecure` (existing 50-entry cap and save stay unchanged; FR-008)
- [x] T013 [P] [US2] Create `src/DigiByte.Web/Components/Shared/DigiIdLoginDetailModal.razor` per contracts/ui-contract.md: parameters `IsOpen`, `Entry` (`DigiIdLoginRecord?`), `OnClosed`; overlay/card styled like `TransactionDetailModal.razor` (`modal-overlay`, `modal-card`); fields in order: Site, Outcome ("Success"), Date & time (local, with seconds), Connection (Secure/Insecure, with a warning style for insecure), Callback URL, Signing address, Challenge (nonce), Signed URI, Signature; long values use wrapped monospace (`break-all`) so there is no horizontal page scroll at 360px; each long value has `CopyValueButton`; null/empty values show "Not recorded" with no copy button; closes via button, overlay click and Escape, and on close the page restores focus to the row that opened it (FR-005, FR-006, FR-007, FR-009, FR-010)
- [x] T014 [US2] In the Login History list of `src/DigiByte.Web/Pages/Identity.razor`, turn each row into a `<button type="button">` (keep existing look, add focus ring and `aria-label="View details for @e.Domain"`) that sets `selectedEntry` and opens the modal, remembering the row so focus can return to it on close; add `<DigiIdLoginDetailModal IsOpen=... Entry=... OnClosed=... />` once near the existing `QrScannerModal`; clear `selectedEntry` when history is cleared (FR-005, FR-012)
- [x] T015 [US2] Verify per quickstart steps 3, 5, 6 and 7 (all fields, copy each value and confirm it equals the displayed text character for character (SC-003), close returns to list with focus back on the row that was tapped, HTTP flag, legacy entry shows "Not recorded", 360px width, dark theme, Clear all removes entries)

**Checkpoint**: US1 and US2 both work independently.

---

## Phase 5: User Story 3 - Copy address from the login success card (Priority: P3)

**Goal**: The "Authenticated!" card shows the full address with a copy button.

**Independent Test**: Quickstart step 2: after signing in, the success card shows the full address and copy works.

- [x] T016 [US3] In the success card of `src/DigiByte.Web/Pages/Identity.razor`, replace the truncated "Address used" value (`max-w-[200px]`, `truncate`) with the full `lastAuthAddress` in a wrapped monospace block plus `<CopyValueButton Value="@lastAuthAddress" Label="address" />` (FR-011)
- [ ] T017 [US3] Verify quickstart step 2: the success-card address equals the confirmation-card address (T010) and the history entry address (SC-004)

---

## Phase 6: Polish & Cross-Cutting

- [x] T018 [P] Regenerate the committed Tailwind CSS output if new utility classes were introduced (follow the existing Tailwind build step in `src/DigiByte.Web`, as in the earlier "regenerate tailwind.css" commit)
- [x] T019 Run `dotnet test tests/DigiByte.Crypto.Tests` and `dotnet test tests/DigiByte.Wallet.Tests`; confirm all new tests pass and nothing regressed versus T001
- [ ] T020 Run the full quickstart.md end to end in the browser, including clipboard-denied behaviour (copy shows "Copy failed", text still selectable) and offline load

---

## Dependencies & Execution Order

- **Phase 1** → **Phase 2** (blocks all stories) → **US1 / US2 / US3** → **Polish**.
- Inside Phase 2: T002 → T003; T004 → T005 (T005 needs T003); T006 → T007; T008 independent. T002, T004, T006, T008 can start in parallel (T004/T006 are in the same test file, so write them one after the other).
- **US1** needs T005, T007, T008. **US2** needs T007, T008 and is independent of US1 (T012 and T014 edit `Identity.razor`; do them after T009/T010 if working in one branch to avoid conflicts). **US3** needs T008 and, for the equality check, US1.
- Tasks touching `Identity.razor` (T007, T009, T010, T012, T014, T016) are sequential. T013 and T008 are new files and run in parallel with them.

## Parallel Example

```text
Phase 2 start:  T002 (Crypto test)  |  T004 (Wallet tests)  |  T008 (CopyValueButton component)
US2:            T013 (DigiIdLoginDetailModal.razor)  in parallel with  T012 (Identity.razor record population)
```

## Implementation Strategy

- **MVP**: Phase 1 + Phase 2 + US1 (T001–T011): the address is visible and copyable before approval.
- **Increment 2**: US2 (T012–T015): the details modal.
- **Increment 3**: US3 (T016–T017) and Polish (T018–T020).
- Write each test first and see it fail before its implementation task (constitution Principle III).
