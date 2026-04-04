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
Blockbook (digibyteblockexplorer.com)
  ↓ failed? (2-min cooldown)
Esplora (digiexplorer.info)
  ↓ failed? (2-min cooldown)
NOWNodes Blockbook (dgb-explorer.nownodes.io)
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
Blockbook (digibyteblockexplorer.com, GET /api/v2/sendtx)
  ↓ failed?
Esplora (digiexplorer.info, POST /tx)
  ↓ failed?
NOWNodes Blockbook (dgb-explorer.nownodes.io)
  ↓ failed?
Error — "Broadcast failed on all backends"
```
Broadcast **never** falls back to mock, even in development.

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
  → HdKeyDerivation(mnemonic, network) → m/44'/20'/0'/0/0
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
    → Blockbook (digibyteblockexplorer.com)     [2-min cooldown on failure]
    → Esplora (digiexplorer.info)                [2-min cooldown on failure]
    → NOWNodes Blockbook (dgb-explorer.nownodes.io) [2-min cooldown on failure]
    → Own pruned node (scantxoutset)             [last resort for reads]
    → Development: MockBlockchainService (demo data)
    → Production: InvalidOperationException

Transaction Broadcast (writes):
  FallbackBlockchainService.BroadcastTransactionAsync(rawTx)
    → Own node first (sendrawtransaction — most reliable)
    → Blockbook (GET /api/v2/sendtx/{hex})
    → Esplora (POST /tx)
    → NOWNodes Blockbook
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
  → Footer links: /about, /roadmap, /analytics, /deployment
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
    3. HdKeyDerivation(mnemonic, network) → m/44'/20'/0'/0/0
    4. AppState.IsUnlocked = true
    5. Navigate to / (Home)
```

### RecoverWallet (`/recover-wallet`)
```
Tab 1 — Seed Phrase:
  → Textarea for 12 or 24 words
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
  → WalletService.GetBalanceAsync() → FallbackBlockchainService (explorer chain)
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
  → PreviewSend() → validate all fields → show confirmation modal
  → Modal shows: recipient, amount, fee, total

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

WIF wallets: fixed Legacy + SegWit addresses (no "generate new")
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
    3. POST signature to callback URL
    4. Success → show "Authenticated with {domain}"
    5. Save to recent logins list

Manual entry:
  → Paste digiid:// URI → press Sign → same flow as scan
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
  → Links: About, Roadmap, Analytics, Deployment
  → "Delete Wallet" (danger zone) → confirmation → WalletService.DeleteWalletAsync()

All preferences persisted to localStorage via AppState.
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

### About, Roadmap, DeploymentInfo, NotFound
```
/about        → Static: version, license, tech stack, GitHub contributors (fetched from API)
/roadmap      → Static: visual timeline with Done/In Progress/Planned milestones
/deployment   → Static: infrastructure status badges, cost breakdown, donation address
/not-found    → 404 error page
```
