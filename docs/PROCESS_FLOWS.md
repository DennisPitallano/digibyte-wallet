# Process Flows

## For Users (Plain Language)

### Creating a New Wallet
1. Open the app → tap **"Create New Wallet"**
2. The app generates 24 secret words (your recovery phrase)
3. **Write them down on paper** — this is your backup
4. Confirm 3 random words to prove you wrote them down
5. Set a 6-digit PIN to lock/unlock the app
6. Done! Your wallet is ready to receive DigiByte

### Receiving DigiByte
1. Open the app → tap **"Receive"**
2. Show the QR code to the sender, or copy the address
3. Choose **Legacy** (D...) or **SegWit** (dgb1...) format
4. Wait for the sender to send — it confirms in ~15 seconds

### Sending DigiByte
1. Tap **"Send"** → enter the recipient's address (or scan QR, or pick a contact)
2. Enter the amount (tap the currency badge to switch between USD and DGB)
3. Tap **"Review Transaction"** → confirm the details
4. The app builds and signs the transaction in your browser, then broadcasts it
5. Green checkmark = sent!

### Importing from Another Wallet (Private Key)
1. Export your private key from the old wallet (starts with K, L, or 5)
2. Open our app → **"Recover Existing Wallet"** → **"Private Key"** tab
3. Paste the key (or scan its QR code)
4. Set a PIN → the app imports the key and shows your address + history

### Forgot Your PIN
1. On the unlock screen, tap **"Forgot PIN?"**
2. Choose **"Reset PIN with recovery phrase"**
3. Enter your 24 words → set a new PIN → you're back in

---

## Blockchain Backend Flows

### Read Operations (Balance, UTXOs, Tx History, Fees, Block Height)
```
Esplora (digiexplorer.info)
  ↓ failed? (2-min cooldown)
Own Pruned Node (Railway, scantxoutset)
  ↓ failed?
Development → Mock (fake demo data)
Production  → Error (InvalidOperationException)
```

### Write Operations (Broadcast Transaction)
```
Own Pruned Node (sendrawtransaction — most reliable)
  ↓ failed?
Esplora (digiexplorer.info, POST /tx)
  ↓ failed?
Error — "Broadcast failed on all backends"
```
Broadcast **never** falls back to mock, even in development.

### Balance and history refresh (spec 023)
```
GetBalanceAsync / GetTransactionHistoryAsync (they share one refresh cycle)
  → gateway answering history? → one pass over the gateway record (history only)
  → change-only sync on and summaries available?
      1. one summary per watched address (reused if read in the last 30 s, e.g. by discovery); any failure fails the balance
      2. balance = Σ confirmed, pending = Σ (pending received − pending spent)
      3. history pages only for addresses whose summary changed (newest page, then older until a saved one); merge by txid
      4. confirmations from each block height and one tip read; save history, then the summaries of addresses that merged
      5. first history result of the unlock → background full re-read (2 at a time, waits for foreground refreshes)
  → otherwise: every address's balance and full history, as before
      tiered refresh (spec 024): step 1 reads only the hot addresses (30 s reuse) and a time-proportional share of the oldest cold
      ones (5 min reuse); unlock, a send and a user-started refresh (Home button or pull, Activity pull) read every address
After a send: Fallback cache invalidated (in-flight reads included) + summary snapshot dropped + page snapshot cleared by wallet id
Send, batch send, consolidate (spec 024): coins read fresh from addresses whose summary (< 5 min old) shows funds or pending,
  then the rest if those can't cover the amount + 0.01 DGB; no such summaries → every address, as before
```

### Light-client gateway flows (optional)

**Read (balance, history).** With the gateway ready and answering: sync headers from the checkpoint and check them (and, every two minutes, compare them with
a public explorer) -> refresh the wallet record from the last synced height minus 6, sending the wallet's addresses and its unspent outpoints in requests of at
most 100 -> match every returned transaction to the wallet's own header at its height -> rebuild the part of the record that changed -> answer from the
record. Any step that cannot be completed correctly makes the gateway decline and the next source answer.

**Send.** The explorers or node carry the send as before. When the gateway carries it, it relays to at least 4 peers and reports accepted, rejected (with
the reason) or no response (sent, not yet confirmed by peers). In every case the wallet records the send at once: its inputs leave the available list and
its change is spendable immediately while shown as pending.

**Restore.** Optional date -> birth height estimate (a day early) -> scan the blocks since then in chunks, extending the set of addresses by the gap limit and
re-reading earlier chunks for newly used addresses -> progress saved after each chunk -> one incremental refresh builds the wallet record. A history older
than the gateway keeps is restored through the explorers, with a message.

### Regtest Mode
```
Own Node (only backend available)
  ↓ failed?
Development → Mock
Production  → Error
```
No public explorers exist for regtest.

### What Each Operation Needs

