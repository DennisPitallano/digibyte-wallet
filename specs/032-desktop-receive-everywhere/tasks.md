# Tasks: Desktop Receive Everywhere

**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

## Phase 1: Menu payments in memory (US1, P1)

- [x] T001 [US1] Failing tests in `tests/DigiByte.Wallet.Tests/ScanRouterTests.cs` for `ScanRouter.TryPayment`: bare address, legacy address, `digibyte:` request with and without amount, label/message dropped, own payment link, look-alike link refused, Digi-ID / room invite / proposal / redeem script / unknown text return false, and agreement with `Route` for every case.
- [x] T002 [US1] Failing tests for `ScanRouter.SendPagePath` (personal, shared, id escaped, no payment data in the path).
- [x] T003 [US1] Failing tests in `tests/DigiByte.Wallet.Tests/PaymentHandoffTests.cs`: a payment for a shared wallet is taken only by that wallet's Send; another Send discards it; personal stays the default.
- [x] T004 [US1] Implement `TryPayment` and `SendPagePath` in `src/DigiByte.Web/Services/ScanRouter.cs`.
- [x] T005 [US1] Implement the wallet-aware `Set` / `Take` in `src/DigiByte.Web/Services/PaymentHandoff.cs`.
- [x] T006 [US1] `src/DigiByte.Web/Layout/NavMenu.razor` `OnScannedAsync`: hand every payment over and open `SendPagePath`.
- [x] T007 [US1] `src/DigiByte.Web/Pages/MultisigSend.razor`: take the hand-over, prefill from it, and hand it to the personal Send from **Pay from my wallet**.

## Phase 2: Wide-screen order (US2–US4)

- [x] T008 [US2] `src/DigiByte.Web/Pages/DigiDollarReceive.razor` to `.receive-main` order with caption.
- [x] T009 [US3] `src/DigiByte.Web/Components/Multisig/ReceiveSheet.razor` to `.receive-main` order with caption.
- [x] T010 [US4] `src/DigiByte.Web/Components/Wallet/ReceiveEmptyState.razor` caption under the QR.

## Phase 3: Polish

- [x] T011 Run `dotnet test tests/DigiByte.Wallet.Tests` (route, hand-over, locale parity).
- [x] T012 Browser check at 1280px and 390px for the three screens, and the menu paste flows with the address bar checked. Not checked by hand: **Pay from my wallet** on a shared wallet that cannot open (hard to reach locally).
- [x] T013 Changelog entry and the Help answer about pasting, if they describe the old address-bar behaviour.
