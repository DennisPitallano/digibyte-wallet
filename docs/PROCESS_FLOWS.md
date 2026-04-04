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
Balance Request:
  FallbackBlockchainService.GetBalanceAsync(addresses)
    → Try NodeApiBlockchainService (localhost:5260/api/address/{addr}/balance)
    → Try BlockchainApiService (digiexplorer.info/api/address/{addr})
    → Fall back to MockBlockchainService (demo data)

Transaction Broadcast:
  FallbackBlockchainService.BroadcastTransactionAsync(rawTx)
    → Try NodeApi first (POST /api/tx/broadcast)
    → Try Explorer (POST digiexplorer.info/api/tx)
    → NEVER falls back to mock (must succeed or throw)
```
