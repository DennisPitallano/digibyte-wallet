# Contracts: First-Receive Flow

The wallet exposes no new network interface. These are the internal contracts between the wallet library, the web services and the pages, plus the UI contract of the new components. Names are final; signatures show intent and may gain parameters during implementation.

## Wallet library (`DigiByte.Wallet.Services`), pure and unit-tested

### `FirstReceiveRule`

```text
enum FirstReceiveState { Undecided, Empty, History, Celebrated }
enum HistorySource   { CompleteLoad, PartialAddressLoad, IncompleteLoad, SavedSnapshot }

record FirstReceiveOutcome(FirstReceiveState State, FirstPayment? Payment)
record FirstPayment(decimal AmountDgb, bool IsPending, IReadOnlyList<string> TxIds)

static FirstReceiveOutcome Evaluate(FirstReceiveState current,
                                    IReadOnlyList<TransactionRecord> transactions,
                                    HistorySource source)
```

- Implements the transition table in [data-model.md](../data-model.md).
- `Payment` is non-null only for `Empty` + `CompleteLoad` + at least one DGB receipt; `State` stays `Empty` in that case.
- `PartialAddressLoad` (the chain was read, but the wallet has not finished finding its used addresses) behaves like `CompleteLoad` except that it never moves `Undecided` to `Empty`.
- Never returns `Celebrated`; that move belongs to the store, when the overlay is shown.

### `FirstReceiveStore`

```text
Task<FirstReceiveState> GetAsync(string walletId)
Task SetAsync(string walletId, FirstReceiveState state)      // Undecided is not written
Task DeleteAsync(string walletId)
Task<bool> IsFirstSessionEndedAsync()
Task EndFirstSessionAsync()                                   // idempotent
static string StateKey(string walletId)                       // "firstReceive:{walletId}"
```

- An unreadable or unknown stored value reads as `Undecided`.
- `SetAsync` never moves a wallet out of `History` or `Celebrated`.

### `NewArrivals`

```text
static IReadOnlyList<TransactionRecord> Find(ISet<string>? known,
                                             IReadOnlyList<TransactionRecord> transactions,
                                             DateTime utcNow)
```

- Home's existing rule, unchanged: `known == null` returns nothing (first list); otherwise received transactions not in `known` that have 2 or fewer confirmations or are newer than 15 minutes.

### `RefreshPacer`

```text
bool ShouldCheck()            // call once per 15 s tick; false while counting down
void Report(bool changed)     // after a check: resets or lengthens the interval (15 → 30 → 60 s)
void Reset()                  // manual refresh
```

### `WalletService`

```text
record HistoryLoad(List<TransactionRecord> Transactions, bool Complete, bool AddressesSettled)
Task<HistoryLoad> LoadTransactionHistoryAsync(int skip = 0, int take = 50)
```

- `Complete` and `AddressesSettled` belong to that call. Overlapping calls each get their own values.
- `AddressesSettled`: true when the session's address discovery has completed successfully, or the session has no HD key (private-key wallet); false while discovery is running, not started or failed. Seed and watch-only wallets both follow it.
- `GetTransactionHistoryAsync` and `LastHistoryIncomplete` keep working for existing callers; first-receive code must not read `LastHistoryIncomplete`.
- `DeleteWalletAsync` additionally removes `FirstReceiveStore.StateKey(walletId)`.

## Web services (`DigiByte.Web.Services`)

`FirstReceiveCoordinator` and `InstallAppState` are scoped (one for the app). `IncomingPaymentAnnouncer` and `AutoRefreshLoop` are created by each page that uses them and end with it.

### `FirstReceiveCoordinator`

```text
Task InitializeAsync()                                   // loads the marker; snapshot pass if absent
Task MarkCreatedEmptyAsync(string walletId)              // called by CreateWallet.razor only
Task<FirstPayment?> ObserveAsync(string walletId,
                                 IReadOnlyList<TransactionRecord> transactions,
                                 HistorySource source)
FirstPayment? Pending(string walletId)                   // celebration waiting to be shown
Task MarkShownAsync(string walletId)                     // state → Celebrated, clears pending
bool FirstSessionEnded { get; }
bool OtherDialogOpen { get; set; }                       // set by MainLayout for "What's New"
event Action? Changed                                    // pending or marker changed
```

- `ObserveAsync` is the only entry point pages use after a history load. It applies `FirstReceiveRule`, saves a changed state, ends the first session when `transactions` is not empty, and returns the payment when one became pending in this call.
- Calls for a wallet are serialised so two pages loading at once cannot both detect the first payment.

### `IncomingPaymentAnnouncer`

```text
void Seed(string walletId, IReadOnlyList<TransactionRecord> saved)   // the saved history is already seen
void Forget()                                                        // wallet switch
Task AnnounceAsync(string walletId, IReadOnlyList<TransactionRecord> transactions,
                   bool hideAmounts, IReadOnlyCollection<string>? celebratedTxIds)
```