| Operation | Blockchain Calls? | Mock (Dev)? | Mock (Prod)? |
|-----------|-------------------|-------------|-------------|
| Create wallet | None (local) | N/A | N/A |
| Generate address | None (local) | N/A | N/A |
| Get balance | Read chain | Yes (fallback) | No (throws) |
| Get UTXOs | Read chain | Yes (fallback) | No (throws) |
| Build & sign tx | None (local, NBitcoin) | N/A | N/A |
| Broadcast tx | Write chain | No (throws) | No (throws) |
| Tx history | Read chain | Yes (fallback) | No (throws) |
| Fee estimation | Read chain | Yes (fallback) | No (throws) |
| Price (fiat) | CoinGecko direct | Falls back to 0 | Falls back to 0 |

---

## For Developers (Technical)

### Wallet Creation Flow
```
User clicks "Create"
  → MnemonicGenerator.Generate() creates 24 BIP39 words
  → User confirms 3 random words
  → User sets 6-digit PIN
  → ICryptoService.EncryptAsync(mnemonic, pin) → AES-256-GCM
  → WalletKeyStore.StoreSeedAsync(walletId, encryptedSeed)
  → HdKeyDerivation(mnemonic, network) → m/84'/20'/0'/0/0
  → Redirect to Home dashboard
```

### Send Transaction Flow
```
User enters address + amount
  → AddressValidator.IsValid(address) across all networks
  → User taps "Review" → Confirm modal
  → User taps "Send Now"
  → WalletService.SendAsync():
    1. Collect all HD addresses (20 receiving + 10 change)
    2. Fetch UTXOs via IBlockchainService.GetUtxosAsync()
    3. Match UTXOs to HD-derived private keys
    4. NBitcoin TransactionBuilder: build + sign tx
    5. IBlockchainService.BroadcastTransactionAsync(rawTx)
    6. TransactionTracker.RecordSendAsync() → save to IndexedDB
    7. Return txid → success animation
```

### WIF Import Flow
```
User pastes WIF key
  → PrivateKeyImporter.IsValidWif(wif) validates format
  → PrivateKeyImporter.DetectNetwork(wif) → mainnet/testnet/regtest
  → User sets PIN
  → ICryptoService.EncryptAsync(wif, pin) → AES-256-GCM
  → WalletKeyStore stores encrypted WIF
  → WalletInfo.WalletType = "privatekey", WifNetwork = detected
  → Single key stored in memory (no HD derivation)
  → Both Legacy + SegWit addresses scanned for balance
```

### PIN Reset Flow
```
User enters seed phrase on Unlock page
  → MnemonicGenerator.IsValid(seedPhrase) validates
  → User sets new PIN
  → WalletService.ResetPinAsync(walletId, seedPhrase, newPin):
    1. Re-encrypt seed with new PIN
    2. Overwrite in WalletKeyStore
  → Auto-unlock with new PIN
```

### Blockchain Data Flow
```
Balance/UTXO/History Request (reads):
  FallbackBlockchainService.TryRead()
    → Esplora (digiexplorer.info)                [2-min cooldown on failure]
    → Own pruned node (scantxoutset)             [last resort for reads]
    → Development: MockBlockchainService (demo data)
    → Production: InvalidOperationException

Transaction Broadcast (writes):
  FallbackBlockchainService.BroadcastTransactionAsync(rawTx)
    → Own node first (sendrawtransaction — most reliable)
    → Esplora (POST /tx)
    → Error — "Broadcast failed on all backends"
    → NEVER falls back to mock, even in development
```

---

## Per-Page Technical Flows

### Welcome (`/welcome`)
```
App start
  → AuthGuard: WalletExists?
    → No  → render Welcome page
    → Yes → redirect /unlock
  → User taps "Create New Wallet" → navigate /create-wallet
  → User taps "Recover"           → navigate /recover-wallet
  → Footer links: /help, /about, /roadmap, /analytics, /deployment
```
No services called. Pure navigation page.

### CreateWallet (`/create-wallet`)
```
Step 1 — Generate Mnemonic:
  OnInitialized()
    → MnemonicGenerator.Generate() → 24 BIP39 words
    → Display words in numbered 3-column grid
    → Warning: "Write these down on paper"
  User taps "I've Written It Down" → GoToConfirm()

Step 2 — Confirm 3 Random Words:
  → Pick 3 random indices from 24 words
  → For each: show "Word #X?" → user types answer
  → VerifyWord() checks match (case-insensitive)
  → All 3 correct → GoToSetPin()

Step 3 — Set PIN:
  → PinInput component (6 digits, masked)
  → User enters PIN → enters again to confirm
  → PINs must match
  → CreateWalletAsync():
    1. ICryptoService.EncryptAsync(mnemonic, pin) → AES-256-GCM ciphertext
    2. WalletKeyStore.StoreSeedAsync(walletId, encryptedSeed)
    3. HdKeyDerivation(mnemonic, network) → m/84'/20'/0'/0/0
    4. AppState.IsUnlocked = true
    5. Navigate to / (Home)
```

