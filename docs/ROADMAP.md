# Roadmap

## Completed

### Phase 1 — Wallet Core
- [x] BIP39/BIP44 HD wallet (24-word mnemonic)
- [x] AES-256-GCM encrypted seed storage (PBKDF2 key derivation)
- [x] Send/Receive with QR codes
- [x] Real transaction signing + broadcasting (NBitcoin)
- [x] WIF private key import (auto network detection)
- [x] Multi-network (Mainnet, Testnet, Regtest)
- [x] Light/Dark theme with DigiByte branding

### Phase 2 — Features
- [x] Contact book with CRUD, QR scan, format badges
- [x] Payment requests (BIP21 URIs with QR)
- [x] Digi-ID passwordless authentication
- [x] Advanced mode (fee control, UTXO info)
- [x] Fiat/DGB amount toggle with CoinGecko pricing
- [x] Remittances with fee comparison
- [x] NFC tap-to-pay (Web NFC API)

### Phase 4 — Infrastructure
- [x] Node API (87 RPC methods + Scalar docs)
- [x] Docker regtest (instant mining, local dev)
- [x] Docker testnet (real network)
- [x] Docker mainnet pruned (`prune=550`, tuned for Railway)
- [x] Multi-explorer fallback (Blockbook → Esplora → NOWNodes → Own node)
- [x] Blockbook API adapter (returns scriptPubKey in UTXOs)
- [x] 2-minute cooldown on failed explorers
- [x] Read/write separation (explorers for reads, own node for broadcasts)
- [x] Mock limited to development only (production throws)
- [x] Railway deployment config (`railway.toml` with 3 services)
- [x] Toast notifications
- [x] Localization (10 languages)

### UX Polish
- [x] OTP-style PIN input with auto-focus + shake animation
- [x] QR code scanner (camera-based + file upload fallback)
- [x] Loading skeletons
- [x] Send success animation
- [x] Transaction detail modal with explorer link
- [x] Address format labels (Legacy/SegWit/P2SH)
- [x] Forgot PIN recovery + Delete wallet
- [x] Full-viewport modal backdrops
- [x] Pull-to-refresh on Home dashboard
- [x] Onboarding walkthrough (5-step carousel)
- [x] Offline banner

### Open Source & Documentation
- [x] LICENSE, CONTRIBUTING, CODE_OF_CONDUCT
- [x] GitHub issue/PR templates
- [x] Architecture documentation
- [x] Process flow documentation (per-page technical flows)
- [x] Comprehensive README with configs, run/test instructions

## In Progress

### API Documentation
- [ ] Node API OpenAPI/Swagger export
- [ ] API usage examples in docs

## Planned

### Phase 3 — P2P Exchange
- [ ] Order book (buy/sell offers)
- [ ] 2-of-3 multisig escrow
- [ ] Trade flow (initiate → pay → release)
- [ ] SignalR real-time chat (TradeChatHub)
- [ ] Reputation system (ratings, badges)
- [ ] EF Core + PostgreSQL

### UX Enhancements
- [ ] Transaction search/filter (date, amount, direction)
- [ ] Biometric unlock (WebAuthn fingerprint/face)
- [ ] First-time walkthrough improvements
- [ ] Custom app icon + splash screen

### Advanced Features
- [ ] Multiple wallet support (switch between wallets)
- [ ] Address book import/export (CSV)
- [ ] Transaction history CSV export
- [ ] DigiAssets (custom token support)
- [ ] Watch-only wallets (xpub import)
- [ ] Spending limits / transaction alerts

### Technical Debt
- [ ] Tailwind CLI build (replace CDN for production)
- [ ] PWA offline mode improvements
- [ ] Unit tests for WalletService + UI components
- [ ] E2E test suite (Playwright)
- [ ] Performance optimization (lazy loading)
- [ ] Backup seed phrase retrieval from encrypted storage

### Deployment
- [ ] Railway production deployment (PWA + Node API + pruned node)
- [ ] CI/CD pipeline (GitHub Actions)
- [ ] Automated testing on PR
- [ ] Docker production images (multi-stage builds)
