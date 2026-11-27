# Quickstart: validating DigiDollar on every DigiPay payment surface

**Feature**: [spec.md](spec.md) | **Contracts**: [contracts/](contracts/) | **Data model**: [data-model.md](data-model.md)

## Prerequisites

- Spec 034 (DD payouts) is merged. 033 writes 034's `DdAddress` rows (research R14).
- For the automated checks, nothing else: the tests use SQLite and the fake DD node.
- For the testnet walk-through:
  - NodeApi with digibyted on testnet and the DigiDollar wallet loaded (see 034's quickstart)
  - two wallets: one holding testnet DGB, one holding testnet DD
  - a merchant with a store that has a payout address (034 FR-031) and DigiDollar on

Environment for Pay.Api:

| Variable | Value |
|---|---|
| `NODEAPI_BASE_URL` / `NODEAPI_WRITE_KEY` | your testnet NodeApi |
| `DIGIPAY_DD_SURFACES` | `true`. Leave it unset first to check the gate (step 1). |

## Automated checks

```powershell
dotnet test tests/DigiByte.Pay.Api.Tests
```

```powershell
dotnet test tests/DigiByte.Pay.Web.Tests
```

```powershell
npm test --prefix src/DigiByte.Pay.Web
```

Expected: all pass, including the new suites listed in research R13.

## Walk-through (testnet)

Run Pay.Api and Pay.Web as usual, for example:

```powershell
dotnet run --project src/DigiByte.Pay.Api
```

```powershell
dotnet run --project src/DigiByte.Pay.Web
```

| # | Do | Expect | Covers |
|---|---|---|---|
| 1 | With `DIGIPAY_DD_SURFACES` unset, open the Dashboard, the POS and a store's tip page. | No DD on the till, link form, new-payment form, default-currency picker or tip page. The DigiDollar setting lists the API only and says DigiPay holds the DD. | FR-032, FR-017, FR-031, SC-004 |
| 2 | Set it to `true` and restart Pay.Api. Sign in as a merchant whose store had DD on before. | A one-time notice about buyer choice, with "Turn off" and "Got it". After "Got it", it never comes back. | FR-030 |
| 3 | POS: set the till to DD, enter 12.50 and charge. | 12.50 DD, a DD address and QR, and no drift warning. Pay 12.50 DD: the paid screen and receipt show 12.50 DD, and shift totals show DD on its own line. | Story 1, FR-003 to FR-005 |
| 4 | POS: set the till to USD, enter 25.00 and charge. Tap DD on the charge screen, then DGB, then DD. | The QR switches. The DD address is the same both times. Pay in DD: the charge locks and completes in DD. | Story 3, FR-020, FR-023, FR-024 |
| 5 | Dashboard: create a ₱1,250 payment link and open it. Switch to DD. | A DD amount rounded up to the cent and a rate line. Reload after 2 minutes: the same amount. | FR-022, clarification Q1 |
| 6 | On the step 5 link, pay in DGB, then send 1.00 DD to the DD option's address. | Paid in DGB. The Dashboard flags an extra DD payment to refund, and a `session.extra_payment` webhook arrives. 034 lists that DD as "extra payment, refund the buyer". | FR-025, C3 |
| 7 | Create a payment link with an amount typed in DGB. | Checkout shows no switch. | FR-026 |
| 8 | Call `POST /v1/pay/sessions` with an API key, a fiat price and no `buyerChoice`. | Same response shape as before plus empty `options`. No switch on checkout. | FR-028, SC-010 |
| 9 | Dashboard: set the default currency to DD, then turn DigiDollar off. | The link form and POS open on USD. Turn DD back on: DD is the default again. | FR-008 |
| 10 | Stop NodeApi. Charge in DD on the POS. Open a buyer-choice checkout. Open the tip page. | POS: a plain message, the amount kept, and "Charge in DGB" working in one tap. Checkout: DD disabled, DGB works. Tip page: DD tab disabled, DGB works. | FR-014, FR-015, SC-005 |
| 11 | Dashboard donation tab: generate a button with DD, opening on DD with a suggested $5. Open it. | The page opens on DD with 5.00 DD and the store's fixed DD address (same after reload). Switching to DGB shows the DGB address with no amount unless one was set. | Story 4, FR-010 to FR-012 |
| 12 | Turn DD off for the store and reload the step 11 tip page. | DGB only. | Story 4, scenario 3 |
| 13 | Turn buyer choice off for the store (`buyerChoiceEnabled:false`) and create a new USD link. | No switch on checkout. | FR-029 |
| 14 | Turn buyer choice back on. Open a new USD link, switch to DD and don't pay. In the Dashboard, turn DigiDollar off for the store, then reload the checkout. | The DD choice is gone and the page is back on DGB, with a note. Paying in DGB still works. | Spec edge case "DD turned off while a buyer is on checkout", research R10 |

## Release step

Set `DIGIPAY_DD_SURFACES=true` in production only once spec 034's payouts are live there and an operator has made one real payout.