### RecoverWallet (`/recover-wallet`)
```
Tab 1 — Seed Phrase:
  Input modes (toggle):
    "Paste All" — masked textarea (CSS -webkit-text-security: disc)
      → Accepts space/comma/newline-separated words
      → Word counter shows "N / 12" or "N / 24"
    "Word by Word" — 3-column numbered grid of individual inputs
      → BIP39 auto-suggest dropdown (up to 6 prefix matches)
      → Keyboard navigation: Arrow Up/Down, Enter/Tab to accept, Escape to dismiss
      → Privacy mode: suggestions show first 2 chars + dots when hidden
      → Red border validation for non-BIP39 words (binary search)
      → Multi-word paste auto-splits across fields (space/comma/newline/tab)
      → Auto-expands to 24 words if >12 pasted
      → 12/24 word count toggle buttons
  Show/hide toggle (eye icon):
    → Textarea: CSS text-security mask ↔ plain text
    → Word inputs: type="password" ↔ type="text"
  Mode switch transfers data between Paste All ↔ Word by Word
  → ValidateMnemonic(): check word count, BIP39 checksum
  → Valid → proceed to PIN step

Tab 2 — Private Key (WIF):
  → Password input for WIF (starts with K, L, or 5)
  → QR scanner button → OnQrScanned()
  → ValidateWif():
    1. PrivateKeyImporter.IsValidWif(wif)
    2. PrivateKeyImporter.DetectNetwork(wif) → mainnet/testnet/regtest
    3. Generate address preview (Legacy + SegWit)
  → Valid → proceed to PIN step

PIN Step (same as CreateWallet Step 3):
  → RecoverAsync():
    1. Encrypt seed/WIF with PIN
    2. Store in WalletKeyStore
    3. WalletInfo.WalletType = "hd" or "privatekey"
    4. Navigate to / (Home)
```

### Unlock (`/unlock`)
```
Normal unlock:
  → PinInput (6 digits)
  → UnlockAsync():
    1. WalletService.UnlockAsync(pin)
    2. Decrypt seed/WIF from IndexedDB
    3. Derive keys into memory
    4. Success → navigate to /

Forgot PIN:
  → Expand "Forgot PIN?" section
  → Option 1: Reset PIN with recovery phrase
    1. Enter 24 words → VerifySeed() validates
    2. Set new PIN (6 digits, confirm)
    3. WalletService.ResetPinAsync(walletId, seed, newPin)
    4. Re-encrypt with new PIN → auto-unlock
  → Option 2: Delete wallet & start over
    1. Confirmation dialog ("This is permanent")
    2. WalletService.DeleteWalletAsync()
    3. Clear IndexedDB → navigate to /welcome
```

### Home (`/`)
```
OnInitializedAsync():
  → AuthGuard check (redirect if locked)
  → WalletSnapshotCache.LoadAsync(walletId) → saved balance + history shown at once
  → WalletService.GetBalanceAsync() → change-only sync or FallbackBlockchainService (see "Balance and history refresh")
    → main figure = confirmed DGB (and its fiat value); "Pending ±X DGB" line when the net pending amount is not zero
  → WalletService.GetTransactionsAsync() → recent tx list
  → ContactService.GetContactsAsync() → quick-send list
  → CoinGecko API → fiat price conversion

Pull-to-refresh (touch gesture):
  → OnTouchStart/Move/End()
  → Threshold reached → refresh balance + transactions

UI Sections:
  → Balance card (DGB + fiat) with network indicator
  → 5 action buttons: Send, Receive, Contacts, Scan, [advanced info]
  → Quick contacts horizontal scroll → SendToContact(addr) → /send?to=addr
  → Transaction list (Virtualize for infinite scroll)
  → OpenTxDetail(tx) → TransactionDetailModal

Confirmation poller (15-second timer):
  → Started after LoadTransactionsAsync() when any tx has Confirmations < 6
  → PollPendingConfirmationsAsync():
    1. Filter txs where Confirmations < 6
    2. For each: TransactionTracker.UpdateConfirmationsAsync(txId)
    3. Fetch current confirmation count from Esplora
    4. If count changed → update tx.Confirmations + persist to IndexedDB
    5. StateHasChanged() → ConfirmationBadge re-renders
  → Timer stops once all txs reach 6+ confirmations or page is disposed
```

