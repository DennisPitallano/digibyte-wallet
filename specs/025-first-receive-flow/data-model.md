# Data Model: First-Receive Flow

Nothing here is secret, and nothing leaves the device. All values are stored through `ISecureStorage`, like the wallet's other per-device preferences.

## First-receive state (per wallet)

- **Key**: `firstReceive:{walletId}`
- **Values**: `empty` | `history` | `celebrated`. No key means *undecided*.
- **Covers** the spec's "first-load record" and "celebration record" (research R3).

| State | Meaning | Can celebrate? |
|---|---|---|
| *undecided* | Nothing is known yet about this wallet on this device | No |
| `empty` | The wallet started empty on this device | Yes, once |
| `history` | The wallet already had history, or its first transaction was not a DGB receipt | Never |
| `celebrated` | The first-payment celebration has been shown | Never again |

### Transitions

| From | Event | To | Effect |
|---|---|---|---|
| *undecided* | Wallet created on this device from a new phrase | `empty` | none |
| *undecided* | Complete load, no transactions, addresses settled | `empty` | none |
| *undecided* | Complete load, no transactions, addresses not yet settled | *undecided* | none |
| *undecided* | Complete load or saved snapshot, has transactions | `history` | none |
| *undecided* | Failed or incomplete load, or snapshot without transactions | *undecided* | none |
| `empty` | Complete load, no transactions | `empty` | none |
| `empty` | Complete load with one or more DGB receipts | `empty` | celebration becomes pending (amount = sum of receipts; pending = all receipts unconfirmed) |
| `empty` | Complete load with transactions, none a DGB receipt | `history` | none |
| `empty` | Failed or incomplete load | `empty` | none |
| `empty` | Pending celebration is shown on screen | `celebrated` | none |
| `history`, `celebrated` | anything | unchanged | none |
| any | Wallet deleted | *removed* | key deleted |

Rules:

- A saved snapshot can only cause *undecided* → `history`.
- "Addresses settled" means the wallet has finished finding which of its addresses have been used (always true for a private-key wallet). It matters only for *undecided* → `empty`; a first payment to an `empty` wallet is detected whether or not addresses are settled.
- Whether a load was complete comes from that load's own result, never from shared state.
- Seed and watch-only wallets both have address discovery and both wait for it; a private-key wallet has one fixed address and is settled from the start.
- Accepted consequence: a wallet that was not created on this device and is paid while still *undecided* becomes `history` and never celebrates (spec Edge Cases).
- On the regtest developer network the wallet reads no chain history at all and returns only the transactions this device tracked, without reporting the load as incomplete. A recovered wallet there can therefore be recorded as `empty` and celebrate later. Accepted: regtest is a local developer network with no real users; mainnet and testnet are unaffected.
- The move to `celebrated` happens when the overlay is shown, not when the payment is detected (research R6).
- A "DGB receipt" is a transaction with direction Received and an amount above zero.

## First-session marker (per device)

- **Key**: `firstSession:ended`
- **Value**: `1` when set; absent otherwise.
- **Set when**: any wallet's history, saved or freshly loaded, is seen to contain a transaction; or at startup, when the marker is absent and any wallet's saved snapshot contains a transaction.
- **Never cleared**, including when wallets are deleted (FR-029).
- **Read as**: ended / not ended. Before the startup check finishes it reads as not ended.

## Pending celebration (in memory only)

Held by the coordinator between detection and display; lost on reload, in which case the next complete load detects the payment again because the state is still `empty`.

| Field | Meaning |
|---|---|
| WalletId | The wallet the payment belongs to |
| AmountDgb | Sum of the DGB receipts in the detecting load |
| IsPending | True when every receipt in that load is unconfirmed |
| TxIds | The receipts' transaction IDs, used to suppress the ordinary notice for them |

## Known arrivals (in memory only)

Per open page (Home or Receive), the set of transaction IDs already seen for the active wallet (research R7). It starts empty when the page opens and is cleared on a wallet switch; the first list after either is recorded without announcing anything, as Home does today. It is not shared between pages and does not survive leaving the page.

## Refresh pacing (in memory only)

The state Home's poller keeps today, now shared with Receive (research R8): the count of unchanged checks in a row, and ticks left until the next check. Base tick 15 seconds; interval 15 s, then 30 s, then 60 s while nothing changes; back to 15 s on any change or manual refresh; no check while the tab is hidden.

## Existing data touched

- `WalletService.DeleteWalletAsync` also removes `firstReceive:{walletId}`.
- `BakuCatalog`: the `home.empty` entry's title and body change. Its ID does not, so an earlier dismissal still applies.
- `wwwroot/locales/en.json`, `es.json`: new keys under `firstreceive.*`, `home.empty.*`, `activity.empty.*`.
- No change to wallet records, encrypted secrets, snapshots, address summaries or the transaction tracker.
