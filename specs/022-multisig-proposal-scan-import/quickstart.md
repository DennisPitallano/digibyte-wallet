# Quickstart: Validate Receive Proposal

## Automated

```powershell
dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName~MultisigProposalReceive"
dotnet test tests/DigiByte.Wallet.Tests
dotnet build
```

Expected: new tests cover added / merged / already-up-to-date / cancelled / closed, restore, wrong-wallet lookup, foreign signer, malformed input, spent-coin set, and the old-record default for `ReceivedFromCoSigner`; the existing suite stays green.

## Risk check first — dense QR on a real phone (task T008a)

Show a near-limit (~2,200 character) proposal QR on one screen and scan it with the in-app scanner on a phone. It must decode within a few seconds. If not, follow the contingencies in [research.md](research.md) R9a before continuing.

## Manual — two devices (or two browser profiles) with the same 2-of-3 multi-sig

1. **Device A**: create a payment on the multi-sig; on the Pending card choose Share — a QR appears.
2. **Device B**: open the same multi-sig → Pending → **Scan proposal**; scan A's QR.
   - Expect "Proposal added", the card highlighted with "Received from a co-signer", the correct recipient/amount/fee, signature progress matching A. (SC-001: under 30 s.)
3. Scan the same QR again → "Already up to date"; the list still has one entry. (SC-003)
4. **A** signs, shares again; **B** scans → "1 new signature added".
5. **Paste/file**: on A choose Copy PSBT and Save as file; on B use Paste and Choose file with each → same outcome as scanning.
6. **Refusals** (nothing added each time): scan an address QR; scan a different multi-sig's proposal (offer to open it if B has that wallet); scan garbage text.
7. **Cancel/restore**: on B cancel the proposal, scan again → "You cancelled this proposal" → **Restore this proposal** brings it back.
8. **Spent coins**: spend the same coins elsewhere, reload Pending → the card shows "Can't be signed – coins already spent" with Sign disabled and Cancel available.
9. **Camera denied**: deny permission → scanner explains; Paste and Choose file still work.

Preview check (dev server): confirm the Pending page renders the panel without console errors at phone width (375 px) and in dark mode.