### Send (`/send`)
```
Recipient entry:
  → Text input (address) or Select contact or Scan QR
  → QR scan → parse digibyte: URI (address, amount, label, message)
  → AddressValidator.IsValid(address) → validate across all networks

Amount entry:
  → Numeric input with DGB ↔ Fiat toggle (ToggleAmountMode)
  → Conversion display using CoinGecko rate
  → "Max" button → use full balance minus estimated fee

Fee selection (Advanced mode only):
  → Slow / Normal / Fast presets
  → Custom sat/vB input
  → FallbackBlockchainService.EstimateFeeAsync() for defaults

Review:
  → PreviewSend() → validate all fields
  → SpendingLimitService.CheckLimitAsync(walletId, sendAmount)
    → If hard block exceeded → set sendError, abort (no confirm modal)
    → If threshold warning → store spendingCheck for amber banner
  → Show confirmation modal
  → Modal shows: recipient, amount, fee, total
  → If spendingCheck.Warning → amber banner with limit message

Confirm:
  → ConfirmSend():
    1. WalletService.SendAsync(address, amount, feeRate):
       a. Collect all HD addresses (20 receiving + 10 change)
       b. FallbackBlockchainService.GetUtxosAsync(addresses) → explorer chain
       c. UtxoSelector picks UTXOs (largest-first)
       d. Match UTXOs to HD-derived private keys
       e. NBitcoin TransactionBuilder: build + sign
       f. FallbackBlockchainService.BroadcastTransactionAsync(rawHex) → node first
       g. TransactionTracker.RecordSendAsync() → IndexedDB
    2. Success → green checkmark animation → navigate to /
    3. Error → show error in modal
```

### Receive (`/receive`)
```
OnInitializedAsync():
  → Detect wallet type (HD vs WIF)
  → WalletService.GetReceiveAddressAsync(format)
  → GenerateQrCode(address) → QRCoder → Base64 PNG data URL

Address format toggle:
  → SetLegacy() / SetSegwit() → regenerate address + QR

Actions:
  → CopyAddress() → JS interop clipboard.writeText()
  → ShareAddress() → navigator.share() native API
  → GenerateNewAddress() → HD only: increment address index

Your addresses (HD only, folded on every visit, spec 012):
  → "Your addresses (N)" row; first open derives the newest 10 via GetReceivingAddressesAsync(count, startIndex),
    "Show more" adds 10 at a time; ReceiveAddressList builds the entries (pure, in DigiByte.Wallet)
  → Choosing an entry changes only what the page shows (QR, copy, share, download); the amount and note stay, nothing is
    saved and NextReceivingIndex is not touched. Receive always opens on the newest address
  → Status per entry: Used · N / Unused / Status unknown, from the saved history (WalletSnapshotCache,
    cache:transactions:<walletId>) at once, then one background GetTransactionHistoryAsync; an answer that couldn't reach
    the chain (WalletService.LastHistoryIncomplete) or carries no wallet-address data replaces nothing, so a status is never
    turned into "unused" by an outage
  → A used address on show shows a short privacy notice under the QR code (never blocks copy or share)
  → Names: AddressLabelService (the same labels as the Activity page); the pencil per entry and the "Label this address"
    box edit the same name
  → "View activity" (used addresses only): writes pref:activityAddress:<walletId> and navigates to /transactions

WIF wallets: fixed Legacy + SegWit addresses (no "generate new", no list)
HD wallets: can generate unlimited addresses from derivation path
```

### Contacts (`/contacts`)
```
OnInitializedAsync():
  → Check query param ?add=ADDRESS (pre-fill from TransactionDetailModal)
  → ContactService.GetContactsAsync() → load all

Search:
  → SearchContacts(query) → filter by name or address substring

Add/Edit:
  → ShowAddForm() or EditContact(contact)
  → Name (required), Address (required + validated), Notes (optional)
  → QR scanner for address field
  → SaveContact() → ContactService.AddAsync() or UpdateAsync()

Actions per contact:
  → SendToContact() → navigate /send?to=address
  → EditContact() → pre-fill form
  → DeleteContact() → confirm → ContactService.DeleteAsync()
```

### Payments (`/payments`)
```
Create payment request:
  → ShowCreateForm()
  → Fields: Amount (optional), Label, Message
  → CreateRequest():
    1. WalletService.GetReceiveAddressAsync()
    2. Build BIP21 URI: digibyte:ADDRESS?amount=X&label=Y&message=Z
    3. PaymentRequestService.SaveAsync(request)
    4. GenerateQr(uri) → QRCoder PNG

View request:
  → QR code display
  → URI display (monospace)
  → CopyUri() → clipboard
  → ShareUri() → navigator.share()

History:
  → List of past payment requests with label, amount, date
  → Click to expand/view QR
```

