# Contract: wallet DigiDollar key screen and deeper DD scan

## W1. `/settings/digidollar-key` (DigiByte.Web)

The page is reached from Settings, next to "Extended public key (xpub)".

| State | Shows |
|---|---|
| Seed wallet, unlocked | The account key (`xpub…`/`tpub…`) in a monospace box, with **Copy**. The first DD receive address (`0/0`). The text below. A link to "Look further for DigiDollar" (W3). |
| Watch-only or multisig wallet | "Your DigiDollar key is only available in a wallet restored from its recovery phrase." No key. |
| Locked | The usual unlock prompt. |

**Text (FR-022)**: "Paste this into DigiPay to take DigiDollar payments straight into this wallet. It lets DigiPay make receive addresses and see payments to them, including all DigiDollar in this wallet's DigiDollar account. It can't spend. Check that DigiPay shows the same first address as below."

## W2. Wallet service API

- `DigiDollarKeyDerivation.GetAccountExtPubKey()` returns the `ExtPubKey` at `m/86'/coin'/0'`.
- `WalletService.GetDigiDollarAccountKeyAsync()` returns `(string Key, string FirstAddress)?`, or null for wallets that aren't seed wallets.
- `DigiDollarAccountKey.Parse(key, network).Address(0)` must equal `FirstAddress`, which is tested.

## W3. Deeper DD scan

- `DigiDollarService.ScanAsync(…, int receiveGapLimit = 20)`. The deep scan passes 500.
- On `/recovery`, a "Look further for DigiDollar" card runs the deep scan. It shows progress and then "Found DigiDollar on N more addresses" or "Nothing more found". Addresses it finds in use are kept, so normal scans start from them.
- The DGB receive search on the same page is unchanged.
