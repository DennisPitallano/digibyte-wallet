# Quickstart: validating DigiDollar payouts

These are end-to-end checks for [spec.md](spec.md). Endpoint and payload details are in [contracts/](contracts/), and states and fields are in [data-model.md](data-model.md).

## Prerequisites

- **Node:** a testnet `digibyted` with DigiDollar active and the `digipay-hot` wallet loaded. The wallet needs at least 100 DGB for fees and some DD to pay test sessions.
- **NodeApi:** running against that node with `DIGIDOLLAR_WALLET=digipay-hot` and `NODEAPI_WRITE_KEY` set.
- **Pay.Api:**
  - `NODEAPI_BASE_URL` and `NODEAPI_WRITE_KEY` set to match NodeApi.
  - `DIGIPAY_OPERATOR_KEY` set.
  - Worker interval left at the 30-second default.
- **Pay.Web:** running and pointed at Pay.Api.
- **Merchant wallet:** a second DigiByte wallet (the DigiByte Wallet PWA on testnet) with a DD receive address and a Digi-ID identity. Use it to sign in to the Dashboard.
- **Store:** a testnet store with a webhook URL pointed at a request bin.

## Automated checks

```powershell
dotnet test tests/DigiByte.Pay.Api.Tests
```

```powershell
dotnet test tests/DigiByte.Pay.Web.Tests
```

```powershell
dotnet test tests/DigiByte.NodeApi.Tests
```

The Pay.Api tests use `FakeDdNode` (research R13) and cover:
- the address checks (format, checksum, wrong network, DGB address, DigiPay's own address)
- the step-up grant (missing, expired, reused, wrong session, address mismatch)
- the 48-hour hold and payouts held while it runs
- cancelling a change
- the schedule, threshold and 7-day cap
- the 1.00 DD minimum
- sending → checking → sent and → failed, with no second send
- fee-reserve and coverage waits
- node down giving `waiting`, never `failed`
- the automatic pause after 3 failures
- operator pause, and operator-key fail-closed
- an absorbed reversal
- store-delete guard and remainder transfer
- webhook bodies and retries of `PayloadJson`
- refusing `dgp_` keys on address routes

## Manual testnet runs

| # | Scenario | Steps | Expected |
|---|---|---|---|
| Q0 | Node send shape (research R1) | Call NodeApi `POST /send` with the key, 1.00 DD and comment `test-q0`, then `GET /sends?since=` | `/send` returns `{txid}`. `/sends` lists that txid; record whether `comment` comes back. |
| Q1 | First address, nothing held (US1) | New store → DigiDollar settings → paste the merchant wallet's DD address → confirm → Digi-ID sign-in | The address applies at once. The audit log has `payout_address.requested` and `.applied`. The bin gets `change_requested` and `changed`. |
| Q2 | Address refused (US1 #3) | Paste a DGB address, a mainnet DD address, an address with one character changed, and the address of a DD session from Q3 | Each is refused with its specific reason, and nothing is saved. |
| Q3 | Automatic payout (US1 #4–6) | Create a 5.00 DD session through the API and pay it from a third wallet | Within 15 minutes of the session confirming, the merchant wallet receives exactly 5.00 DD. The payout goes sent → confirmed and links to the session. The bin gets `payout.sent` and `payout.confirmed`. |
| Q4 | Balance during an outage (US2, FR-034/035) | Stop NodeApi, pay another session from a wallet connected to a different node, wait 2 minutes | The Dashboard shows the last balance with "may be out of date" and the reason "waiting for DigiDollar service". No payout is `failed`. DGB checkout still works. Restart NodeApi and the payout goes out. |
| Q5 | Address change with hold (US3) | Request a change to a new address → Digi-ID → watch the Dashboard, then pay a session | A banner and countdown appear, plus `change_requested` and an audit entry. The payment is held with reason "address change pending". Cancel the change and the held DD goes to the old address. Request it again and set `EffectiveAt` back in the dev database: it applies and the payout goes to the new address. |
| Q6 | API key refused (US3 #6) | `PUT /stores/{id}/payout-address` with a `dgp_` key | 403 `session_required` |
| Q7 | Held DD from before payouts (US4) | Use a store with DD sessions from before this feature (including an overpaid one and one paid after expiry) and no address | The balance is broken down by source. Setting the first address triggers the 48-hour hold. After it applies, one payout lists every source. |
| Q8 | Threshold and Pay out now (US5) | Threshold 50.00: pay 20.00 and 25.00, then 10.00. Then press "Pay out now" with 3.00 held | Nothing is paid until 55.00 is reached, then one payout of 55.00. "Pay out now" sends 3.00. |
| Q9 | Operator pause | `POST /operator/payouts/pause` with the key, then pay a session | The Dashboard shows "paused by DigiPay". Nothing is sent or failed. Resume and it is sent. |
| Q10 | Store delete | Delete a store holding 0.50 DD and then one holding 7.00 DD | 0.50 moves to the merchant-level balance and the store is deleted. 7.00 returns 409 `dd_held` until a final payout is confirmed. |

## Release checklist

- [ ] Production NodeApi has `DIGIDOLLAR_WALLET=digipay-hot`, a funded fee reserve, and a write key shared with Pay.Api (research R11, `docs/RAILWAY_NODE.md`).
- [ ] `DIGIPAY_OPERATOR_KEY` is set, and Railway alert rules exist for each `DdPayoutAlert` value.
- [ ] The backfill has run, and the operator health endpoint shows `coverageOk: true` before payouts are enabled.
- [ ] The docs and Dashboard copy updated (plan Constitution Check, Docs row): DD is described as held until payout, not "non-custodial".
- [ ] The PR is flagged security-relevant (step-up auth and operator key).