- Created by the page (`new IncomingPaymentAnnouncer(Notifications, JS)`); not registered for injection.

- Transient: one per page that checks (Home, Receive). Its seen-ID list starts empty when the page opens and is cleared when `walletId` changes, so the first list after opening a page or switching wallet is recorded without announcing, exactly as Home behaves today.
- Shows the existing success toast and haptic for `NewArrivals.Find(...)`, excluding `celebratedTxIds`.
- A payment is announced at most once: on the page that was open when it arrived. Any other page records it silently in its first list.

### `AutoRefreshLoop`

```text
void Start(Func<Task> check, Func<bool>? isBusy = null)
void Report(bool changed)            // the page calls this after every refresh, automatic or manual
void NotifyManualRefresh()
Task StopAsync()
```

- 15 s timer; skips a tick when a check is running, when `isBusy` says the page is already refreshing, when `domHelpers.isHidden` is true, or when `RefreshPacer.ShouldCheck()` is false. Exceptions from `check` are swallowed.
- Created by the page (`new AutoRefreshLoop(JS)`) and stopped when the page is left; not registered for injection.

### `InstallAppState`

- `InstallMode { Hidden, Native, IosSteps, MenuSteps }` and `ComputeInstallMode(...)`, moved unchanged from `Settings.razor`; `Task<InstallMode> GetModeAsync()` reads `pwaInstall.getState`.

## Page obligations

| Page | Must do |
|---|---|
| `CreateWallet.razor` | After a successful create: `MarkCreatedEmptyAsync(wallet.Id)`. |
| `Home.razor` | Saved snapshot → `ObserveAsync(..., SavedSnapshot)`. Each network history load uses `LoadTransactionHistoryAsync` → `ObserveAsync` with `IncompleteLoad` (threw, or `Complete` false), `PartialAddressLoad` (`Complete` true, `AddressesSettled` false) or `CompleteLoad`, then `AnnounceAsync`. Use `AutoRefreshLoop`. Render `ReceiveEmptyState` when history is known empty; keep the "Unable to load data" card when the fetch failed. Render `FirstReceiveCelebration`. Show `SponsorsStrip` only when `FirstSessionEnded`. No `OnboardingWalkthrough`. |
| `Receive.razor` | Same observe and announce calls in `LoadHistoryAsync`, plus one check on open for private-key wallets (which that method skips); start an `AutoRefreshLoop` on open, stop it on leave; render `FirstReceiveCelebration`. A failed check changes nothing on screen. |
| `Transactions.razor` | Same observe call in `LoadAsync` (no automatic loop); **Receive** button in the empty state when `all.Count == 0` and not failed; render `FirstReceiveCelebration`. |
| `Settings.razor` | Use `InstallAppState`; open the steps dialog when the URL has `?install=1`. |
| `MainLayout.razor` | Call `InitializeAsync` once; mirror `showVersionInfo` into `OtherDialogOpen`. |
| `PwaInstallBanner.razor` | Show only when existing rules pass and (always-show page or `FirstSessionEnded`). |

## UI contract

### `ReceiveEmptyState` (Home)

- **Inputs**: `Address`.
- **Shows**: the QR code for `Address` (alt text names it as the receiving address), the full address as selectable text, a **Copy address** button, a **Receive** link to `/receive`.
- **Behaviour**: Copy puts the full address on the clipboard and confirms with a toast; if the browser refuses the clipboard it shows a notice instead and the page keeps working. Renders with no network. Renders nothing while `Address` is empty. Home also shows it under the "Unable to load data" card.
- **Text**: from `home.empty.*` translation keys. No explanatory sentence of its own; that is the mascot's `home.empty` message, rendered by Home above it.

### `FirstReceiveCelebration` (Home, Receive, Activity)

- **Inputs**: `Blocked` (the page has a dialog open).
- **Appears when**: the coordinator has a pending payment for the active wallet, `Blocked` is false and `OtherDialogOpen` is false. On appearing it calls `MarkShownAsync`.
- **Shows**: a heading; the amount in DGB and, when a price is known, in the user's currency, or no amount when balances are hidden; "on its way" wording when `IsPending`, otherwise "received"; the mascot's line only in Full mascot mode; **Done**; **Install app** when `InstallAppState` mode is not `Hidden`.
- **Behaviour**: `role="dialog"`, `aria-modal`, labelled by its heading, focus moves to **Done**, Escape and **Done** close it, focus returns to the page. Moving decoration is omitted under `prefers-reduced-motion`. **Install app** closes the overlay, then prompts (Native) or goes to `/settings?install=1` (steps modes). Leaving the page or locking closes it.
- **Text**: from `firstreceive.*` translation keys, English and Spanish; the mascot line is English only.

### Activity empty state

- When the wallet has no transactions and the load did not fail: the existing "No transactions yet" text plus a **Receive** button (`activity.empty.receive`) that opens `/receive`. Not shown for "nothing matches" states.