### Identity — Digi-ID (`/identity`)
```
How it works (info section):
  1. Website shows Digi-ID QR code
  2. Scan with wallet
  3. Wallet signs challenge → sends to website

Scan flow:
  → ScanDigiId() → open QrScannerModal
  → OnQrScanned(result):
    1. Parse digiid:// URI
    2. Extract domain, callback URL, nonce
    3. Show confirmation popup (domain + security warning if HTTP)

Approve:
  → ConfirmAuthAsync():
    1. Derive Digi-ID key: m/13'/siteIndex'/0'/0
    2. Sign challenge message with derived key
       (the API proxy derives the callback URL from the signed URI, verifies the signature, refuses http,
        redirects and private addresses, and answers refusals with {"source":"proxy","reason":...})
    3. POST signature to callback URL
    4. Success → show "Authenticated with {domain}"
    5. Prepend entry (domain, address, UTC timestamp) to history list
    6. Trim to 50 entries → SaveAsync("digiid_login_history") → IndexedDB
    7. Error → show error message + "Try Again" button

Login history:
  → OnInitializedAsync() → LoadHistoryAsync():
    1. Storage.GetAsync("digiid_login_history") → deserialize JSON
    2. Render entries: domain, truncated address (dgb1abc…xyz), relative time
  → ClearHistoryAsync():
    1. Clear list in memory
    2. Storage.RemoveAsync("digiid_login_history")

Manual entry:
  → Paste digiid:// URI → press Sign → same flow as scan

Address warning (every place a Digi-ID address is shown in full or can be copied):
  → DigiIdAddressWarning: "identity address, not watched by the wallet, never use it to receive payments",
    with a link to /identity#recover-funds. Shown under the signing address (approval panel), under
    "Address used" (success panel), in the login detail dialog, and in Help. The shortened address in a
    history row is exempt: tapping the row opens the dialog, which carries the warning.
```

#### Recover funds (`/identity#recover-funds`)

A Digi-ID address is a real DigiByte address that the wallet never watches. If someone sends DGB to one, the
funds are not lost, since the key comes from the seed, but they are invisible. This section finds and moves them.
It needs an unlocked wallet made from a seed phrase (watch-only and single-key wallets have no Digi-ID addresses).

```
Sites to check: login history (digiid_login_history) + websites the user types in
  → DigiIdDomain.Normalize(): same host string a login signs with (lowercase, no scheme/port/path)
  → the address is derived from the site with THIS wallet; the address saved in the history is never trusted

Check (ScanDigiIdFundsAsync):
  → one GetVerifiedUtxosAsync request PER ADDRESS (4 at a time), never batched:
    an explorer must not learn that two sites belong to the same person
  → a lookup that fails is "Could not check" (with Retry), never "nothing found"
  → confirmed and pending are shown; only confirmed can be swept
  → outputs the address's own standard script does not describe (a token, say) are left untouched
    and reported as "other outputs left untouched"

Sweep (PlanDigiIdSweepAsync → review → ExecuteDigiIdSweepAsync):
  → plan: re-check each address, estimate the fee (Legacy inputs, never below 100 sat/vB),
    drop addresses whose net would be under 0.0001 DGB, give each remaining address its OWN fresh
    receive address; nothing is saved or sent, so Cancel leaves the wallet as it was
  → at most 18 to 19 addresses per confirmation: the highest destination plus one is saved as the next
    receive index, and must stay inside the 20 unused addresses a restore from seed can reach
    (the address the Receive page already shows is one of those 20)
  → confirm once: save the new receive index first (if that fails, nothing is sent), then for each
    address in turn: record "Sending" (this blocks a second sweep of it), re-check, sign with the site key,
    broadcast ONE transaction for that address alone, record "Pending" and add it to the history
  → one address failing does not stop the others; its funds stay on the Digi-ID address
  → if the wallet locks part way, nothing more is sent and the rest are recorded "Failed: wallet locked"

Status (GetDigiIdSweepsAsync, RefreshDigiIdSweepsAsync):
  → records are saved per wallet (digiid_sweeps_{walletId}) and hold no key; the list shows at once on
    open, then is refreshed in the background, on the Refresh button, and about every 30 seconds
    while the section is open and a sweep is pending (never while the page is closed)
  → Pending → Confirmed at one confirmation; Pending → Failed if the network does not know it for 10 minutes;
    a sweep interrupted before sending, with the funds still there, becomes Failed so it can be tried again
```

### Settings (`/settings`)
```
Sections:
  → Theme toggle (Light/Dark) → ThemeService.Toggle()
  → Display mode (Simple/Advanced) → AppState.DisplayMode
  → Currency selector (USD, EUR, PHP, JPY, etc.) → AppState.Currency
  → Network selector (Mainnet/Testnet/Regtest) → AppState.Network
  → Language selector (10 locales) → LocalizationService.SetLocale()
  → "Back Up Seed Phrase" → navigate /backup-seed
  → "Spending Limits" → navigate /settings/spending-limits (hidden for xpub)
  → Links: About, Roadmap, Analytics, Deployment
  → "Delete Wallet" (danger zone) → confirmation
    → SpendingLimitService.DeleteSettingsAsync(walletId)
    → BiometricService.DisableAsync()
    → WalletService.WipeAllDataAsync()

All preferences persisted to localStorage via AppState.
```

