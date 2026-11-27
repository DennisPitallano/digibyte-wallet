# Quickstart: validating spec 035

## Automated

```powershell
dotnet test tests/DigiByte.Crypto.Tests --filter DigiDollarAccountKey
dotnet test tests/DigiByte.Wallet.Tests --filter "DigiDollarKeyExport|DigiDollarDeepScan"
dotnet test tests/DigiByte.Pay.Api.Tests
dotnet test tests/DigiByte.Pay.Web.Tests
cd tests/DigiByte.Pay.E2E; $env:PW_CHANNEL='msedge'; npx playwright test
```

Expected results:

- **Crypto**: public derivation equals seed derivation on mainnet and testnet. Every refusal reason in [stores-api S1](contracts/stores-api.md) is covered.
- **Pay.Api**:
  - Stores sharing a key never get the same address.
  - Payments are watched from fake explorer history.
  - DD payments wait through an explorer outage and catch up afterwards.
  - No payout routes exist.
- **E2E**: the POS DD switch shows a real `TD…` address derived from the dev store's key.

## Manual, on testnet

1. In the wallet (testnet), open **Settings → DigiDollar key for DigiPay**. Copy the key and note the first address ([W1](contracts/wallet-dd-key.md)).
2. In DigiPay, on a testnet store, paste the key into the DigiDollar card. The preview shows the same first address. Confirm and save ([S1, S2](contracts/stores-api.md)).
3. Paste a mainnet key, a DGB `tpub` and a `tprv` into the card. They're refused as `wrong_network`, as a first-address mismatch the merchant won't confirm, and as `private_key`, which is never echoed back.
4. Turn DigiDollar on. Create a $5.00 DD payment and pay it from a second testnet wallet. It goes to seen in under a minute, then to paid and confirmed at 6 confirmations (SC-004).
5. The DD appears in the merchant's own wallet. There's no payouts section anywhere in the Dashboard.
6. Add the same key to a second store, and create a DD payment on each. The two addresses differ.
7. Block `digiexplorer.info` (for example in a hosts file). Checkout shows DD as temporarily unavailable, and DGB payments still work. Pay a DD payment made before the block, then unblock. The payment shows as paid within 5 minutes (SC-005).
8. Remove the key. DigiDollar turns off for the store, and an open DD payment still completes.
9. Search the Dashboard, the docs and the API reference for "payout", "holds your DigiDollar" and "operator". Nothing about DD custody remains (SC-006).
