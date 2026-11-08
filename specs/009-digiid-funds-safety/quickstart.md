# Quickstart: validating Digi-ID Address Funds Safety

Run these after implementation. For contract details see [contracts/wallet-service.md](contracts/wallet-service.md) and [contracts/ui.md](contracts/ui.md); for types see [data-model.md](data-model.md).

## Prerequisites

- .NET 10 SDK.
- A throwaway HD wallet. Use a **test or regtest network**, or a mainnet wallet holding only a trivial amount. Never validate with real savings.
- For the end-to-end checks, a way to send a small amount to an arbitrary address (another wallet or a faucet on the test network).

## 1. Automated tests

```bash
dotnet build src/src.sln
```

```bash
dotnet test tests/DigiByte.Crypto.Tests --filter DigiIdDomain
```

```bash
dotnet test tests/DigiByte.Wallet.Tests --filter "DigiIdSweep|DigiIdFundsRecovery"
```

Expected: all pass, including these negative cases:

- Locked wallet, watch-only wallet and WIF wallet each refuse scan, plan and execute and make no lookup.
- A throwing lookup gives `CouldNotCheck`, never `Empty`.
- A plan never contains two equal destinations, and never more than 19 items.
- After a sweep is executed, the address the Receive page shows (`GetReceivingAddressAsync`) is not one of the sweep destinations, and `NextReceivingIndex` is the highest destination index plus one.
- An address whose net is below 0.0001 DGB is excluded as too small.
- Execute never broadcasts when saving the destination index fails.
- A failed item leaves the others to run, and a second `Execute` for an address with a `Pending` record sends nothing.
- A manually entered `HTTPS://Example.com:8443/path` gives the same address as a login to `digiid://example.com/...`.
- An output with a different script is excluded from amounts and sweeps and counted as "other outputs left untouched".
- A wallet that locks between two items stops, marks the unsent items `Failed` with "wallet locked", and leaves broadcast items alone.
- The signed fee is within 10% of the estimate shown in the plan.
- Opening or unlocking the wallet makes no Digi-ID lookup (FR-014).

## 2. Warning (User Story 1)

1. Run the wallet and open the **ID** tab with an unlocked wallet.
2. Paste a `digiid://` test URI. The approval panel shows the signing address.
3. **Expect**: the warning is directly under the address, with a link to the recovery section.
4. Approve. **Expect**: the same warning under the "Address used" row on the success panel.
5. Open an entry in Login History. **Expect**: the warning under "Signing address".
6. Open Help, then Digi-ID. **Expect**: the new entry and the link to the recovery section.
7. Switch language to Spanish. **Expect**: the Identity warnings are in Spanish. For another language they appear in English.

## 3. Find funds (User Story 2)

1. From the success panel or history, note a site's Digi-ID address.
2. Send a small amount to it and wait for 1 confirmation. Send a second small amount and leave it unconfirmed.
3. Open the Identity page, section **Recover funds**, tap **Check for funds**.
4. **Expect**: that site is listed with the confirmed amount and the unconfirmed amount shown as pending, plus a total.
5. Lock the wallet and reopen the section. **Expect**: an "unlock first" state, with no lookup made (check the network panel).
6. Turn off the network and run the check. **Expect**: an error with a Retry, never "No funds found".
7. Open the browser network panel during a check of several sites. **Expect**: one request per address, none carrying several Digi-ID addresses.
8. Clear the login history, then type the site's domain in the field (try `Example.com`, then a full `https://` URL). **Expect**: the same address and funds are found.
9. With the network panel open, load the Identity page and open the **Recover funds** section without tapping check. **Expect**: no request carrying a Digi-ID address until you tap **Check for funds** (FR-014).
10. Time a check of a wallet with up to 50 sites, from tapping **Check for funds** to the result. **Expect**: under 30 seconds (SC-002).

## 4. Sweep (User Story 3)

1. With funds found on two different sites, select both and tap **Sweep selected**.
2. **Expect** on the review: for each address the amount, estimated fee, net and a **different** destination, then totals. Cancel. **Expect**: nothing sent, and your receive address on the Receive page is unchanged.
3. Sweep again and confirm. **Expect**: two separate transactions (check an explorer: each has one input address and one output), and each status moves Sending to Pending. The Receive page now shows an address that is not one of the swept destinations.
4. Reload the page mid-way. **Expect**: the section still shows both sweeps and their status.
5. After 1 confirmation. **Expect**: status Confirmed, funds in the normal balance, two entries in transaction history, and the Digi-ID addresses empty on a re-check.
6. Send an amount smaller than the fee to a Digi-ID address and re-check. **Expect**: marked "too small to sweep", and nothing is sent.
7. Double click **Confirm**. **Expect**: only one transaction per address.
8. Time the sweep from tapping **Confirm** to every item showing Pending. **Expect**: under 2 minutes (SC-003). Compare each fee paid with the estimate on the review. **Expect**: within 10% (SC-004).
9. Stay on the section while a sweep is pending. **Expect**: the status changes to Confirmed on its own within about 30 seconds of the confirmation, or on tapping **Refresh**. Close the section. **Expect**: no further requests in the network panel.
10. Lock the wallet during a multi-address sweep, using a short auto-lock. **Expect**: unsent items show "Failed: wallet locked" and can be swept again after unlocking.

## 5. Docs

- `CHANGELOG.md` has an entry under Unreleased.
- `docs/PROCESS_FLOWS.md` describes the recovery flow.
- Help has the new entry.