### Spending Limits (`/settings/spending-limits`)
```
Step 1 — PIN verification:
  → PinInput (6 digits)
  → WalletService.UnlockWalletAsync(walletId, pin)
  → Incorrect → error message + shake
  → Correct → pinVerified = true, show settings controls

Step 2 — Configure limits:
  → Enable/disable toggle → auto-save
  → Daily / Weekly / Monthly DGB limit inputs (number, min 0)
  → Alert threshold selector (60% / 70% / 80% / 90%)
  → Hard block toggle (Block Sends vs Warn Only)
  → Each change → SaveSettingsAsync() → 2s green confirmation toast

Current spending summary:
  → GetSpentInPeriodAsync(24h / 7d / 30d)
  → Progress bars: green (<threshold), amber (>=threshold), red (>limit)
  → Label: "X / Y DGB" for each period
```

### Sign a PSBT (`/settings/psbt-sign`, Advanced mode)
```
Load (paste base64 or upload a .psbt file, up to 1 MB):
  → PsbtSigningService.LoadAsync → PsbtDocument.FromText / FromBytes
      Refused before review: TooLarge, NotAPsbt, InvalidStructure
  → IPsbtKeyProvider.GetOwnedScriptsAsync (waits up to 20s for address discovery)
  → Chain facts per input:
      This wallet's inputs → IBlockchainService.GetVerifiedUtxosAsync (throws → ChainStatus Unavailable)
      Inputs with no data  → GetTransactionAsync (failed or not found → InputNotIdentified, with Retry)
      Other participants' inputs with data → not looked up (fee unverified until Story 4)
  → PsbtAnalyzer.Analyze → ReviewSummary
      Owner by script only; PSBT key paths ignored
      Refusals checked in order; the first that applies wins
      Warnings: lock time, unconfirmed, non-standard output, high fee (>10%),
                unverified fee, shared outputs not checked, out-of-range note

Review shows: total leaving (or received), every output with full address,
              fee and its status, inputs to sign, inputs belonging to others

Sign (only when CanSign):
  → PsbtConfirmationFlow, stopping at the first failure:
      1. Required acknowledgment
      2. SpendingLimitService.CheckLimitAsync(NetAmount, or 0 when the wallet gains)  — fails open
      3. Baku send-risk warning when every input is the wallet's                   — fails open
      4. Biometrics if enabled for sending, else PIN (VerifyPinAsync + PinAttemptThrottle)
  → PsbtSigningService.SignAsync
      Repeats every chain check, fills missing SegWit data from the chain,
      signs only the wallet's unsigned inputs (one at a time, SIGHASH_ALL)
  → Result: complete / N inputs still need signatures; copy base64 or download .psbt

Broadcast (complete, with a wallet input and spending keys):
  → Confirmation listing outputs and fee
  → PsbtSigningService.BroadcastAsync: finalizes again, sends, records one history entry
  → Same cache clearing as Send
Already fully signed PSBT: same confirmation flow → PrepareSignedAsync → broadcast confirmation

Locking the wallet discards the PSBT. Watch-only wallets can review only.
```

### BackupSeed (`/backup-seed`)
```
Step 1 — PIN verification:
  → PinInput (6 digits)
  → VerifyPin() → WalletService.UnlockAsync(pin)
  → Incorrect → error message + shake

Step 2 — Seed display:
  → GetSeedPhraseAsync() → decrypt from IndexedDB
  → Show 24 words in numbered 3-column grid
  → Warning banner: "Never share these words"
  → Done button → navigate /settings
```

### Analytics (`/analytics`)
```
OnInitializedAsync():
  → LoadPrice() → CoinGecko API: /api/v3/coins/digibyte
  → LoadBlockHeight() → FallbackBlockchainService.GetBlockHeightAsync()

Displays:
  → Price card: DGB/USD + 24h change %
  → Market stats grid: market cap, 24h volume, circulating supply, block height
  → Network stats: block time (~15s), 5 MultiAlgo, 21B max supply, SegWit status
  → 7-day sparkline chart from CoinGecko
```

### P2P Marketplace (`/p2p`)
```
Placeholder page — "Coming soon"
  → Tab structure: Buy DGB | Sell DGB | My Orders
  → No functional logic yet
  → Will use: SignalR (TradeChatHub), 2-of-3 multisig escrow, DigiByte.Api backend
```

### Remittance (`/remittance`)
```
Recipient lookup:
  → Enter username/phone → Find button
  → Resolve to DGB address via directory API
  → Show resolved address → Send button → navigate /send

Fee comparison:
  → Enter dollar amount
  → Display comparison table: Western Union, Wise, Remitly, DigiByte
  → Highlight DGB as lowest cost with "You save $X" callout
```

### Multisig Wallets (`/multisig`)

#### Ownership and scoping (spec 016)
```
Every multisig list entry carries OwnerWalletIds (the app wallets that own it); drafts carry one OwnerWalletId.
  → Create/Import add the active wallet id as an owner
  → ListWalletsAsync(ownerWalletId, ownPublicKeyHex) returns only entries the active wallet owns
  → Entries saved before scoping (no owner) are attributed to a wallet whose public key is one of the
    co-signer keys (two wallets that both hold keys each become owners); nothing is deleted
  → Unmatched entries are hidden everywhere and shown under "Unassigned multisigs" with a Claim action
  → GetAccessStateAsync → Owned | OtherWallet | Unassigned | NotFound (direct links never reveal other wallets' data)
  → DeleteWalletAsync(id, ownerWalletId) removes only that wallet as owner; data is deleted with the last owner
  → Names and co-signer labels are local, shared by all owner wallets, never part of the script
```

