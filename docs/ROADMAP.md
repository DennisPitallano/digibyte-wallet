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
- [x] Configurable fee rates (Low/Normal/Fast presets, sat/vB control)
- [x] Fiat/DGB amount toggle with CoinGecko pricing
- [x] Multi-currency fiat — USD ($), EUR (€), GBP (£), PHP (₱), JPY (¥)
- [x] Remittances with fee comparison
- [x] NFC tap-to-pay (Web NFC API)

### Phase 4 — Infrastructure
- [x] Node API (87 RPC methods + Scalar docs)
- [x] Docker regtest (instant mining, local dev)
- [x] Docker testnet (real network)
- [x] Docker mainnet pruned (`prune=550`, tuned for Railway)
- [x] Multi-explorer fallback (Esplora → Own node)
- [x] Polly resilience — per-client retry, circuit breaker, timeout
- [x] Two-tier caching — MemoryCacheService (TTL + dedup) + IndexedDB persistent
- [x] Production log suppression (HTTP/Polly → Warning; Dev → Information)
- [x] Railway deployment config (`railway.toml` with 3 services)
- [x] Toast notifications

### Security & Stability
- [x] PIN lockout — exponential backoff after 3 failed attempts
- [x] Global error boundary — styled crash recovery screen
- [x] Content Security Policy — all assets self-hosted (Tailwind, Inter font, jsQR)
- [x] MemoryCacheService TryGet fix for value types
- [x] CoinGecko price — throw on failure for proper fallback chain
- [x] Currency cache invalidation on fiat change
- [x] IndexedDB zero-guards (no storing/loading 0 prices)

### UX Polish
- [x] Self-hosted Tailwind CSS v3 (purged ~37KB)
- [x] Self-hosted Inter font (woff2)
- [x] Self-hosted jsQR (CSP compliant)
- [x] DigiByte favicon and app icons (SVG + PNG)
- [x] OTP-style PIN input with auto-focus + shake animation
- [x] QR code scanner (camera-based + file upload)
- [x] Loading skeletons (balance, price, transactions)
- [x] Send page form disabled during loading
- [x] Real-time amount warnings (exceeds balance, below minimum)
- [x] Send success animation
- [x] Transaction detail modal with explorer link
- [x] Address format labels (Legacy/SegWit/P2SH)
- [x] Forgot PIN recovery + Delete wallet + close button
- [x] Pull-to-refresh on Home dashboard
- [x] Onboarding walkthrough (5-step carousel)
- [x] Offline banner
- [x] Version and tagline on Unlock/CreateWallet pages
- [x] Developer tools guard (Development only)

### Open Source & Documentation
- [x] LICENSE, CONTRIBUTING, CODE_OF_CONDUCT
- [x] GitHub issue/PR templates
- [x] Architecture documentation
- [x] Process flow documentation (per-page technical flows)
- [x] Comprehensive README with configs, run/test instructions

## In Progress

### Alpha Release Prep
- [ ] Multi-language support (10 locales defined, translation files pending)
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

### Multi-Language
- [ ] Wire up L10n.T() across all pages
- [ ] Complete translation files (8 of 10 locales pending)
- [ ] Component re-render on locale change

### UX Enhancements
- [ ] Transaction search/filter (date, amount, direction)
- [ ] Biometric unlock (WebAuthn fingerprint/face)
- [ ] Batch send UI (TransactionBuilder supports it)
- [ ] Transaction confirmation detail view (inputs/outputs/fee)

### Advanced Features
- [ ] Multiple wallet support (switch between wallets)
- [ ] Address book import/export (CSV)
- [ ] Transaction history CSV export
- [ ] DigiAssets (custom token support)
- [ ] Watch-only wallets (xpub import)
- [ ] Spending limits / transaction alerts
- [ ] Hardware wallet support (Ledger/Trezor)

### Technical Debt
- [ ] PWA offline mode improvements (background sync for pending tx)
- [ ] Service worker update notification
- [ ] Unit tests for WalletService + UI components
- [ ] E2E test suite (Playwright)
- [ ] Performance optimization (lazy loading)

### Deployment
- [ ] Railway production deployment (PWA + Node API + pruned node)
- [ ] CI/CD pipeline (GitHub Actions)
- [ ] Automated testing on PR
- [ ] Docker production images (multi-stage builds)
