# Quickstart: validating Your Addresses on the Receive Page

## Automated

```bash
dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName~ReceiveAddressList"
dotnet build
dotnet test tests/DigiByte.Wallet.Tests
```

Expected: the new tests pass, the build succeeds, and the existing wallet tests still pass. The live-explorer benchmark tests need the network and fail without it, as they do on `main`.

## Manual (HD testnet or regtest wallet; use the preview)

1. Open Receive on a wallet with one address. A folded "Your addresses (1)" row shows under the QR card; open it and the list shows that one address, marked as on show (story 1, scenario 5).
2. Choose "Generate new address" three times. Each new address appears on top and is shown (story 4).
3. Choose an older address in the list. The QR code, the address, copy, share and download use it. Enter an amount first: it stays (story 1).
4. Name an address in the list, then check the name box under the QR code and the Activity address picker show it. Rename it in Activity and see it on Receive. Name the address on show with the box under the QR code and see the list update (story 3, FR-005).
5. Pay an address (or use a wallet with history), open Receive: that address shows as used with its count; an untouched one shows as unused (story 2).
6. Open Receive with no network and no cached history: addresses are listed and every entry shows a muted "Status unknown" label, none shown as unused; once the history loads the labels are replaced by the real status.
7. On a used address choose "View activity": Activity opens filtered to it. Unused addresses have no such action (story 5).
8. With more than 10 addresses, "Show more" adds 10 until all are listed, then goes away (story 1, scenario 2).
9. Switch to an older address, go to another tab and come back: the newest address is on show again (FR-012).
10. Reach the limit of unused addresses in a row and press Generate: the existing message appears and the list is unchanged (story 4, scenario 2).
11. Open Receive with a single-key (WIF) wallet: no list, the legacy/SegWit switch as before (FR-009).
12. Check phone width (375 px): no sideways scrolling and the list is usable.
13. Count the taps from opening Receive to showing an older address in the QR code: two for the newest 10, three after "Show more" (SC-001).
14. Open Receive with a wallet that has only one generated address: the list shows that one address and nothing looks broken.
15. Open Receive with a watch-only (xpub) wallet, and a multisig wallet if one is available: the list shows and switching works, or Receive behaves exactly as it does today for that wallet type.
16. With a used address on show a short privacy notice appears under the QR code and copy, share and download still work at once; with an unused address, or when the status is unknown, no notice appears (FR-013).
17. Switch to an older address, reload the page and open Receive again: the newest address is on show and it is still the wallet's current receiving address (FR-011).
18. With a used address on show and the list folded, never opened: the privacy notice still appears (FR-013).

Expected outcomes follow the success criteria in [spec.md](spec.md); the data shapes are in [data-model.md](data-model.md) and the helper contract in [contracts/receive-address-list-ui.md](contracts/receive-address-list-ui.md).