#### Dashboard, Activity (`/multisig/{id}`, `/multisig/{id}/activity`)
```
Dashboard: header (editable name, M-of-N) → balance (loading/unavailable states, never 0 when unknown)
  → address (Copy, QR) → Send / Receive / Pending → "needs your signature" call to action
  → backup card (until BackupAt is set by Download or Copy) → collapsible Co-signers (full keys) and Recent activity
Activity: GetActivityAsync(config, skip, take = 25) = chain transactions (paged) + local proposals
  → MultisigActivityBuilder: Received / Sent (change excluded) / proposal kinds; a broadcast proposal shows once
  → chain unreachable → local proposals only, marked partial
```

#### Create Multisig Wallet (`/multisig/create`)
```
Step 1 — Set Threshold:
  → Choose M (required signatures) and N (total signers)
  → Validate: 1 ≤ M ≤ N ≤ 15

Step 2 — Add Co-Signers:
  → Own public key auto-added from WalletService.GetPublicKey()
  → Add N-1 external co-signers (name + compressed public key hex)
  → Validate each key (33 bytes, starts with 02 or 03)

Step 3 — Confirm & Create:
  → MultisigService.CreateRedeemScript(M, publicKeys) → BIP67 sorted keys
  → MultisigService.GetP2SH_P2WSH_Address() or GetP2WSH_Address()
  → MultisigWalletService.CreateMultisigWalletAsync() → persist to IndexedDB
  → Navigate to /multisig/{walletId}
```

#### Import Multisig Wallet (`/multisig/import`)
```
User pastes redeem script (hex):
  → MultisigWalletService.ImportMultisigWalletAsync(redeemScriptHex)
  → Parse script: extract M, N, and public keys
  → Detect if user's key is among co-signers (ownKeyIndex ≥ 0 = signing, -1 = watch-only)
  → Derive address → persist config → navigate to detail
```

#### Send from Multisig (`/multisig/{id}/send`)
```
User types, pastes, scans (address or digibyte: link) or picks a contact; amount or MAX:
  → MultisigSendHelper.ValidateDestination / ClassifyScan / EstimateSend (fee = vsize x sat/vB, change below the
    0.001 DGB dust threshold is added to the fee) → review screen → Confirm
  → MultisigWalletService.CreateSpendingPSBTAsync(config, address, amount, feeRate, memo, submissionToken)
    1. Fetch UTXOs for multisig address, choose coins (largest first)
    2. MultisigService.CreateMultisigPSBTWithFee(redeemScript, coins, dest, amount, change, fee, dust)
    3. Create PendingMultisigTransaction (with UnsignedTxId) → persist to IndexedDB
    4. Return PSBT base64 for sharing with co-signers; the same submissionToken never creates a second proposal

Signing flow:
  → User signs: MultisigWalletService.SignPendingTransactionAsync(walletId, txId)
    1. WalletService.GetPrivateKeyForMultisig() → get signing key
    2. MultisigService.SignPSBT(psbt, privateKey, redeemScript)
    3. Update PendingMultisigTransaction.SignedBy list
  → Co-signer signs: MultisigWalletService.ImportSignedPSBTAsync(pending, signedPsbtBase64)
    1. Verify the copy is for the same unsigned transaction (UnsignedTxId) and signed only by keys of the redeem script
       (otherwise PsbtImportException with a plain reason)
    2. MultisigService.CombinePSBTs(original, signed)
    3. Update SignedBy; at the threshold status becomes ReadyToFinalize
  → Receive a co-signer's proposal (Pending → Receive proposal panel, or Scan on the dashboard; specs/022-multisig-proposal-scan-import):
    scan its QR, paste it, or choose a file; all three go through one handler. No server involved.
    1. MultisigSendHelper.ClassifyScan must say Psbt (an address or payment link is refused with a plain reason)
    2. MultisigWalletService.ReceiveProposalAsync(config, psbt): recipient/amount/fee come from the PSBT itself
       (MultisigProposalReader); a PSBT spending coins of another multisig, or carrying a signature from a key that is not
       a co-signer, is refused in full (PsbtImportException) and nothing is saved
    3. Outcome: Added (new item, ReceivedFromCoSigner = true, labelled "Received from a co-signer"), Merged (verified
       signatures added), AlreadyUpToDate (one entry per UnsignedTxId, never a duplicate), Cancelled, Closed (already sent)
    4. Cancelled here → "Restore this proposal" asks first; RestoreCancelledAsync re-checks and merges, nothing silent
    5. Spends another multisig of the same owner → FindWalletForProposalAsync offers "Open <name>"; the PSBT travels
       in memory only (ProposalHandoff), never in a URL, and is not added automatically
    6. Open proposals whose coins are already spent elsewhere (GetSpentInputIdsAsync; marks nothing when the chain is
       unreachable) show "Can't be signed – coins already spent": Sign, Sign together online and Broadcast are disabled,
       Cancel and Share stay
  → Cancel: CancelPendingAsync marks Cancelled on this device only (co-signers with a copy could still broadcast)
  → Remove: RemoveCancelledAsync(walletId, pendingId?) deletes Cancelled proposals from this device (one, or all when no id);
    only Cancelled can be removed; local only; they also leave Activity; "Clear all cancelled" asks for confirmation first

Finalize & broadcast:
  → MultisigWalletService.FinalizeAndBroadcastAsync(walletId, txId)
    1. MultisigService.CanFinalize(psbt) → verify enough signatures
    2. MultisigService.FinalizePSBT(psbt) → complete transaction
    3. Extract raw tx → IBlockchainService.BroadcastTransactionAsync()
    4. Update status → Broadcast, store txid
```

