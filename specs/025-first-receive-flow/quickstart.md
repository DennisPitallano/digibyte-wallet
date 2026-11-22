# Quickstart: First-Receive Flow

How to check the feature works. Details are in [contracts/first-receive.md](contracts/first-receive.md) and [data-model.md](data-model.md).

## 1. Automated tests

```powershell
dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName~FirstReceive|FullyQualifiedName~HistoryLoad|FullyQualifiedName~NewArrivals|FullyQualifiedName~RefreshPacer"
```

| Scenario | Expected |
|----------|----------|
| Undecided wallet, complete load with no transactions | state `empty`, no payment |
| Undecided wallet, complete load with transactions | state `history`, no payment |
| Undecided wallet, incomplete load (empty or not) | still undecided |
| Undecided wallet, chain read but addresses not yet settled, no transactions | still undecided |
| Two overlapping history loads, one failing | each caller sees its own result; the failed one is never reported complete |
| Undecided wallet, saved snapshot with transactions | state `history` |
| Undecided wallet, saved snapshot with none | still undecided |
| `empty` wallet, complete load with one unconfirmed receipt | payment, pending, that amount; state still `empty` |
| `empty` wallet, complete load with two receipts, one confirmed | one payment, total of both, not pending |
| `empty` wallet, complete load with a receipt confirmed days ago | payment (age does not matter) |
| `empty` wallet, complete load whose only transaction is not a DGB receipt | state `history`, no payment |
| `empty` wallet, incomplete load containing a receipt | no payment, still `empty` |
| `history` or `celebrated` wallet, complete load with a new receipt | no payment, state unchanged |
| Store: unreadable stored value | reads as undecided |
| Store: set `empty` on a `history` or `celebrated` wallet | unchanged |
| Deleting a wallet | its `firstReceive:` key is gone; the first-session marker stays |
| New arrivals: first list for a wallet | nothing announced |
| New arrivals: a transaction already in the known set | not returned again |
| Pacer: unchanged checks | intervals of 15 s, 30 s, 60 s, 60 s; back to 15 s after a change or manual refresh |

The whole wallet suite must still pass:

```powershell
dotnet test tests/DigiByte.Wallet.Tests
```

## 2. In the app (testnet)

Start the wallet with the `digibyte-web` preview configuration (http://localhost:5251) on testnet, with a second funded wallet to pay from.

### New wallet, empty Home (Story 1, Story 4)

1. Create a wallet. On Home: no introduction slides; the QR code, full address, **Copy address** and **Receive** link are where the list will be; the mascot's message points at them; no install banner; no sponsors strip; the checklist is shown.
2. Tap **Copy address**: the clipboard holds the full address and a "copied" notice shows.
3. Open Receive and Activity: no install banner on either. Activity shows "No transactions yet" with a **Receive** button.
4. Set the mascot to Off in Settings and return to Home: the message is gone and the empty state is still clear.
5. Turn the network off and reload Home: the QR code and address still show.

### First payment on Receive (Story 2)

6. With Receive open on the new wallet, send it a payment from the other wallet. Without touching anything, within one check the celebration appears: amount, "on its way", **Done**, and **Install app** when the wallet is not installed.
7. Tap **Done**: you are still on Receive. Go to Home: the payment is in the list, no second celebration, and no "payment received" toast for it.
8. The install banner and sponsors strip now follow their usual rules.
9. Send a second payment with Receive open: the ordinary "payment received" toast shows once; no celebration. Go to Home: no second toast for it. Lock, send another payment, unlock: no toast for it (unchanged from today).

### First payment while closed

10. Create another new wallet, copy its address, close the tab, pay it, wait for a few confirmations, reopen and unlock. Home shows the celebration with "received" wording.

### Variations

11. Hidden balance on: the celebration shows no amount.
12. Reduced motion on (OS setting, or the browser's rendering emulation): no moving decoration.
13. Mascot Off or Guardian only: no mascot image or line; heading and amount still show.
14. **Install app** in a browser with an install prompt: the prompt opens. On iOS or a browser without one: Settings opens with the steps dialog.
15. New wallet created with the network off, paid before the first successful load: the celebration still appears once the wallet loads.

### No false celebration (Story 3)

16. Recover a wallet that has history: no celebration on first load, and none when a new payment arrives.
17. Recover a wallet that is really empty, then pay it: the celebration appears.
18. Import a watch-only wallet with history: no celebration.
19. Update an existing install that has a funded wallet: no celebration, and the install banner and sponsors strip show as before on the first page opened.

### Several wallets

20. With a funded wallet, add a new empty one and switch between them: the banner and sponsors strip do not come and go. The empty wallet still gets its own celebration on its first payment.

## 3. Load check for Receive's automatic check (SC-008, FR-024)

On `/dev/node` (developer view), note the request counter, leave Receive open on a quiet wallet for 5 minutes, and compare with Home left open for 5 minutes: the totals should be about the same, and a hidden tab should add none.

## 4. Spanish

In the browser console run `localStorage.setItem('dgb-locale','es')` and reload: the empty state's buttons, Activity's **Receive** button and the celebration's heading, wording and buttons are in Spanish. The mascot's lines stay in English. Remove the key afterwards.
