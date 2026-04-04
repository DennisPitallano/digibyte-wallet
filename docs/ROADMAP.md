# Roadmap

## Completed

### Phase 1 — Wallet Core
- [x] BIP39/BIP44 HD wallet (24-word mnemonic)
- [x] AES-256-GCM encrypted seed storage
- [x] Send/Receive with QR codes
- [x] Real transaction signing + broadcasting
- [x] WIF private key import
- [x] Multi-network (Mainnet, Testnet, Regtest)
- [x] Light/Dark theme with DigiByte branding

### Phase 2 — Features
- [x] Contact book with CRUD, QR scan, format badges
- [x] Payment requests (BIP21 URIs)
- [x] Digi-ID passwordless authentication
- [x] Advanced mode (fee control, UTXO info)
- [x] Fiat/DGB amount toggle

### Phase 4 — Infrastructure
- [x] Node API (87 RPC methods + Scalar docs)
- [x] Docker (regtest + testnet)
- [x] Remittances with fee comparison
- [x] NFC tap-to-pay (Web NFC API)
- [x] Toast notifications
- [x] Localization (10 languages)

### UX Polish
- [x] OTP-style PIN input with auto-focus + shake animation
- [x] QR code scanner (camera-based)
- [x] Loading skeletons
- [x] Send success animation
- [x] Transaction detail modal with explorer link
- [x] Address format labels (Legacy/SegWit/P2SH)
- [x] Forgot PIN recovery + Delete wallet
- [x] Full-viewport modal backdrops

## In Progress

### Open Source & Documentation
- [x] LICENSE, CONTRIBUTING, CODE_OF_CONDUCT
- [x] GitHub issue/PR templates
- [x] Architecture documentation
- [x] Process flow documentation
- [ ] API documentation (Node API)

## Planned

### Phase 3 — P2P Exchange
- [ ] Order book (buy/sell offers)
- [ ] 2-of-3 multisig escrow
- [ ] Trade flow (initiate → pay → release)
- [ ] SignalR real-time chat
- [ ] Reputation system (ratings, badges)
- [ ] EF Core + PostgreSQL

### UX Enhancements
- [ ] Pull-to-refresh on Home dashboard
- [ ] Transaction search/filter (date, amount, direction)
- [ ] Biometric unlock (WebAuthn fingerprint/face)
- [ ] Onboarding tutorial / first-time walkthrough
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
- [ ] Railway deployment (PWA + Node API + DigiByte node)
- [ ] CI/CD pipeline (GitHub Actions)
- [ ] Automated testing on PR
- [ ] Docker production images