#### Sign Together Online (`/multisig/sign-room`, specs/017-multisig-online-signing)
```
Optional, next to the offline Share/Import flow. Hub: DigiByte.Api /hubs/multisig-signing (co-signer coordination
relay, constitution 1.2.0): public data only, in memory, anonymous counts in logs, never needed for signing.

Open (Pending → Sign together online, co-signers only, never watch-only):
  → SigningRoomClient.CheckAvailabilityAsync (5 s; only now is the hub contacted) → GetOptions (15/30/60/120 min, operator cap)
  → RequestChallenge → CoSignerProof.Sign(key, SigningRoomDomain, "open", UnsignedTxId, nonce, connectionId)
  → OpenRoom: hub checks proof, key ∈ co-signers, every input spends the multisig (P2WSH or P2SH-P2WSH)
    → room exists for this payment? ExistingRoom(code) only to a key of that room, else "Invalid or expired code"
    → else new room (6-char code), RoomState with the PSBT

Join (/multisig/sign-room?code=…; locked wallet → /unlock?returnUrl=… → back):
  → RequestChallenge → proof ("join", code) → JoinRoom; unknown code, bad proof and non-member key all look the same
    (5 failures/min per connection → "Too many attempts, try again shortly")
  → Wallet: same network? → FindByRedeemScriptAsync(keys, M) (own key required) → CheckProposalMatches
    → AdoptProposalAsync: recipient/amount/fee/memo read from the PSBT (MultisigProposalReader), never from the room
    → MergeRoomSignaturesAsync for each room copy (each signature checked against its input's sighash)
    → signatures held only here (e.g. imported offline) → SubmitSignature

Sign: SignPendingTransactionAsync (saved locally first) → SubmitSignature → hub verifies every signature, stores,
  relays SignatureAdded + RoomState → every wallet merges (verified) into its own proposal
Broadcast (local copy has M signatures): ClaimBroadcast (one holder; released on failure, after 60 s or on
  disconnect) → FinalizeAndBroadcastAsync ("already known" = success) → ReportBroadcast → Broadcasted(txid) →
  every wallet MarkBroadcastAsync → shows once in Activity as Sent
Reconnect: automatic, then Rejoin with a fresh proof; offline participants keep their place and signature
End: lifetime over / creator closes / broadcast + 60 s → RoomEnded; proposals and signatures stay on every device
```

### About, Roadmap, DeploymentInfo, NotFound
```
/about        → Static: version, license, tech stack, GitHub contributors (fetched from API)
/roadmap      → Static: visual timeline with Done/In Progress/Planned milestones
/deployment   → Infrastructure status, cost breakdown, donation address with:
                 - Live DGB balance + USD conversion (CoinGecko via IBlockchainService)
                 - Cached data (5-min TTL via MemoryCacheService, donation:balance/price keys)
                 - Refresh button (clears cache prefix, re-fetches)
                 - Copy address button (clipboard + "Copied!" feedback)
                 - Mainnet-forced queries (SetNetworkMode temporary switch)
/donations    → Interactive donation transaction history:
                 - Per-transaction USD value at current price
                 - Cached tx list (donation:txs key, 5-min TTL)
                 - Refresh button + mainnet-forced queries
                 - ScriptPubKey hex matching for accurate tx filtering
/help         → Static: accordion help sections (14 topics), search/filter, report issue / suggest feature (pre-filled GitHub URL)
/help/multisig → Static: comprehensive multisig guide — visual flows, real-world scenarios (2-of-2 joint, 2-of-3 backup/escrow, 3-of-5 treasury), step-by-step walkthroughs (create, send, import), technical details (BIP67, BIP174, P2SH-P2WSH/P2WSH), wallet type comparison table
/not-found    → 404 error page
```
