# Quickstart: validating Wallet Addresses in Activity

## Automated

```bash
dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName~ActivityAddress|FullyQualifiedName~TransactionHistoryAddress"
dotnet build
```

Expected: the new tests pass, the build succeeds, and the existing wallet tests still pass (`dotnet test tests/DigiByte.Wallet.Tests`).

## Manual (testnet or regtest wallet, HD wallet)

1. Open Receive, copy the first address, tap "Generate new address", copy the second. Optionally label them (for example "Salary", "Donations").
2. Send a small amount to each address from another wallet; wait for them to appear.
3. Open the activity page. Each received row shows its own address (or label), not the sender's address. Matches FR-001/FR-002.
4. Open a row. The details list the full address with a copy action (FR-003).
5. Open the address picker in the filter bar. Both addresses are listed with a count of 1 each; an address you generated but never used is not listed (FR-004).
6. Pick the first address. Only its transaction remains; combine with "Received", then clear it (FR-005, FR-006).
7. Type part of one of your addresses, or part of its label, in the search box. Matching transactions remain (FR-009).
8. Send from the wallet to one of its own other addresses. One row appears; it matches the filter for either address (clarified self-transfer rule).
9. Turn off the network and reload. History from storage still shows; rows without known addresses show none and nothing errors.

Expected outcomes follow the success criteria in [spec.md](spec.md); the data details are in [data-model.md](data-model.md) and the helper contract in [contracts/activity-addresses-ui.md](contracts/activity-addresses-ui.md).

## Added steps (remember and rename)

10. Choose an address in the picker, open another tab such as Wallet, and come back to Activity. The same address is still chosen (FR-011). Clear it, leave and return: no address is chosen.
11. Open the picker and use the rename control on an address: type a name and save. The name shows in the picker, on the rows for that address, and search finds it. Change it, then empty it to remove it (FR-012).
